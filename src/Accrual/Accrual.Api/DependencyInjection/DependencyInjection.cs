using System.Text.Json.Serialization;
using Accrual.Application.DependencyInjection;
using Accrual.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace Accrual.Api.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddAccrual(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is required.");

        services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Accrual", Version = "v1" });
            options.MapType<Contracts.SchemaType>(() =>
            {
                var schema = new OpenApiSchema { Type = "string" };
                schema.Enum = new List<IOpenApiAny> { new OpenApiString("Linear"), new OpenApiString("Fibonacci") };
                return schema;
            });
        });
        services.AddAccrualApplication();
        services.AddAccrualInfrastructure(configuration);
        services.AddHealthChecks().AddNpgSql(connectionString, name: "database", tags: ["ready"]);
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));
        return services;
    }
}
