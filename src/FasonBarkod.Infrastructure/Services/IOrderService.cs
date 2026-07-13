using FasonBarkod.Core.Dtos;

namespace FasonBarkod.Infrastructure.Services;

public interface IOrderService
{
    Task<IReadOnlyList<OrderSummaryDto>> GetOrdersAsync(CancellationToken cancellationToken = default);

    Task<OrderDetailDto?> GetOrderDetailAsync(string salesOrderNo, CancellationToken cancellationToken = default);
}
