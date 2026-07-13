using FasonBarkod.Core.Entities;
using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;
using FasonBarkod.Infrastructure.Data;
using FasonBarkod.Infrastructure.Services;
using FasonBarkod.Web.Configuration;
using FasonBarkod.Web.Models;
using FasonBarkod.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FasonBarkod.Web.Controllers;

[Authorize]
public class SasController(
    IFasonBarkodApiClient apiClient,
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager,
    ILabelPrintService labelPrintService,
    IPrintSettingsStore printSettingsStore,
    IOptions<TestingOptions> testingOptions) : Controller
{
    private readonly TestingOptions _testingOptions = testingOptions.Value;

    private const string VendorSessionKey = "SasVendorCode";

    private string? ResolveVendorCode(string? vendorCode)
    {
        if (!string.IsNullOrWhiteSpace(vendorCode))
        {
            return vendorCode.Trim();
        }

        return HttpContext.Session.GetString(VendorSessionKey);
    }

    private void RememberVendorCode(string? vendorCode)
    {
        if (!string.IsNullOrWhiteSpace(vendorCode))
        {
            HttpContext.Session.SetString(VendorSessionKey, vendorCode.Trim());
        }
    }

    [HttpGet]
    public IActionResult Index(string? purchaseOrderNo, string? vendorCode)
    {
        return View(new SasSearchViewModel
        {
            PurchaseOrderNo = purchaseOrderNo ?? string.Empty,
            VendorCode = vendorCode
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SasSearchViewModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.PurchaseOrderNo))
        {
            ModelState.AddModelError(nameof(model.PurchaseOrderNo), "SAS numarası zorunludur.");
            return View(model);
        }

        RememberVendorCode(model.VendorCode);

        return RedirectToAction(nameof(Detail), new
        {
            id = model.PurchaseOrderNo.Trim(),
            vendorCode = model.VendorCode?.Trim()
        });
    }

    public async Task<IActionResult> Detail(
        string id,
        string? vendorCode,
        string? lineNo,
        string? tab,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        var model = await BuildDetailViewModelAsync(id, ResolveVendorCode(vendorCode), lineNo, cancellationToken);
        if (model is null)
        {
            ModelState.AddModelError(string.Empty,
                $"SAS bulunamadı: {id}. SAP bu numara/cari ile kalem döndürmedi. Cari kodunu (LIFNR) ve SAS numarasını kontrol edin.");
            return View("Index", new SasSearchViewModel
            {
                PurchaseOrderNo = id,
                VendorCode = ResolveVendorCode(vendorCode)
            });
        }

        if (!string.IsNullOrWhiteSpace(tab))
        {
            model.ActiveTab = tab;
        }

        model.RecentPrints = await LoadRecentPrintsAsync(id, model.MaterialNumber, cancellationToken);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintKoliUstu(SasDetailViewModel model, CancellationToken cancellationToken)
    {
        model.ActiveTab = "print";
        return await PrintAsync(model, LabelType.KoliUstu, model.KoliUstuQuantity, cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintKoliIci(SasDetailViewModel model, CancellationToken cancellationToken)
    {
        model.ActiveTab = "print";
        return await PrintAsync(model, LabelType.KoliIci, model.KoliIciQuantity, cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReprintKoliUstu(SasDetailViewModel model, CancellationToken cancellationToken)
    {
        model.ActiveTab = "reprint";
        return await ReprintAsync(model, LabelType.KoliUstu, cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReprintKoliIci(SasDetailViewModel model, CancellationToken cancellationToken)
    {
        model.ActiveTab = "reprint";
        return await ReprintAsync(model, LabelType.KoliIci, cancellationToken);
    }

    public IActionResult Print(string orderNo, string lineNo, string materialNumber, decimal packageQuantity, string materialDescription)
    {
        return RedirectToAction(nameof(Detail), new { id = orderNo, lineNo });
    }

    private async Task<IActionResult> PrintAsync(
        SasDetailViewModel model,
        LabelType labelType,
        decimal printQuantity,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.LineNo))
        {
            ModelState.AddModelError(string.Empty, "Önce tablodan bir kalem seçin.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        if (model.PackageQuantity <= 0 || printQuantity <= 0)
        {
            ModelState.AddModelError(string.Empty, "Paket miktarı ve basım miktarı 0'dan büyük olmalıdır.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        if (!PackageQuantityValidator.IsValidMultiple(printQuantity, model.PackageQuantity, labelType))
        {
            ModelState.AddModelError(string.Empty, PackageQuantityValidator.NotMultipleError);
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var result = await apiClient.CreateSasBarcodeAsync(new CreateSasBarcodeRequest(
            model.PurchaseOrderNo,
            model.LineNo,
            model.MaterialNumber,
            model.PackageQuantity,
            printQuantity,
            labelType,
            User.Identity?.Name,
            model.VendorCode), cancellationToken);

        if (!result.Success && !IsAllowedMockBarcodeResult(result))
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "SAP barkod alınamadı.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var printResult = await labelPrintService.PrintSasLabelsAsync(
            await BuildPrintContextAsync(model, labelType, result, cancellationToken: cancellationToken),
            cancellationToken);

        if (!printResult.Success)
        {
            ModelState.AddModelError(string.Empty, printResult.ErrorMessage ?? "Yazdırma başarısız.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        if (result.Success)
        {
            await SaveBarcodePrintsAsync(
                model,
                result,
                printQuantity,
                labelType,
                isReprint: false,
                cancellationToken);
        }

        return RedirectToDetail(model, "print");
    }

    private async Task<IActionResult> ReprintAsync(
        SasDetailViewModel model,
        LabelType labelType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.LineNo))
        {
            ModelState.AddModelError(string.Empty, "Önce tablodan bir kalem seçin.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(model.SerialNumber))
        {
            ModelState.AddModelError(string.Empty, "Tekrar basım için seri numarası (SERNR) zorunludur.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        if (model.PackageQuantity <= 0)
        {
            ModelState.AddModelError(string.Empty, "Paket miktarı 0'dan büyük olmalıdır.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var result = await apiClient.ReprintSasBarcodeAsync(new ReprintSasBarcodeRequest(
            model.PurchaseOrderNo,
            model.LineNo,
            model.PackageQuantity,
            labelType,
            model.SerialNumber.Trim(),
            User.Identity?.Name,
            model.VendorCode), cancellationToken);

        if (!result.Success && !IsAllowedMockBarcodeResult(result))
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "SAP tekrar basım başarısız.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var printResult = await labelPrintService.PrintSasLabelsAsync(
            await BuildPrintContextAsync(model, labelType, result, model.SerialNumber.Trim(), cancellationToken),
            cancellationToken);

        if (!printResult.Success)
        {
            ModelState.AddModelError(string.Empty, printResult.ErrorMessage ?? "Yazdırma başarısız.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        if (result.Success)
        {
            await SaveBarcodePrintsAsync(
                model,
                result,
                model.PackageQuantity,
                labelType,
                isReprint: true,
                cancellationToken);
        }

        return RedirectToDetail(model, "reprint");
    }

    private async Task<SasLabelPrintContext> BuildPrintContextAsync(
        SasDetailViewModel model,
        LabelType labelType,
        SapBarcodeResult result,
        string? serialNumber = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await printSettingsStore.GetAsync(cancellationToken);

        return new SasLabelPrintContext(
            labelType,
            model.PurchaseOrderNo,
            model.LineNo,
            model.MaterialNumber,
            model.MaterialDescription,
            ResolveMaterialGroupCode(model),
            model.PackageQuantity,
            result,
            serialNumber,
            settings.PrinterName,
            TemplateFileName: null,
            model.VendorCode);
    }

    private static string? ResolveMaterialGroupCode(SasDetailViewModel model) =>
        model.Lines.FirstOrDefault(l => l.LineNo == model.LineNo)?.MaterialGroupCode;

    private async Task SaveBarcodePrintsAsync(
        SasDetailViewModel model,
        SapBarcodeResult result,
        decimal printQuantity,
        LabelType labelType,
        bool isReprint,
        CancellationToken cancellationToken)
    {
        if (!result.Success)
        {
            return;
        }

        var barcodeEntries = ResolveBarcodeEntries(result);
        if (barcodeEntries.Count == 0)
        {
            return;
        }

        var user = await userManager.GetUserAsync(User);
        var labelName = labelType == LabelType.KoliUstu ? "Koli Üstü" : "Koli İçi";
        var operation = isReprint ? "Tekrar basım" : "Yeni basım";
        var perLabelQty = labelType == LabelType.KoliUstu
            ? model.PackageQuantity
            : 1m;

        foreach (var barcodeNo in barcodeEntries)
        {
            var print = new BarcodePrint
            {
                BarcodeNo = barcodeNo,
                SalesOrderNo = model.PurchaseOrderNo,
                MaterialCode = model.MaterialNumber,
                MaterialName = model.MaterialDescription,
                Quantity = perLabelQty,
                PrintDate = DateTime.UtcNow,
                PrintedByUserId = user?.Id,
                Status = result.Success ? BarcodePrintStatus.Printed : BarcodePrintStatus.Pending,
                SapSent = result.Success
            };

            context.BarcodePrints.Add(print);

            context.SapTransferLogs.Add(new SapTransferLog
            {
                BarcodePrint = print,
                Result = result.Success ? SapTransferResult.Success : SapTransferResult.Pending,
                ErrorMessage = result.Success ? null : result.ErrorMessage,
                RequestPayload = $"SAS {operation} — {labelName}, kalem {model.LineNo}, basım miktarı {printQuantity}",
                ResponsePayload = result.Success
                    ? "SAP ZMM_N_SAS_B"
                    : result.ErrorMessage ?? "Mock barkod (SAP yanıt vermedi veya test modu)"
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<string> ResolveBarcodeEntries(SapBarcodeResult result)
    {
        if (result.Labels is { Count: > 0 } labels)
        {
            return labels
                .Select(ResolvePrimaryBarcode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return result.BarcodeNumbers
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolvePrimaryBarcode(SasBarcodeLabelDto label)
    {
        if (!string.IsNullOrWhiteSpace(label.Barcode1))
        {
            return label.Barcode1.Trim();
        }

        if (!string.IsNullOrWhiteSpace(label.Barcode2))
        {
            return label.Barcode2.Trim();
        }

        if (!string.IsNullOrWhiteSpace(label.Barcode3))
        {
            return label.Barcode3.Trim();
        }

        return label.Barcode4.Trim();
    }

    private RedirectToActionResult RedirectToDetail(SasDetailViewModel model, string tab) =>
        RedirectToAction(nameof(Detail), new
        {
            id = model.PurchaseOrderNo,
            vendorCode = model.VendorCode,
            lineNo = model.LineNo,
            tab
        });

    private async Task<IActionResult> RenderDetailWithErrorsAsync(
        SasDetailViewModel model,
        CancellationToken cancellationToken)
    {
        var refreshed = await BuildDetailViewModelAsync(
            model.PurchaseOrderNo,
            model.VendorCode,
            model.SelectedLineNo ?? model.LineNo,
            cancellationToken);

        if (refreshed is null)
        {
            return RedirectToAction(nameof(Index));
        }

        ApplyPostedPrintState(refreshed, model);
        refreshed.RecentPrints = await LoadRecentPrintsAsync(
            model.PurchaseOrderNo,
            model.MaterialNumber,
            cancellationToken);
        return View("Detail", refreshed);
    }

    private static void ApplyPostedPrintState(SasDetailViewModel target, SasDetailViewModel source)
    {
        target.PackageQuantity = source.PackageQuantity;
        target.KoliUstuQuantity = source.KoliUstuQuantity;
        target.KoliIciQuantity = source.KoliIciQuantity;
        target.SerialNumber = source.SerialNumber;
        target.ActiveTab = source.ActiveTab;
    }

    private async Task<SasDetailViewModel?> BuildDetailViewModelAsync(
        string purchaseOrderNo,
        string? vendorCode,
        string? lineNo,
        CancellationToken cancellationToken)
    {
        var lines = await apiClient.ListSasAsync(purchaseOrderNo, vendorCode, cancellationToken);
        if (lines.Count == 0)
        {
            return null;
        }

        var model = new SasDetailViewModel
        {
            PurchaseOrderNo = purchaseOrderNo,
            VendorCode = !string.IsNullOrWhiteSpace(vendorCode)
                ? vendorCode.Trim()
                : lines.Select(l => l.VendorCode).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim(),
            Lines = lines.ToList()
        };

        model.CustomerName = lines
            .Select(l => l.CustomerName)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            ?.Trim();

        if (!string.IsNullOrWhiteSpace(model.VendorCode))
        {
            RememberVendorCode(model.VendorCode);
        }

        if (!string.IsNullOrWhiteSpace(lineNo))
        {
            var selected = lines.FirstOrDefault(l => l.LineNo == lineNo);
            if (selected is not null)
            {
                model.SelectLine(selected);
            }
        }

        if (string.IsNullOrWhiteSpace(model.VendorCode))
        {
            var fromSelected = model.SelectedLine?.VendorCode;
            if (!string.IsNullOrWhiteSpace(fromSelected))
            {
                model.VendorCode = fromSelected.Trim();
                RememberVendorCode(model.VendorCode);
            }
        }

        return model;
    }

    private async Task<List<BarcodePrintListItemViewModel>> LoadRecentPrintsAsync(
        string purchaseOrderNo,
        string? materialCode,
        CancellationToken cancellationToken)
    {
        var query = context.BarcodePrints
            .AsNoTracking()
            .Include(x => x.PrintedByUser)
            .Where(x =>
                x.SalesOrderNo == purchaseOrderNo &&
                !x.BarcodeNo.StartsWith("MOCK-") &&
                !x.BarcodeNo.StartsWith("FSN-"));

        if (!string.IsNullOrWhiteSpace(materialCode))
        {
            query = query.Where(x => x.MaterialCode == materialCode);
        }

        return await query
            .OrderByDescending(x => x.PrintDate)
            .Take(20)
            .Select(x => new BarcodePrintListItemViewModel
            {
                Id = x.Id,
                BarcodeNo = x.BarcodeNo,
                SalesOrderNo = x.SalesOrderNo,
                MaterialCode = x.MaterialCode,
                MaterialName = x.MaterialName,
                Quantity = x.Quantity,
                PrintDate = x.PrintDate,
                PrintedBy = x.PrintedByUser != null ? x.PrintedByUser.FullName ?? x.PrintedByUser.Email : null,
                Status = x.Status,
                SapSent = x.SapSent
            })
            .ToListAsync(cancellationToken);
    }

    private bool IsAllowedMockBarcodeResult(SapBarcodeResult result) =>
        _testingOptions.AllowMockBarcodes &&
        result.BarcodeNumbers.Count > 0 &&
        result.BarcodeNumbers.All(static b =>
            b.StartsWith("MOCK-", StringComparison.OrdinalIgnoreCase));
}
