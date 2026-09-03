using CashFlow.Connectors.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Connectors.Statements;

public static class DependencyInjection
{
    public static IServiceCollection AddStatementParsers(this IServiceCollection services)
    {
        services.AddSingleton<IStatementParser, TBankOperationsParser>();
        services.AddSingleton<IStatementParser, SberPdfStatementParser>();
        return services;
    }
}
