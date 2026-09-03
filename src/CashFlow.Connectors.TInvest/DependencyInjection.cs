using CashFlow.Connectors.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Connectors.TInvest;

public static class DependencyInjection
{
    public static IServiceCollection AddTInvestConnector(this IServiceCollection services)
    {
        services.AddSingleton<IConnector, TInvestConnector>();
        return services;
    }
}
