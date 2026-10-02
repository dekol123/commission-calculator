using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using Users.Api.Data;
using Users.Api.Observability;
using Users.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.SwaggerDoc("v1", new() { Title = "Users", Version = "v1" }));

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Connection string 'Database' is required.");

builder.Services.AddDbContext<UsersDb>(options =>
{
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(5));
    options.UseSnakeCaseNamingConvention();
});
builder.Services.AddScoped<UserTreeService>();
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "database", tags: ["ready"]);

builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));
builder.WebHost.UseShutdownTimeout(TimeSpan.FromSeconds(20));

var app = builder.Build();

app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled");
        if (feature?.Error is not null)
            logger.LogError(feature.Error, "Unhandled exception");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new Contracts.ErrorResponse("Unexpected error."));
    });
});
app.UseHttpMetrics();
app.UseSwagger();
app.UseSwaggerUI();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapMetrics();
app.MapControllers();

await DatabaseStartup.MigrateAsync<UsersDb>(app);
app.Run();

internal static class DatabaseStartup
{
    public static async Task MigrateAsync<TContext>(WebApplication app)
        where TContext : DbContext
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Database");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                logger.LogInformation("Database migrations applied");
                return;
            }
            catch (Exception exception) when (attempt < 15)
            {
                logger.LogWarning(exception, "Database is not ready, attempt {Attempt}", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }
}
