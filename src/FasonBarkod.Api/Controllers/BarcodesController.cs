using FasonBarkod.Core.Dtos;
using FasonBarkod.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace FasonBarkod.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BarcodesController(IBarcodeService barcodeService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetBarcodes(CancellationToken cancellationToken)
    {
        var prints = await barcodeService.GetPrintsAsync(cancellationToken);
        return Ok(prints);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBarcode(int id, CancellationToken cancellationToken)
    {
        var print = await barcodeService.GetPrintAsync(id, cancellationToken);
        if (print is null)
        {
            return NotFound(new { error = $"Etiket kaydı bulunamadı: {id}" });
        }

        return Ok(print);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBarcode([FromBody] CreateBarcodeRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest(new { error = "Adet 0'dan büyük olmalıdır." });
        }

        var print = await barcodeService.CreatePrintAsync(request, cancellationToken);
        if (print is null)
        {
            return BadRequest(new { error = "Etiket oluşturulamadı. Sipariş kalemi veya adet geçersiz." });
        }

        return CreatedAtAction(nameof(GetBarcode), new { id = print.Id }, print);
    }
}
