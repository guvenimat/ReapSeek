using Microsoft.Extensions.DependencyInjection;
using ReapSeek.Finance.Application;
using ReapSeek.Finance.Contracts;
using ReapSeek.Finance.Infrastructure;

namespace ReapSeek.Finance;

public static class FinanceModule
{
    public static IServiceCollection AddFinance(this IServiceCollection services)
    {
        services.AddSingleton<SellerFinanceRepository>();
        services.AddSingleton<ISellerFinance, SellerFinance>();
        return services;
    }
}
