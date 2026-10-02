using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Prometheus;
using Wallet.Api.Clients;
using Wallet.Api.Data;
using Wallet.Api.Observability;
using Wallet.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.SwaggerDoc("v1", new() { Title = "Wallet", Version = "v1" }));

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Connection string 'Database' is required.");

builder.Services.AddDbContext<WalletDb>(options =>
{
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(5));
    options.UseSnakeCaseNamingConvention();
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IWalletMetrics, WalletMetrics>();
builder.Services.AddScoped<InboxService>();
builder.Services.AddScoped<WalletQueryService>();
builder.Services.AddScoped<PayoutProcessor>();
builder.Services.Configure<PayoutOptions>(builder.Configuration.GetSection(PayoutOptions.SectionName));
builder.Services.AddHostedService<PayoutWorker>();

builder.Services.AddTransient<CorrelationPropagationHandler>();
var accrualAddress = builder.Configuration["Accrual:BaseAddress"];
if (string.IsNullOrWhiteSpace(accrualAddress))
    throw new InvalidOperationException("Accrual:BaseAddress is required.");
if (!accrualAddress.EndsWith('/'))
    accrualAddress += "/";

builder.Services.AddHttpClient<IAccrualClient, AccrualHttpClient>(client => client.BaseAddress = new Uri(accrualAddress))
    .AddHttpMessageHandler<CorrelationPropagationHandler>()
    .AddHttpMessageHandler(sp => new HttpClientErrorHandler(sp.GetRequiredService<IWalletMetrics>(), "accrual"))
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 2;
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 4;
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    });

builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "database", tags: ["ready"]);
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

await DatabaseStartup.MigrateAsync<WalletDb>(app);
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
