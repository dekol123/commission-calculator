using Microsoft.Extensions.DependencyInjection;

namespace Accrual.Application.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddAccrualApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        return services;
    }
}
