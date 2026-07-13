using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;
using FasonBarkod.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace FasonBarkod.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SasController(ISapService sapService) : ControllerBase
{
    [HttpGet("{orderNo}/lines")]
    public async Task<IActionResult> GetLines(
        string orderNo,
        [FromQuery] string? vendorCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderNo))
        {
            return BadRequest(new { error = "SAS numarası zorunludur." });
        }

        var lines = await sapService.ListSasAsync(orderNo, vendorCode, cancellationToken);
        if (lines.Count == 0)
        {
            return NotFound(new
            {
                error = $"SAS bulunamadı: {orderNo}. SAP bağlantısı başarılı ancak ZMM_N_SAS_L bu numara/cari ile kalem döndürmedi.",
                hint = $"SE37'de ZMM_N_SAS_L fonksiyonunu EBELN={orderNo} ve KUNNR/LIFNR={(string.IsNullOrWhiteSpace(vendorCode) ? "(boş)" : vendorCode)} ile test edin. Test ortamı (client 100) bu SAS'ı içermiyor olabilir."
            });
        }

        return Ok(lines);
    }

    [HttpPost("barcodes")]
    public async Task<IActionResult> CreateBarcode(
        [FromBody] CreateSasBarcodeApiRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PrintQuantity <= 0 || request.PackageQuantity <= 0)
        {
            return BadRequest(new { error = "Miktar değerleri 0'dan büyük olmalıdır." });
        }

        if (!PackageQuantityValidator.IsValidMultiple(request.PrintQuantity, request.PackageQuantity, request.LabelType))
        {
            return BadRequest(new { error = PackageQuantityValidator.NotMultipleError });
        }

        var result = await sapService.CreateBarcodeAsync(new SapBarcodeRequest(
            BarcodeModuleType.Sas,
            request.LabelType,
            request.PurchaseOrderNo,
            request.LineNo,
            request.MaterialNumber,
            request.PackageQuantity,
            request.PrintQuantity,
            BrandCode: null,
            VendorCode: request.VendorCode), cancellationToken);

        if (!result.Success && result.BarcodeNumbers.Count == 0)
        {
            return BadRequest(new { error = result.ErrorMessage ?? "Barkod alınamadı." });
        }

        return Ok(result);
    }

    [HttpPost("barcodes/reprint")]
    public async Task<IActionResult> ReprintBarcode(
        [FromBody] ReprintSasBarcodeApiRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SerialNumber))
        {
            return BadRequest(new { error = "Seri numarası (SERNR) zorunludur." });
        }

        if (request.PackageQuantity <= 0)
        {
            return BadRequest(new { error = "Paket miktarı 0'dan büyük olmalıdır." });
        }

        var result = await sapService.ReprintBarcodeAsync(new SapReprintRequest(
            BarcodeModuleType.Sas,
            request.PurchaseOrderNo,
            request.LineNo,
            request.PackageQuantity,
            request.LabelType,
            request.SerialNumber,
            VendorCode: request.VendorCode), cancellationToken);

        if (!result.Success && result.BarcodeNumbers.Count == 0)
        {
            return BadRequest(new { error = result.ErrorMessage ?? "Tekrar basım başarısız." });
        }

        return Ok(result);
    }
}

public record ReprintSasBarcodeApiRequest(
    string PurchaseOrderNo,
    string LineNo,
    decimal PackageQuantity,
    LabelType LabelType,
    string SerialNumber,
    string? PrintedBy,
    string? VendorCode = null);

public record CreateSasBarcodeApiRequest(
    string PurchaseOrderNo,
    string LineNo,
    string MaterialNumber,
    decimal PackageQuantity,
    decimal PrintQuantity,
    LabelType LabelType,
    string? PrintedBy,
    string? VendorCode = null);
