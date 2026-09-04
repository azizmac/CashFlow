using CashFlow.Connectors.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Connectors.Statements;

public static class DependencyInjection
{
    public static IServiceCollection AddStatementParsers(this IServiceCollection services)
    {
        services.AddSingleton<IStatementParser, TBankOperationsParser>();
        services.AddSingleton<IStatementParser, SberPdfStatementParser>();
        services.AddSingleton<IStatementParser, SberBusinessStatementParser>();
        services.AddSingleton<IStatementParser, SberBusinessOperationsParser>();
        services.AddSingleton<IStatementParser, ClientBankExchangeParser>();
        return services;
    }
}
