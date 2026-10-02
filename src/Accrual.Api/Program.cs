using System.Text.Json.Serialization;
using Accrual.Api.Application;
using Accrual.Api.Clients;
using Accrual.Api.Data;
using Accrual.Api.Observability;
using Accrual.Api.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Accrual", Version = "v1" });
    options.MapType<Contracts.SchemaType>(() =>
    {
        var schema = new OpenApiSchema { Type = "string" };
        schema.Enum = new List<IOpenApiAny> { new OpenApiString("Linear"), new OpenApiString("Fibonacci") };
        return schema;
    });
});

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Connection string 'Database' is required.");

builder.Services.AddDbContext<AccrualDb>(options =>
{
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(5));
    options.UseSnakeCaseNamingConvention();
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAccrualMetrics, AccrualMetrics>();
builder.Services.AddScoped<IEventIntakeStore, EventIntakeStore>();
builder.Services.AddScoped<IEventQueryStore, EventQueryStore>();
builder.Services.AddScoped<IClaimStore, ClaimStore>();
builder.Services.AddScoped<PendingCalculationStore>();
builder.Services.AddScoped<OutboxStore>();
builder.Services.AddScoped<SchemaService>();
builder.Services.AddScoped<EventIntakeService>();
builder.Services.AddScoped<ClaimService>();
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddHostedService<PendingCalculationWorker>();
builder.Services.AddHostedService<OutboxDispatcher>();

builder.Services.AddTransient<CorrelationPropagationHandler>();
var usersAddress = RequireBaseAddress(builder.Configuration["Users:BaseAddress"], "Users:BaseAddress");
var walletAddress = RequireBaseAddress(builder.Configuration["Wallet:BaseAddress"], "Wallet:BaseAddress");

builder.Services.AddHttpClient<IUsersGateway, UsersHttpGateway>(client => client.BaseAddress = usersAddress)
    .AddHttpMessageHandler<CorrelationPropagationHandler>()
    .AddHttpMessageHandler(sp => new HttpClientErrorHandler(sp.GetRequiredService<IAccrualMetrics>(), "users"))
    .AddStandardResilienceHandler(HttpResilience.Configure);

builder.Services.AddHttpClient<WalletHttpClient>(client => client.BaseAddress = walletAddress)
    .AddHttpMessageHandler<CorrelationPropagationHandler>()
    .AddHttpMessageHandler(sp => new HttpClientErrorHandler(sp.GetRequiredService<IAccrualMetrics>(), "wallet"))
    .AddStandardResilienceHandler(HttpResilience.Configure);

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

await DatabaseStartup.MigrateAsync<AccrualDb>(app);
app.Run();

static Uri RequireBaseAddress(string? value, string name)
{
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"{name} is required.");

    if (!value.EndsWith('/'))
        value += "/";

    return new Uri(value);
}

internal static class HttpResilience
{
    public static void Configure(HttpStandardResilienceOptions options)
    {
        options.Retry.MaxRetryAttempts = 2;
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 4;
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    }
}

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
