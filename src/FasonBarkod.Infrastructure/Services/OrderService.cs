using FasonBarkod.Core.Dtos;
using FasonBarkod.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FasonBarkod.Infrastructure.Services;

public class OrderService(ApplicationDbContext context) : IOrderService
{
    public async Task<IReadOnlyList<OrderSummaryDto>> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        return await context.SalesOrderLines
            .GroupBy(x => new { x.SalesOrderNo, x.CustomerName, x.OrderDate })
            .Select(g => new OrderSummaryDto(
                g.Key.SalesOrderNo,
                g.Key.CustomerName,
                g.Key.OrderDate,
                g.Count(),
                g.Sum(x => x.Quantity)))
            .OrderByDescending(x => x.OrderDate)
            .ThenByDescending(x => x.SalesOrderNo)
            .ToListAsync(cancellationToken);
    }

    public async Task<OrderDetailDto?> GetOrderDetailAsync(string salesOrderNo, CancellationToken cancellationToken = default)
    {
        var lines = await context.SalesOrderLines
            .Where(x => x.SalesOrderNo == salesOrderNo)
            .OrderBy(x => x.MaterialCode)
            .Select(x => new OrderLineDto(
                x.Id,
                x.SalesOrderNo,
                x.CustomerCode,
                x.CustomerName,
                x.MaterialCode,
                x.MaterialName,
                x.Color,
                x.BatchNo,
                x.Quantity,
                x.OrderDate))
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            return null;
        }

        return new OrderDetailDto(
            salesOrderNo,
            lines[0].CustomerName,
            lines[0].OrderDate,
            lines);
    }
}
