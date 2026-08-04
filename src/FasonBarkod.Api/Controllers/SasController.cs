using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;
using FasonBarkod.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace FasonBarkod.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SasController(ISapService sapService) : ControllerBase
{
    [HttpGet("lines")]
    public async Task<IActionResult> GetLinesByQuery(
        [FromQuery] string? orderNo,
        [FromQuery] string? vendorCode,
        CancellationToken cancellationToken)
    {
        var result = await sapService.ListSasAsync(orderNo ?? string.Empty, vendorCode, cancellationToken);
        if (result.Lines.Count == 0)
        {
            var sapMessage = result.SapMessages.Count > 0
                ? string.Join(" | ", result.SapMessages)
                : null;

            return NotFound(new
            {
                error = string.IsNullOrWhiteSpace(orderNo)
                    ? "Listelenecek SAS bulunamadı."
                    : $"“{orderNo}” numaralı SAS bulunamadı. Numarayı kontrol edin.",
                sapMessage,
                hint = $"SE37 ZMM_N_SAS_L EBELN={(string.IsNullOrWhiteSpace(orderNo) ? "(boş)" : orderNo)} KUNNR={(string.IsNullOrWhiteSpace(vendorCode) ? "(boş)" : vendorCode)}",
                diagnostic = $"http://localhost:5135/saphealth/sas-test?allowEmptyOrder=true&orderNo={Uri.EscapeDataString(orderNo ?? string.Empty)}&vendorCode={Uri.EscapeDataString(vendorCode ?? string.Empty)}"
            });
        }

        return Ok(result.Lines);
    }

    [HttpGet("{orderNo}/lines")]
    public async Task<IActionResult> GetLines(
        string orderNo,
        [FromQuery] string? vendorCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderNo))
        {
            return await GetLinesByQuery(null, vendorCode, cancellationToken);
        }

        var result = await sapService.ListSasAsync(orderNo, vendorCode, cancellationToken);
        if (result.Lines.Count == 0)
        {
            var sapMessage = result.SapMessages.Count > 0
                ? string.Join(" | ", result.SapMessages)
                : null;

            return NotFound(new
            {
                error = $"“{orderNo}” numaralı SAS bulunamadı. Numarayı kontrol edin.",
                sapMessage,
                hint = $"SE37 ZMM_N_SAS_L EBELN={orderNo} KUNNR={(string.IsNullOrWhiteSpace(vendorCode) ? "(boş)" : vendorCode)}",
                diagnostic = $"http://localhost:5135/saphealth/sas-test?orderNo={Uri.EscapeDataString(orderNo)}&vendorCode={Uri.EscapeDataString(vendorCode ?? string.Empty)}"
            });
        }

        return Ok(result.Lines);
    }

    [HttpGet("{orderNo}/serials")]
    public async Task<IActionResult> GetSerials(
        string orderNo,
        [FromQuery] string lineNo,
        [FromQuery] bool boxInside = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderNo) || string.IsNullOrWhiteSpace(lineNo))
        {
            return BadRequest(new { error = "SAS no ve kalem (lineNo) zorunludur." });
        }

        var result = await sapService.ListSasSerialsAsync(orderNo, lineNo, boxInside, cancellationToken);
        if (!result.Success && result.Serials.Count == 0)
        {
            return BadRequest(new { error = result.ErrorMessage ?? "Seri listesi alınamadı." });
        }

        return Ok(result);
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

        var listResult = await sapService.ListSasAsync(request.PurchaseOrderNo, request.VendorCode, cancellationToken);
        var line = listResult.Lines.FirstOrDefault(l =>
            string.Equals(l.LineNo, request.LineNo?.Trim(), StringComparison.OrdinalIgnoreCase)
            || (int.TryParse(l.LineNo, out var a) && int.TryParse(request.LineNo, out var b) && a == b));

        var orderQty = line?.Quantity ?? 0;
        var originalPackage = line is { PackageQuantity: > 0 }
            ? line.PackageQuantity
            : request.PackageQuantity;

        var validationError = PackageQuantityValidator.ValidateNewPrint(
            request.LabelType,
            request.PrintQuantity,
            request.PackageQuantity,
            orderQty,
            originalPackage);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return BadRequest(new { error = validationError });
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

        // Koli üstü reprint: PaketIci=0 (eski Doqu). Koli içi: paket miktarı > 0.
        if (request.LabelType == LabelType.KoliIci && request.PackageQuantity <= 0)
        {
            return BadRequest(new { error = "Koli içi tekrar basımda paket miktarı 0'dan büyük olmalıdır." });
        }

        var packageQty = request.LabelType == LabelType.KoliUstu ? 0 : request.PackageQuantity;

        var result = await sapService.ReprintBarcodeAsync(new SapReprintRequest(
            BarcodeModuleType.Sas,
            request.PurchaseOrderNo,
            request.LineNo,
            packageQty,
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
