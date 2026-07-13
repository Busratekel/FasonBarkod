using FasonBarkod.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace FasonBarkod.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetOrders(CancellationToken cancellationToken)
    {
        var orders = await orderService.GetOrdersAsync(cancellationToken);
        return Ok(orders);
    }

    [HttpGet("{orderNo}/lines")]
    public async Task<IActionResult> GetOrderLines(string orderNo, CancellationToken cancellationToken)
    {
        var order = await orderService.GetOrderDetailAsync(orderNo, cancellationToken);
        if (order is null)
        {
            return NotFound(new { error = $"Sipariş bulunamadı: {orderNo}" });
        }

        return Ok(order);
    }
}
