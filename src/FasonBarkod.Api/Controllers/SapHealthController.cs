using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;
using FasonBarkod.Infrastructure.Sap;
using FasonBarkod.Infrastructure.Sap.Rfc;
using FasonBarkod.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using SapNwRfc;

namespace FasonBarkod.Api.Controllers;

[ApiController]
[Route("saphealth")]
public class SapHealthController(ISapConnectionFactory connectionFactory) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        if (!connectionFactory.IsConfigured)
        {
            return Ok(new
            {
                status = "not_configured",
                message = "Sap:Enabled=true ve bağlantı bilgileri gerekli."
            });
        }

        try
        {
            using var connection = connectionFactory.OpenConnection();
            return Ok(new
            {
                status = "connected",
                message = "SAP RFC bağlantısı başarılı."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new
            {
                status = "error",
                message = ex.Message
            });
        }
    }

    [HttpGet("sas-test")]
    public IActionResult TestSasList(
        [FromQuery] string? orderNo = null,
        [FromQuery] string? vendorCode = null,
        [FromQuery] string? lineNo = null,
        [FromQuery] bool legacy = false,
        [FromQuery] bool allowEmptyOrder = false)
    {
        // allowEmptyOrder=true: EBELN olmadan liste denemesi (girişte tüm SAS listesi mümkün mü?)
        if (string.IsNullOrWhiteSpace(orderNo) && !allowEmptyOrder)
        {
            return BadRequest(new { error = "orderNo zorunlu. Boş denemek için allowEmptyOrder=true kullanın." });
        }

        if (!connectionFactory.IsConfigured)
        {
            return Ok(new { status = "not_configured" });
        }

        var rfcName = legacy ? SapRfcFunctions.Sas.ListLegacy : SapRfcFunctions.Sas.List;

        try
        {
            using var connection = connectionFactory.OpenConnection();
            var import = ZmmSasLImportMapper.FromRequest(orderNo ?? string.Empty, vendorCode);
            var invoke = new ZmmSasLInvoke
            {
                PurchaseOrderNumbers = import.PurchaseOrderNumbers,
                VendorCode = import.VendorCode
            };

            using var function = connection.CreateFunction(rfcName);
            var result = function.Invoke<ZmmSasLInvoke>(invoke);

            var items = result.Items.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(lineNo))
            {
                items = items.Where(r => r.LineNo.ToString().TrimStart('0') == lineNo.TrimStart('0'));
            }

            var distinctOrders = items
                .Select(r => r.PurchaseOrderNo?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(50)
                .ToList();

            return Ok(new
            {
                rfc = rfcName,
                experiment = allowEmptyOrder && string.IsNullOrWhiteSpace(orderNo) ? "empty_ebeln" : "normal",
                sent = new
                {
                    ebelnCount = invoke.PurchaseOrderNumbers?.Length ?? 0,
                    ebeln = invoke.PurchaseOrderNumbers?.FirstOrDefault()?.PurchaseOrderNo,
                    kunnr = invoke.VendorCode
                },
                metadata = SapRfcDiagnostics.DescribeFunction(connection, rfcName),
                structures = SapRfcDiagnostics.DescribeTypes(connection, "Z_TT_EBELN", "ZMM14701"),
                realStructures = new
                {
                    isEbeln = SapRfcDiagnostics.DescribeTableParameterStructure(connection, rfcName, "IS_EBELN"),
                    itData = SapRfcDiagnostics.DescribeTableParameterStructure(connection, rfcName, "IT_DATA"),
                    itHata = SapRfcDiagnostics.DescribeTableParameterStructure(connection, rfcName, "IT_HATA")
                },
                itDataCount = result.Items.Length,
                distinctOrderCount = distinctOrders.Count,
                distinctOrders,
                itHataMesajlari = result.Errors.Select(e => e.Message.Trim()).Where(m => !string.IsNullOrWhiteSpace(m)),
                sample = items.Take(10).Select(r => new
                {
                    r.PurchaseOrderNo,
                    r.LineNo,
                    r.MaterialNumber,
                    r.MaterialDescription,
                    r.Quantity,
                    r.PackageQuantity,
                    r.PrintedBoxCount,
                    r.PrintedInsideBoxCount,
                    r.MaterialGroupCode,
                    r.MaterialGroupDescription
                })
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// ZMM_N_SAS_B (barkod oluşturma) tanılaması — SE37'ye gerek olmadan gerçek RFC parametre
    /// yapısını ve IT_DATA/hata mesajını doğrudan görmek için.
    /// </summary>
    [HttpGet("sas-barcode-test")]
    public IActionResult TestSasBarcode(
        [FromQuery] string orderNo,
        [FromQuery] string lineNo,
        [FromQuery] int packageQty = 10,
        [FromQuery] int? printQty = null,
        [FromQuery] bool koliUstu = false)
    {
        if (string.IsNullOrWhiteSpace(orderNo) || string.IsNullOrWhiteSpace(lineNo))
        {
            return BadRequest(new { error = "orderNo ve lineNo zorunlu." });
        }

        if (!connectionFactory.IsConfigured)
        {
            return Ok(new { status = "not_configured" });
        }

        var qty = printQty ?? packageQty;

        try
        {
            using var connection = connectionFactory.OpenConnection();

            var request = new SapBarcodeRequest(
                BarcodeModuleType.Sas,
                koliUstu ? LabelType.KoliUstu : LabelType.KoliIci,
                orderNo,
                lineNo,
                string.Empty,
                packageQty,
                qty,
                null);
            var import = ZmmSasBImportMapper.FromBarcodeRequest(request);

            using var function = connection.CreateFunction(SapRfcFunctions.Sas.CreateBarcode);
            var result = function.Invoke<ZmmSasBInvoke>(import);

            return Ok(new
            {
                rfc = SapRfcFunctions.Sas.CreateBarcode,
                sent = new
                {
                    ebeln = import.PurchaseOrderNo,
                    ebelp = import.LineNo,
                    koliBasMik = import.BoxTopPrintQuantity,
                    koliIciBasMik = import.BoxInsidePrintQuantity,
                    paketIci = import.PackageInsideQuantity,
                    koli = import.PrintBoxTop,
                    koliIci = import.PrintBoxInside,
                    basilacakBarkod = import.BarcodesToPrint
                },
                metadata = SapRfcDiagnostics.DescribeFunction(connection, SapRfcFunctions.Sas.CreateBarcode),
                realStructures = new
                {
                    itData = SapRfcDiagnostics.DescribeTableParameterStructure(connection, SapRfcFunctions.Sas.CreateBarcode, "IT_DATA"),
                    itHata = SapRfcDiagnostics.DescribeTableParameterStructure(connection, SapRfcFunctions.Sas.CreateBarcode, "IT_HATA")
                },
                itDataCount = result.Items.Length,
                itHataMesajlari = ZmmSasBResultHelper.ExtractMessages(result.Errors),
                sample = result.Items.Take(3).Select(r => new
                {
                    r.Barcode1,
                    r.Barcode2,
                    r.Barcode3,
                    r.Barcode4,
                    r.MaterialNumber,
                    r.MaterialDescription,
                    r.SerialNumber,
                    r.PackageQuantity
                })
            });
        }
        catch (Exception ex)
        {
            using var diagConnection = connectionFactory.OpenConnection();
            return StatusCode(500, new
            {
                error = ex.Message,
                metadata = SapRfcDiagnostics.DescribeFunction(diagConnection, SapRfcFunctions.Sas.CreateBarcode),
                realStructures = new
                {
                    itData = SapRfcDiagnostics.DescribeTableParameterStructure(diagConnection, SapRfcFunctions.Sas.CreateBarcode, "IT_DATA"),
                    itHata = SapRfcDiagnostics.DescribeTableParameterStructure(diagConnection, SapRfcFunctions.Sas.CreateBarcode, "IT_HATA")
                }
            });
        }
    }

}
