using FasonBarkod.Core.Entities;
using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;
using FasonBarkod.Infrastructure.Data;
using FasonBarkod.Infrastructure.Sap;
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
    IOptions<TestingOptions> testingOptions,
    ILogger<SasController> logger) : Controller
{
    private readonly TestingOptions _testingOptions = testingOptions.Value;

    private const string VendorSessionKey = "SasVendorCode";

    private async Task<string?> GetUserVendorCodeAsync()
    {
        var user = await userManager.GetUserAsync(User);
        return string.IsNullOrWhiteSpace(user?.VendorCode) ? null : user.VendorCode.Trim();
    }

    /// <summary>
    /// Operatör: her zaman kendi satıcı kodu.
    /// Admin: form/query/session veya kendi satıcı kodu (varsa).
    /// </summary>
    private async Task<string?> ResolveVendorCodeAsync(string? requestedVendorCode)
    {
        var userVendor = await GetUserVendorCodeAsync();
        var isAdmin = User.IsInRole("Admin");

        if (!isAdmin)
        {
            return userVendor;
        }

        if (!string.IsNullOrWhiteSpace(requestedVendorCode))
        {
            return requestedVendorCode.Trim();
        }

        var fromSession = HttpContext.Session.GetString(VendorSessionKey);
        if (!string.IsNullOrWhiteSpace(fromSession))
        {
            return fromSession.Trim();
        }

        return userVendor;
    }

    private void RememberVendorCode(string? vendorCode)
    {
        if (!string.IsNullOrWhiteSpace(vendorCode) && User.IsInRole("Admin"))
        {
            HttpContext.Session.SetString(VendorSessionKey, vendorCode.Trim());
        }
    }

    /// <summary>
    /// Index listesi: Operatör → hesap satıcı kodu. Admin → yalnızca formdaki cari (session yok; boşsa tüm SAS).
    /// </summary>
    private async Task<string?> ResolveVendorCodeForListAsync(string? requestedVendorCode)
    {
        if (!User.IsInRole("Admin"))
        {
            return await GetUserVendorCodeAsync();
        }

        return string.IsNullOrWhiteSpace(requestedVendorCode) ? null : requestedVendorCode.Trim();
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? purchaseOrderNo,
        string? vendorCode,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        // Giriş sonrası / SAS sayfası açılınca Listele'ye basmadan listeyi getir.
        if (!string.IsNullOrWhiteSpace(purchaseOrderNo))
        {
            if (!SapAccountNumber.TryNormalizeEbeln(purchaseOrderNo, out var ebeln, out var ebelnError))
            {
                ModelState.AddModelError(string.Empty, ebelnError!);
                var isAdminEarly = User.IsInRole("Admin");
                return View(new SasSearchViewModel
                {
                    PurchaseOrderNo = purchaseOrderNo,
                    VendorCode = await ResolveVendorCodeForListAsync(vendorCode),
                    VendorCodeLocked = !isAdminEarly,
                    Page = page,
                    PageSize = pageSize
                });
            }

            var resolvedForRedirect = await ResolveVendorCodeAsync(vendorCode);
            return RedirectToAction(nameof(Detail), new
            {
                id = ebeln,
                vendorCode = resolvedForRedirect
            });
        }

        var isAdmin = User.IsInRole("Admin");
        var model = new SasSearchViewModel
        {
            VendorCode = await ResolveVendorCodeForListAsync(vendorCode),
            VendorCodeLocked = !isAdmin,
            Page = page,
            PageSize = pageSize
        };

        return View(await FillOrderListAsync(model, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SasSearchViewModel model, CancellationToken cancellationToken)
    {
        var resolvedVendor = await ResolveVendorCodeForListAsync(model.VendorCode);
        model.VendorCode = resolvedVendor;
        model.VendorCodeLocked = !User.IsInRole("Admin");
        ModelState.Remove(nameof(model.PurchaseOrderNo));

        if (!User.IsInRole("Admin") && string.IsNullOrWhiteSpace(resolvedVendor))
        {
            ModelState.AddModelError(
                string.Empty,
                "Hesabınıza satıcı kodu tanımlı değil. Yönetici ile iletişime geçin.");
            return View(model);
        }

        RememberVendorCode(resolvedVendor);

        if (!string.IsNullOrWhiteSpace(model.PurchaseOrderNo))
        {
            if (!SapAccountNumber.TryNormalizeEbeln(model.PurchaseOrderNo, out var ebeln, out var ebelnError))
            {
                ModelState.AddModelError(string.Empty, ebelnError!);
                return View(model);
            }

            return RedirectToAction(nameof(Detail), new
            {
                id = ebeln,
                vendorCode = resolvedVendor
            });
        }

        // PRG: sayfalama linkleri GET ile çalışsın
        return RedirectToAction(nameof(Index), new
        {
            vendorCode = resolvedVendor,
            page = 1,
            pageSize = model.PageSize > 0 ? model.PageSize : 10
        });
    }

    /// <summary>
    /// Admin (cari yok): SAP açık SAS listesi.
    /// Cari / satıcı kodu dolu: SAP I_KUNNR filtresi + yerel basım geçmişi birleşimi.
    /// </summary>
    private async Task<SasSearchViewModel> FillOrderListAsync(
        SasSearchViewModel model,
        CancellationToken cancellationToken)
    {
        var resolvedVendor = model.VendorCode?.Trim();

        if (!User.IsInRole("Admin") && string.IsNullOrWhiteSpace(resolvedVendor))
        {
            ModelState.AddModelError(
                string.Empty,
                "Hesabınıza satıcı kodu tanımlı değil. Yönetici ile iletişime geçin.");
            return model;
        }

        if (!string.IsNullOrWhiteSpace(resolvedVendor))
        {
            // SAP: açık SAS listesi + I_KUNNR batch filtresi.
            // Yerel basım geçmişi yedek (API/SAP kapalı veya kapalı siparişler).
            var localOrders = await LoadVendorOrderSummariesAsync(resolvedVendor, cancellationToken);
            List<SasOrderSummaryViewModel> sapOrders = [];

            try
            {
                var sapResponse = await apiClient.ListSasAsync(string.Empty, resolvedVendor, cancellationToken);
                if (sapResponse.Lines.Count > 0)
                {
                    sapOrders = GroupLinesToOrderSummaries(sapResponse.Lines, resolvedVendor);
                }
                else if (!string.IsNullOrWhiteSpace(sapResponse.Error) && localOrders.Count == 0)
                {
                    ModelState.AddModelError(string.Empty, sapResponse.Error);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cari SAS listesi SAP'den alınamadı: {Vendor}", resolvedVendor);
                if (localOrders.Count == 0)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "SAP listesi alınamadı. Daha önce basılmış kayıtlar da yok.");
                }
            }

            model.Orders = MergeOrderSummaries(sapOrders, localOrders, resolvedVendor);

            if (model.Orders.Count == 0)
            {
                model.ListMessage = model.VendorCodeLocked
                    ? "Size ait açık / kayıtlı SAS bulunamadı."
                    : "Bu satıcı için açık / kayıtlı SAS bulunamadı.";
                ApplyOrderPaging(model);
                return model;
            }

            model.ListMessage = null;
            model.ShowVendorColumn = !model.VendorCodeLocked;
            ApplyOrderPaging(model);
            return model;
        }

        var response = await apiClient.ListSasAsync(string.Empty, null, cancellationToken);
        if (response.Lines.Count == 0)
        {
            ModelState.AddModelError(
                string.Empty,
                response.Error
                ?? "Listelenecek SAS bulunamadı.");
            return model;
        }

        var grouped = response.Lines
            .GroupBy(l => l.PurchaseOrderNo?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderByDescending(g => g.Key)
            .ToList();

        var orderNos = grouped.Select(g => g.Key).ToList();
        var vendorByOrder = await LoadVendorCodesByOrdersAsync(orderNos, cancellationToken);

        model.Orders = grouped
            .Select(g => new SasOrderSummaryViewModel
            {
                PurchaseOrderNo = g.Key,
                LineCount = g.Count(),
                SampleMaterial = g.Select(x => x.MaterialDescription)
                    .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)),
                VendorCode = vendorByOrder.GetValueOrDefault(g.Key)
            })
            .ToList();

        model.ShowVendorColumn = model.Orders.Any(o => !string.IsNullOrWhiteSpace(o.VendorCode));
        model.ListMessage = null;
        ApplyOrderPaging(model);
        return model;
    }

    private static List<SasOrderSummaryViewModel> GroupLinesToOrderSummaries(
        IReadOnlyList<SasLineDto> lines,
        string vendorCode) =>
        lines
            .GroupBy(l => l.PurchaseOrderNo?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderByDescending(g => g.Key)
            .Select(g => new SasOrderSummaryViewModel
            {
                PurchaseOrderNo = g.Key,
                LineCount = g.Count(),
                SampleMaterial = g.Select(x => x.MaterialDescription)
                    .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)),
                VendorCode = vendorCode
            })
            .ToList();

    private static List<SasOrderSummaryViewModel> MergeOrderSummaries(
        IReadOnlyList<SasOrderSummaryViewModel> fromSap,
        IReadOnlyList<SasOrderSummaryViewModel> fromLocal,
        string vendorCode)
    {
        var merged = new Dictionary<string, SasOrderSummaryViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var order in fromSap)
        {
            merged[order.PurchaseOrderNo] = order;
        }

        foreach (var order in fromLocal)
        {
            if (merged.TryGetValue(order.PurchaseOrderNo, out var existing))
            {
                if (string.IsNullOrWhiteSpace(existing.SampleMaterial) && !string.IsNullOrWhiteSpace(order.SampleMaterial))
                {
                    existing.SampleMaterial = order.SampleMaterial;
                }

                continue;
            }

            merged[order.PurchaseOrderNo] = new SasOrderSummaryViewModel
            {
                PurchaseOrderNo = order.PurchaseOrderNo,
                LineCount = order.LineCount,
                SampleMaterial = order.SampleMaterial,
                VendorCode = order.VendorCode ?? vendorCode
            };
        }

        return merged.Values.OrderByDescending(x => x.PurchaseOrderNo).ToList();
    }

    private static void ApplyOrderPaging(SasSearchViewModel model)
    {
        var allowedSizes = new[] { 10, 25, 50, 100 };
        if (!allowedSizes.Contains(model.PageSize))
        {
            model.PageSize = 10;
        }

        model.TotalCount = model.Orders.Count;
        if (model.Page < 1)
        {
            model.Page = 1;
        }

        if (model.Page > model.TotalPages)
        {
            model.Page = model.TotalPages;
        }

        model.Orders = model.Orders
            .Skip((model.Page - 1) * model.PageSize)
            .Take(model.PageSize)
            .ToList();
    }

    public async Task<IActionResult> Detail(
        string id,
        string? vendorCode,
        string? lineNo,
        string? tab,
        string? sernr,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        if (!SapAccountNumber.TryNormalizeEbeln(id, out var normalizedId, out var ebelnError))
        {
            ModelState.AddModelError(string.Empty, ebelnError!);
            return View("Index", new SasSearchViewModel
            {
                PurchaseOrderNo = id,
                VendorCodeLocked = !User.IsInRole("Admin")
            });
        }

        id = normalizedId;

        var resolvedVendor = await ResolveVendorCodeAsync(vendorCode);

        if (!User.IsInRole("Admin") && string.IsNullOrWhiteSpace(resolvedVendor))
        {
            ModelState.AddModelError(
                string.Empty,
                "Hesabınıza satıcı kodu tanımlı değil. Yönetici ile iletişime geçin.");
            return View("Index", new SasSearchViewModel
            {
                PurchaseOrderNo = id,
                VendorCodeLocked = true
            });
        }

        var (model, listError) = await BuildDetailViewModelAsync(id, resolvedVendor, lineNo, cancellationToken);
        if (model is null)
        {
            ModelState.AddModelError(
                string.Empty,
                listError ?? $"“{id}” numaralı SAS bulunamadı. Numarayı kontrol edin.");
            return View("Index", new SasSearchViewModel
            {
                PurchaseOrderNo = id,
                VendorCode = resolvedVendor,
                VendorCodeLocked = !User.IsInRole("Admin")
            });
        }

        if (!string.IsNullOrWhiteSpace(tab))
        {
            model.ActiveTab = tab;
        }

        model.RecentPrints = await LoadRecentPrintsAsync(id, model.MaterialNumber, model.VendorCode, model.LineNo, cancellationToken);

        // Yeniden basım: query'den veya bu kalemin son basımından SERNR doldur.
        if (!string.IsNullOrWhiteSpace(sernr))
        {
            model.SerialNumber = sernr.Trim();
            model.ActiveTab = "reprint";
        }
        else if (string.IsNullOrWhiteSpace(model.SerialNumber))
        {
            model.SerialNumber = await ResolveLatestSerialForLineAsync(
                id,
                model.LineNo,
                model.MaterialNumber,
                model.VendorCode,
                cancellationToken) ?? string.Empty;
        }

        await LoadSapSerialsAsync(model, cancellationToken);

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Serials(
        string id,
        string lineNo,
        bool boxInside = false,
        string? vendorCode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(lineNo))
        {
            return BadRequest(new { error = "SAS no ve kalem zorunludur." });
        }

        _ = vendorCode;
        var result = await apiClient.ListSasSerialsAsync(id, lineNo, boxInside, cancellationToken);
        return Json(result);
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
        // Halil/ABAP: yeni Print DURUM='' satır sayar → Max:0. Masaüstü gibi Serials+Reprint.
        return await PrintKoliIciFromSerialsAsync(model, model.KoliIciQuantity, cancellationToken);
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
        model.VendorCode = await ResolveVendorCodeAsync(model.VendorCode);

        if (!User.IsInRole("Admin") && string.IsNullOrWhiteSpace(model.VendorCode))
        {
            ModelState.AddModelError(string.Empty, "Hesabınıza satıcı kodu tanımlı değil. Yönetici ile iletişime geçin.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(model.LineNo))
        {
            ModelState.AddModelError(string.Empty, "Önce tablodan bir kalem seçin.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var currentLines = await apiClient.ListSasAsync(model.PurchaseOrderNo, model.VendorCode, cancellationToken);
        var currentLine = currentLines.Lines.FirstOrDefault(l =>
            string.Equals(l.LineNo, model.LineNo, StringComparison.OrdinalIgnoreCase)
            || (int.TryParse(l.LineNo, out var a) && int.TryParse(model.LineNo, out var b) && a == b));
        if (currentLine is null)
        {
            ModelState.AddModelError(string.Empty, "Seçili kalem SAP listesinde bulunamadı. Sayfayı yenileyip tekrar deneyin.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var originalPackage = currentLine.PackageQuantity > 0
            ? currentLine.PackageQuantity
            : model.OriginalPackageQuantity;
        model.OriginalPackageQuantity = originalPackage;
        model.OrderQuantity = currentLine.Quantity;

        // Paket sadece Admin değiştirebilir; operatörde form manipülasyonu da SAP paketini kullanır.
        if (!User.IsInRole("Admin"))
        {
            model.PackageQuantity = originalPackage > 0 ? originalPackage : currentLine.PackageQuantity;
        }

        // Doqu: kullanıcı paketi düşürebilir; SAP orijinalinden büyük olamaz. Üzerine yazma.
        var validationError = PackageQuantityValidator.ValidateNewPrint(
            labelType,
            printQuantity,
            model.PackageQuantity,
            currentLine.Quantity,
            originalPackage,
            currentLine.PrintedBoxCount);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            ModelState.AddModelError(string.Empty, validationError);
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        // Ekrandaki paket (Admin düzenlediyse o değer) olduğu gibi SAP'ye gider.
        var packageForSap = model.PackageQuantity;
        if (packageForSap <= 0)
        {
            packageForSap = printQuantity > 0 ? printQuantity : 1;
        }

        // Koli üstü öncesi mevcut koli içi SERNR'ler (sonra diff ile yenileri Reprint).
        HashSet<string> beforeInsideSerials = new(StringComparer.OrdinalIgnoreCase);
        if (labelType == LabelType.KoliUstu)
        {
            var beforeInside = await apiClient.ListSasSerialsAsync(
                model.PurchaseOrderNo,
                model.LineNo,
                boxInside: true,
                cancellationToken);
            foreach (var s in beforeInside.Serials)
            {
                var sn = s.SerialNumber?.Trim();
                if (!string.IsNullOrWhiteSpace(sn))
                {
                    beforeInsideSerials.Add(sn);
                }
            }
        }

        var result = await apiClient.CreateSasBarcodeAsync(new CreateSasBarcodeRequest(
            model.PurchaseOrderNo,
            model.LineNo,
            model.MaterialNumber,
            packageForSap,
            printQuantity,
            labelType,
            User.Identity?.Name,
            model.VendorCode), cancellationToken);

        if (!result.Success && !IsAllowedMockBarcodeResult(result))
        {
            ModelState.AddModelError(string.Empty, FormatSapPrintError(result.ErrorMessage, labelType));
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var usedMock = !result.Success && IsAllowedMockBarcodeResult(result);

        var printResult = await labelPrintService.PrintSasLabelsAsync(
            await BuildPrintContextAsync(model, labelType, result, cancellationToken: cancellationToken),
            cancellationToken);

        if (!printResult.Success)
        {
            ModelState.AddModelError(string.Empty, printResult.ErrorMessage ?? "Yazdırma başarısız.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var allQzJobs = new List<string>();
        var allSavedIds = new List<int>();
        if (printResult.UseQzTray && printResult.RawJobs is { Count: > 0 })
        {
            allQzJobs.AddRange(printResult.RawJobs);
        }

        var savedIds = await SaveBarcodePrintsAsync(
            model,
            result,
            printQuantity,
            labelType,
            isReprint: false,
            status: printResult.UseQzTray ? BarcodePrintStatus.Pending : BarcodePrintStatus.Printed,
            cancellationToken);
        allSavedIds.AddRange(savedIds);

        var successParts = new List<string>();
        if (usedMock)
        {
            successParts.Add(
                $"{printResult.PrintedCount} MOCK {LabelName(labelType)} (SAP: {result.ErrorMessage}).");
        }
        else
        {
            successParts.Add($"{printResult.PrintedCount} {LabelName(labelType)} etiketi.");
        }

        // Halil/Doqu: koli üstü Print SAP'de koli içi serilerini de ZMMIST14000'e yazar.
        // Yeni koli içi Print atma; Serials(KoliIci='X') + Reprint ile bas.
        if (labelType == LabelType.KoliUstu && !usedMock)
        {
            var (insidePrinted, insideNote) = await PrintNewInsideSerialsFromTableAsync(
                model,
                packageForSap,
                beforeInsideSerials,
                allQzJobs,
                allSavedIds,
                cancellationToken);
            if (insidePrinted > 0)
            {
                successParts.Add($"{insidePrinted} koli içi (tablodan Reprint).");
            }
            else if (!string.IsNullOrWhiteSpace(insideNote))
            {
                successParts.Add(insideNote);
            }
        }

        if (allQzJobs.Count > 0)
        {
            FasonBarkod.Web.Filters.QzTrayViewBagFilter.QueueJobs(this, allQzJobs, allSavedIds);
            TempData["Success"] = string.Join(" ", successParts)
                + " QZ Tray'e gönderiliyor…";
        }
        else if (!string.IsNullOrWhiteSpace(printResult.ErrorMessage))
        {
            TempData["Success"] = printResult.ErrorMessage;
        }
        else
        {
            TempData["Success"] = string.Join(" ", successParts);
        }

        return RedirectToDetail(model, "print");
    }

    private static string LabelName(LabelType labelType) =>
        labelType == LabelType.KoliUstu ? "koli üstü" : "koli içi";

    /// <summary>
    /// Koli içi: GetSAPSASBarcodePrint (yeni) değil — ZMMIST14000 Serials + Reprint.
    /// ABAP yeni Print'te DURUM='' AND KOLIB='X' sayar; yoksa Max:0. Masaüstü Reprint kullanır.
    /// </summary>
    private async Task<IActionResult> PrintKoliIciFromSerialsAsync(
        SasDetailViewModel model,
        decimal printQuantity,
        CancellationToken cancellationToken)
    {
        model.VendorCode = await ResolveVendorCodeAsync(model.VendorCode);

        if (!User.IsInRole("Admin") && string.IsNullOrWhiteSpace(model.VendorCode))
        {
            ModelState.AddModelError(string.Empty, "Hesabınıza satıcı kodu tanımlı değil. Yönetici ile iletişime geçin.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(model.LineNo))
        {
            ModelState.AddModelError(string.Empty, "Önce tablodan bir kalem seçin.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        if (printQuantity < 1)
        {
            ModelState.AddModelError(string.Empty, PackageQuantityValidator.BoxInsideZeroError);
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var currentLines = await apiClient.ListSasAsync(model.PurchaseOrderNo, model.VendorCode, cancellationToken);
        var currentLine = currentLines.Lines.FirstOrDefault(l =>
            string.Equals(l.LineNo, model.LineNo, StringComparison.OrdinalIgnoreCase)
            || (int.TryParse(l.LineNo, out var a) && int.TryParse(model.LineNo, out var b) && a == b));
        if (currentLine is null)
        {
            ModelState.AddModelError(string.Empty, "Seçili kalem SAP listesinde bulunamadı. Sayfayı yenileyip tekrar deneyin.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var originalPackage = currentLine.PackageQuantity > 0
            ? currentLine.PackageQuantity
            : model.OriginalPackageQuantity;
        model.OriginalPackageQuantity = originalPackage;
        model.OrderQuantity = currentLine.Quantity;

        if (!User.IsInRole("Admin"))
        {
            model.PackageQuantity = originalPackage > 0 ? originalPackage : currentLine.PackageQuantity;
        }

        if (model.PackageQuantity <= 0)
        {
            ModelState.AddModelError(string.Empty, PackageQuantityValidator.PackageRequiredError);
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var packageForSap = model.PackageQuantity;
        var want = (int)Math.Floor(printQuantity);
        if (want < 1)
        {
            want = 1;
        }

        var serialsResult = await apiClient.ListSasSerialsAsync(
            model.PurchaseOrderNo,
            model.LineNo,
            boxInside: true,
            cancellationToken);

        if (!serialsResult.Success && serialsResult.Serials.Count == 0)
        {
            ModelState.AddModelError(
                string.Empty,
                serialsResult.ErrorMessage
                ?? "Koli içi serileri okunamadı (GetSAPSASBarcodeSerials KoliIci='X').");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        // Önce DURUM boş (ABAP Max sayacı ile aynı), yetmezse diğer seriler (masaüstü Reprint gibi).
        var ordered = serialsResult.Serials
            .Where(s => !string.IsNullOrWhiteSpace(s.SerialNumber))
            .OrderBy(s => string.IsNullOrWhiteSpace(s.Status) ? 0 : 1)
            .ThenBy(s => s.SerialNumber, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.SerialNumber.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(want)
            .ToList();

        if (ordered.Count == 0)
        {
            ModelState.AddModelError(
                string.Empty,
                "ZMMIST14000’de koli içi SERNR yok. Önce koli üstü basın (SAP tabloya iç yazar) "
                + "veya Yeniden Etiket Yazdır’dan seçin. "
                + "Yeni koli içi Print Max:0 verir (DURUM boş satır yok).");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var allQzJobs = new List<string>();
        var allSavedIds = new List<int>();
        var printed = 0;
        var errors = new List<string>();

        foreach (var sernr in ordered)
        {
            var reprint = await apiClient.ReprintSasBarcodeAsync(new ReprintSasBarcodeRequest(
                model.PurchaseOrderNo,
                model.LineNo,
                packageForSap,
                LabelType.KoliIci,
                sernr,
                User.Identity?.Name,
                model.VendorCode), cancellationToken);

            if (!reprint.Success && !IsAllowedMockBarcodeResult(reprint))
            {
                errors.Add($"{sernr}: {reprint.ErrorMessage ?? "Reprint başarısız"}");
                continue;
            }

            var insidePrint = await labelPrintService.PrintSasLabelsAsync(
                await BuildPrintContextAsync(
                    model, LabelType.KoliIci, reprint, sernr, cancellationToken),
                cancellationToken);

            if (!insidePrint.Success)
            {
                errors.Add($"{sernr}: {insidePrint.ErrorMessage ?? "yazdırma hatası"}");
                continue;
            }

            if (insidePrint.UseQzTray && insidePrint.RawJobs is { Count: > 0 })
            {
                allQzJobs.AddRange(insidePrint.RawJobs);
            }

            var saved = await SaveBarcodePrintsAsync(
                model,
                reprint,
                packageForSap,
                LabelType.KoliIci,
                isReprint: true,
                status: insidePrint.UseQzTray ? BarcodePrintStatus.Pending : BarcodePrintStatus.Printed,
                cancellationToken);
            allSavedIds.AddRange(saved);
            printed += insidePrint.PrintedCount > 0 ? insidePrint.PrintedCount : 1;
        }

        if (printed == 0)
        {
            ModelState.AddModelError(
                string.Empty,
                errors.Count > 0
                    ? "Koli içi basılamadı: " + string.Join("; ", errors.Take(3))
                    : "Koli içi basılamadı.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var msg = $"{printed} koli içi etiketi (tablodan Reprint)."
            + (ordered.Count < want ? $" İstenen {want}, tabloda {ordered.Count} SERNR vardı." : "")
            + (errors.Count > 0 ? " Bazı hatalar: " + string.Join("; ", errors.Take(2)) : "");

        if (allQzJobs.Count > 0)
        {
            FasonBarkod.Web.Filters.QzTrayViewBagFilter.QueueJobs(this, allQzJobs, allSavedIds);
            TempData["Success"] = msg + " QZ Tray'e gönderiliyor…";
        }
        else
        {
            TempData["Success"] = msg;
        }

        return RedirectToDetail(model, "print");
    }

    /// <summary>
    /// Koli üstü sonrası: Serials(KoliIci='X') ile yeni serileri bul, Reprint ile bas.
    /// Yeni Print (GetSAPSASBarcodePrint koli içi) kullanılmaz.
    /// </summary>
    private async Task<(int PrintedCount, string? Note)> PrintNewInsideSerialsFromTableAsync(
        SasDetailViewModel model,
        decimal packageForSap,
        HashSet<string> beforeSerials,
        List<string> allQzJobs,
        List<int> allSavedIds,
        CancellationToken cancellationToken)
    {
        var after = await apiClient.ListSasSerialsAsync(
            model.PurchaseOrderNo,
            model.LineNo,
            boxInside: true,
            cancellationToken);

        if (!after.Success && after.Serials.Count == 0)
        {
            return (0, "Koli içi serileri okunamadı: " + (after.ErrorMessage ?? "Serials hatası"));
        }

        var newSerials = after.Serials
            .Select(s => s.SerialNumber?.Trim() ?? "")
            .Where(s => s.Length > 0 && !beforeSerials.Contains(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (newSerials.Count == 0)
        {
            return (0,
                "Koli içi için tabloda yeni SERNR yok (Serials KoliIci='X'). "
                + "Yeniden Etiket Yazdır sekmesinden mevcut iç serileri basabilirsiniz.");
        }

        var printed = 0;
        var errors = new List<string>();

        foreach (var sernr in newSerials)
        {
            var reprint = await apiClient.ReprintSasBarcodeAsync(new ReprintSasBarcodeRequest(
                model.PurchaseOrderNo,
                model.LineNo,
                packageForSap,
                LabelType.KoliIci,
                sernr,
                User.Identity?.Name,
                model.VendorCode), cancellationToken);

            if (!reprint.Success && !IsAllowedMockBarcodeResult(reprint))
            {
                errors.Add($"{sernr}: {reprint.ErrorMessage ?? "Reprint başarısız"}");
                continue;
            }

            var insidePrint = await labelPrintService.PrintSasLabelsAsync(
                await BuildPrintContextAsync(
                    model, LabelType.KoliIci, reprint, sernr, cancellationToken),
                cancellationToken);

            if (!insidePrint.Success)
            {
                errors.Add($"{sernr}: {insidePrint.ErrorMessage ?? "yazdırma hatası"}");
                continue;
            }

            if (insidePrint.UseQzTray && insidePrint.RawJobs is { Count: > 0 })
            {
                allQzJobs.AddRange(insidePrint.RawJobs);
            }

            var saved = await SaveBarcodePrintsAsync(
                model,
                reprint,
                packageForSap > 0 ? packageForSap : 1,
                LabelType.KoliIci,
                isReprint: true,
                status: insidePrint.UseQzTray ? BarcodePrintStatus.Pending : BarcodePrintStatus.Printed,
                cancellationToken);
            allSavedIds.AddRange(saved);
            printed += insidePrint.PrintedCount > 0 ? insidePrint.PrintedCount : 1;
        }

        if (printed == 0 && errors.Count > 0)
        {
            return (0, "Koli içi Reprint: " + string.Join("; ", errors.Take(3)));
        }

        if (errors.Count > 0)
        {
            return (printed, $"Koli içi kısmen: {printed} OK; hata: {string.Join("; ", errors.Take(2))}");
        }

        return (printed, null);
    }

    private async Task<IActionResult> ReprintAsync(
        SasDetailViewModel model,
        LabelType labelType,
        CancellationToken cancellationToken)
    {
        model.VendorCode = await ResolveVendorCodeAsync(model.VendorCode);

        if (!User.IsInRole("Admin") && string.IsNullOrWhiteSpace(model.VendorCode))
        {
            ModelState.AddModelError(string.Empty, "Hesabınıza satıcı kodu tanımlı değil. Yönetici ile iletişime geçin.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(model.LineNo))
        {
            ModelState.AddModelError(string.Empty, "Önce tablodan bir kalem seçin.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var serials = (model.SerialNumbers ?? [])
            .Select(s => s?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        if (serials.Count == 0 && !string.IsNullOrWhiteSpace(model.SerialNumber))
        {
            serials.Add(model.SerialNumber.Trim());
        }

        if (serials.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Tekrar basım için listeden en az bir SERNR seçin.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        model.SerialNumber = serials[0];

        // Bu kalemde basılmış etiket yoksa yeniden basıma izin verme.
        var hasPrinted = await context.BarcodePrints.AsNoTracking().AnyAsync(
            x => x.SalesOrderNo == model.PurchaseOrderNo
                 && x.LineNo == model.LineNo
                 && (x.SapSent || x.Status == BarcodePrintStatus.Printed),
            cancellationToken);
        if (!hasPrinted)
        {
            var lines = await apiClient.ListSasAsync(model.PurchaseOrderNo, model.VendorCode, cancellationToken);
            var line = lines.Lines.FirstOrDefault(l => l.LineNo == model.LineNo);
            hasPrinted = line is { PrintedBoxCount: > 0 } or { PrintedInsideBoxCount: > 0 };
        }

        if (!hasPrinted)
        {
            var serialCheck = await apiClient.ListSasSerialsAsync(
                model.PurchaseOrderNo,
                model.LineNo,
                labelType == LabelType.KoliIci,
                cancellationToken);
            hasPrinted = serialCheck.Serials.Count > 0;
        }

        if (!hasPrinted)
        {
            ModelState.AddModelError(string.Empty, "Bu kalemde henüz basılmış etiket yok. Önce Etiket Yazdır ile basım yapın.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        // Paket sadece Admin değiştirebilir.
        if (!User.IsInRole("Admin"))
        {
            var forcedPackage = model.OriginalPackageQuantity > 0
                ? model.OriginalPackageQuantity
                : model.PackageQuantity;
            if (forcedPackage > 0)
            {
                model.PackageQuantity = forcedPackage;
            }
        }

        // Doqu: koli üstü reprint'te paket kontrolü yok (PaketIci=0). Koli içi reprint'te paket zorunlu.
        if (labelType == LabelType.KoliIci)
        {
            if (model.PackageQuantity <= 0)
            {
                ModelState.AddModelError(string.Empty, PackageQuantityValidator.PackageRequiredError);
                return await RenderDetailWithErrorsAsync(model, cancellationToken);
            }

            if (model.OriginalPackageQuantity > 0 && model.PackageQuantity > model.OriginalPackageQuantity)
            {
                ModelState.AddModelError(string.Empty, PackageQuantityValidator.PackageTooLargeError);
                return await RenderDetailWithErrorsAsync(model, cancellationToken);
            }
        }

        var packageForSap = labelType == LabelType.KoliUstu ? 0 : model.PackageQuantity;
        var allQzJobs = new List<string>();
        var allSavedIds = new List<int>();
        var printed = 0;
        var errors = new List<string>();

        foreach (var sernr in serials)
        {
            var result = await apiClient.ReprintSasBarcodeAsync(new ReprintSasBarcodeRequest(
                model.PurchaseOrderNo,
                model.LineNo,
                packageForSap,
                labelType,
                sernr,
                User.Identity?.Name,
                model.VendorCode), cancellationToken);

            if (!result.Success && !IsAllowedMockBarcodeResult(result))
            {
                errors.Add($"{sernr}: {result.ErrorMessage ?? "SAP tekrar basım başarısız"}");
                continue;
            }

            var printResult = await labelPrintService.PrintSasLabelsAsync(
                await BuildPrintContextAsync(model, labelType, result, sernr, cancellationToken),
                cancellationToken);

            if (!printResult.Success)
            {
                errors.Add($"{sernr}: {printResult.ErrorMessage ?? "Yazdırma başarısız"}");
                continue;
            }

            if (printResult.UseQzTray && printResult.RawJobs is { Count: > 0 })
            {
                allQzJobs.AddRange(printResult.RawJobs);
            }

            var savedIds = await SaveBarcodePrintsAsync(
                model,
                result,
                model.PackageQuantity,
                labelType,
                isReprint: true,
                status: printResult.UseQzTray ? BarcodePrintStatus.Pending : BarcodePrintStatus.Printed,
                cancellationToken);
            allSavedIds.AddRange(savedIds);
            printed += printResult.PrintedCount > 0 ? printResult.PrintedCount : 1;
        }

        if (printed == 0)
        {
            ModelState.AddModelError(
                string.Empty,
                errors.Count > 0
                    ? "Tekrar basım başarısız: " + string.Join("; ", errors.Take(3))
                    : "Tekrar basım başarısız.");
            return await RenderDetailWithErrorsAsync(model, cancellationToken);
        }

        var labelName = labelType == LabelType.KoliUstu ? "koli üstü" : "koli içi";
        var msg = $"{printed} {labelName} etiketi (tekrar, {serials.Count} SERNR)."
            + (errors.Count > 0 ? " Bazı hatalar: " + string.Join("; ", errors.Take(2)) : "");

        if (allQzJobs.Count > 0)
        {
            FasonBarkod.Web.Filters.QzTrayViewBagFilter.QueueJobs(this, allQzJobs, allSavedIds);
            TempData["Success"] = msg + " QZ Tray'e gönderiliyor… Fiziksel basım onaylanınca durum güncellenir.";
        }
        else
        {
            TempData["Success"] = msg;
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
            model.VendorCode,
            ResolveMaterialGroupDescription(model));
    }

    private static List<SasLineDto> SortLinesByLineNo(IReadOnlyList<SasLineDto> lines) =>
        lines
            .OrderBy(l => int.TryParse(l.LineNo, out var parsed) ? parsed : int.MaxValue)
            .ThenBy(l => l.LineNo, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? ResolveMaterialGroupCode(SasDetailViewModel model) =>
        model.Lines.FirstOrDefault(l => l.LineNo == model.LineNo)?.MaterialGroupCode;

    private static string? ResolveMaterialGroupDescription(SasDetailViewModel model) =>
        model.Lines.FirstOrDefault(l => l.LineNo == model.LineNo)?.MaterialGroupDescription;

    /// <summary>
    /// Yazıcı job'ı hazırlandıktan sonra çağrılır.
    /// QZ Tray kullanılıyorsa Status=Pending; tarayıcı QZ başarısını bildirene kadar "Basıldı" yazılmaz.
    /// Sunucu yazıcısı (WindowsRaw) için Status=Printed.
    /// </summary>
    private async Task<IReadOnlyList<int>> SaveBarcodePrintsAsync(
        SasDetailViewModel model,
        SapBarcodeResult result,
        decimal printQuantity,
        LabelType labelType,
        bool isReprint,
        BarcodePrintStatus status,
        CancellationToken cancellationToken)
    {
        var perLabelQty = labelType == LabelType.KoliUstu
            ? model.PackageQuantity
            : 1m;

        var barcodeEntries = ResolveBarcodeEntries(result, model, printQuantity, perLabelQty);
        if (barcodeEntries.Count == 0)
        {
            return Array.Empty<int>();
        }

        var user = await userManager.GetUserAsync(User);
        var labelName = labelType == LabelType.KoliUstu ? "Koli Üstü" : "Koli İçi";
        var operation = isReprint ? "Tekrar basım" : "Yeni basım";
        var vendorCode = string.IsNullOrWhiteSpace(model.VendorCode) ? null : model.VendorCode.Trim();
        var saved = new List<BarcodePrint>();
        var serialByBarcode = BuildSerialLookup(result);

        foreach (var barcodeNo in barcodeEntries)
        {
            serialByBarcode.TryGetValue(barcodeNo, out var serial);
            if (string.IsNullOrWhiteSpace(serial) && !string.IsNullOrWhiteSpace(model.SerialNumber) && isReprint)
            {
                serial = model.SerialNumber.Trim();
            }

            var print = new BarcodePrint
            {
                BarcodeNo = barcodeNo,
                SalesOrderNo = model.PurchaseOrderNo,
                LineNo = model.LineNo,
                VendorCode = vendorCode,
                LabelType = labelType,
                MaterialCode = model.MaterialNumber,
                MaterialName = model.MaterialDescription,
                SerialNumber = string.IsNullOrWhiteSpace(serial) ? null : serial.Trim(),
                Quantity = perLabelQty,
                PrintDate = DateTime.UtcNow,
                PrintedByUserId = user?.Id,
                Status = status,
                SapSent = result.Success
            };

            context.BarcodePrints.Add(print);
            saved.Add(print);

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
        return saved.Select(x => x.Id).ToList();
    }

    private static IReadOnlyList<string> ResolveBarcodeEntries(
        SapBarcodeResult result,
        SasDetailViewModel model,
        decimal printQuantity,
        decimal perLabelQty)
    {
        if (result.BarcodeNumbers.Count > 0)
        {
            return result.BarcodeNumbers
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (result.Labels is { Count: > 0 } labels)
        {
            var fromLabels = labels
                .Select(ResolvePrimaryBarcode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fromLabels.Count > 0)
            {
                return fromLabels;
            }

            var fromSerials = labels
                .Select(l => l.SerialNumber?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fromSerials.Count > 0)
            {
                return fromSerials;
            }
        }

        var labelCount = result.PrintBarcodeQuantity > 0
            ? result.PrintBarcodeQuantity
            : (int)Math.Max(1, Math.Ceiling(printQuantity / Math.Max(perLabelQty, 1m)));

        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        return Enumerable.Range(1, labelCount)
            .Select(i => $"{model.PurchaseOrderNo}-{model.LineNo}-{stamp}-{i}")
            .ToList();
    }

    private static string ResolvePrimaryBarcode(SasBarcodeLabelDto label)
    {
        foreach (var candidate in new[] { label.Barcode1, label.Barcode2, label.Barcode3, label.Barcode4 })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var trimmed = candidate.Trim();
            if (!string.IsNullOrWhiteSpace(label.MaterialNumber)
                && trimmed.Equals(label.MaterialNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return trimmed;
        }

        return string.Empty;
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
        var (refreshed, _) = await BuildDetailViewModelAsync(
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
            model.VendorCode,
            model.LineNo,
            cancellationToken);
        await LoadSapSerialsAsync(refreshed, cancellationToken);
        return View("Detail", refreshed);
    }

    private async Task LoadSapSerialsAsync(
        SasDetailViewModel model,
        CancellationToken cancellationToken)
    {
        model.SapSerialsBoxTop = [];
        model.SapSerialsBoxInside = [];
        model.SapSerialsError = null;

        if (string.IsNullOrWhiteSpace(model.PurchaseOrderNo) || string.IsNullOrWhiteSpace(model.LineNo))
        {
            return;
        }

        var topTask = apiClient.ListSasSerialsAsync(
            model.PurchaseOrderNo, model.LineNo, boxInside: false, cancellationToken);
        var insideTask = apiClient.ListSasSerialsAsync(
            model.PurchaseOrderNo, model.LineNo, boxInside: true, cancellationToken);
        await Task.WhenAll(topTask, insideTask);

        var top = await topTask;
        var inside = await insideTask;

        model.SapSerialsBoxTop = top.Serials.ToList();
        model.SapSerialsBoxInside = inside.Serials.ToList();

        // SOAP boşsa (test/canlı uyumsuzluğu vb.) bu uygulamada basılan SERNR/barkodları göster.
        if (model.SapSerialsBoxTop.Count == 0 || model.SapSerialsBoxInside.Count == 0)
        {
            var local = await LoadLocalSerialsAsync(
                model.PurchaseOrderNo, model.LineNo, model.VendorCode, cancellationToken);
            if (model.SapSerialsBoxTop.Count == 0)
            {
                model.SapSerialsBoxTop = local.BoxTop;
            }

            if (model.SapSerialsBoxInside.Count == 0)
            {
                model.SapSerialsBoxInside = local.BoxInside;
            }
        }

        if (model.SapSerialsCount == 0)
        {
            model.SapSerialsError = top.ErrorMessage ?? inside.ErrorMessage;
            if (string.IsNullOrWhiteSpace(model.SapSerialsError))
            {
                model.SapSerialsError =
                    "Bu kalemde henüz listelenecek seri yok. Önce etiket basımı yapın; " +
                    "SOAP servisi ile SAP ortamı (test/canlı) aynı olmalı.";
            }
        }

        if (string.IsNullOrWhiteSpace(model.SerialNumber))
        {
            model.SerialNumber = model.SapSerialsBoxTop.FirstOrDefault()?.SerialNumber
                ?? model.SapSerialsBoxInside.FirstOrDefault()?.SerialNumber
                ?? string.Empty;
        }
    }

    private async Task<(List<SasSerialDto> BoxTop, List<SasSerialDto> BoxInside)> LoadLocalSerialsAsync(
        string purchaseOrderNo,
        string lineNo,
        string? vendorCode,
        CancellationToken cancellationToken)
    {
        var query = context.BarcodePrints
            .AsNoTracking()
            .Where(x => x.SalesOrderNo == purchaseOrderNo && x.SapSent);

        if (!string.IsNullOrWhiteSpace(lineNo))
        {
            query = query.Where(x => x.LineNo == lineNo);
        }

        if (!string.IsNullOrWhiteSpace(vendorCode))
        {
            query = query.Where(x => x.VendorCode == vendorCode);
        }

        var rows = await query
            .OrderByDescending(x => x.PrintDate)
            .Select(x => new { x.SerialNumber, x.BarcodeNo, x.LabelType, x.Quantity })
            .Take(200)
            .ToListAsync(cancellationToken);

        static string PickSernr(string? serial, string? barcode) =>
            !string.IsNullOrWhiteSpace(serial) ? serial.Trim() : (barcode?.Trim() ?? string.Empty);

        var top = rows
            .Where(x => x.LabelType == LabelType.KoliUstu)
            .Select(x => PickSernr(x.SerialNumber, x.BarcodeNo))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => new SasSerialDto(purchaseOrderNo, lineNo, s, 0, "AD", "*", " "))
            .ToList();

        var inside = rows
            .Where(x => x.LabelType == LabelType.KoliIci)
            .Select(x => PickSernr(x.SerialNumber, x.BarcodeNo))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => new SasSerialDto(purchaseOrderNo, lineNo, s, 0, "AD", "*", "X"))
            .ToList();

        return (top, inside);
    }

    private static string FormatSapPrintError(string? sapMessage, LabelType labelType)
    {
        if (string.IsNullOrWhiteSpace(sapMessage))
        {
            return "SAP barkod alınamadı.";
        }

        if (sapMessage.Contains("Max:", StringComparison.OrdinalIgnoreCase)
            && (sapMessage.Contains("koli içi", StringComparison.OrdinalIgnoreCase)
                || labelType == LabelType.KoliIci))
        {
            return "SAP koli içi basıma izin vermedi (Max: 0). "
                + "SAP tarafındaki kota/tanım bu basımı reddetti. "
                + $"({sapMessage})";
        }

        if (sapMessage.Contains("Max:", StringComparison.OrdinalIgnoreCase)
            && (sapMessage.Contains("koli üstü", StringComparison.OrdinalIgnoreCase)
                || labelType == LabelType.KoliUstu))
        {
            return "SAP koli üstü basıma izin vermedi (Max: 0). "
                + "Paket sipariş miktarının katı olmalı ve kalan kota olmalı. "
                + $"({sapMessage})";
        }

        return sapMessage;
    }

    private static void ApplyPostedPrintState(SasDetailViewModel target, SasDetailViewModel source)
    {
        target.PackageQuantity = source.PackageQuantity;
        target.OriginalPackageQuantity = source.OriginalPackageQuantity > 0
            ? source.OriginalPackageQuantity
            : target.OriginalPackageQuantity;
        target.OrderQuantity = source.OrderQuantity > 0 ? source.OrderQuantity : target.OrderQuantity;
        target.KoliUstuQuantity = source.KoliUstuQuantity;
        target.KoliIciQuantity = source.KoliIciQuantity;
        target.SerialNumber = source.SerialNumber;
        target.ActiveTab = source.ActiveTab;
    }

    private async Task<(SasDetailViewModel? Model, string? Error)> BuildDetailViewModelAsync(
        string purchaseOrderNo,
        string? vendorCode,
        string? lineNo,
        CancellationToken cancellationToken)
    {
        var response = await apiClient.ListSasAsync(purchaseOrderNo, vendorCode, cancellationToken);
        if (response.Lines.Count == 0)
        {
            
            logger.LogWarning(
                "SAS bulunamadı: {OrderNo}, cari={Vendor}, sap={Sap}, hint={Hint}",
                purchaseOrderNo,
                vendorCode ?? "(boş)",
                response.SapMessage ?? "(yok)",
                response.Hint ?? "(yok)");

            var message = !string.IsNullOrWhiteSpace(response.Error)
                ? response.Error!
                : $"“{purchaseOrderNo}” numaralı SAS bulunamadı. Numarayı kontrol edin.";

            if (!string.IsNullOrWhiteSpace(vendorCode))
            {
                message += " Satıcı kodunuz ile eşleşmiyor olabilir.";
            }

            return (null, message);
        }

        var lines = response.Lines.ToList();
        var resolvedVendor = !string.IsNullOrWhiteSpace(vendorCode)
            ? vendorCode.Trim()
            : lines.Select(l => l.VendorCode).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

        // SAP satırında LIFNR yok; istekteki / hesaptaki satıcı kodunu satırlara yaz.
        if (string.IsNullOrWhiteSpace(resolvedVendor))
        {
            resolvedVendor = await FindVendorCodeForOrderAsync(purchaseOrderNo, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(resolvedVendor))
        {
            var v = resolvedVendor.Trim();
            lines = lines
                .Select(l => string.IsNullOrWhiteSpace(l.VendorCode) ? l with { VendorCode = v } : l)
                .ToList();
        }

        var model = new SasDetailViewModel
        {
            PurchaseOrderNo = purchaseOrderNo,
            VendorCode = resolvedVendor,
            Lines = SortLinesByLineNo(lines)
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

        return (model, null);
    }

    private async Task<List<SasOrderSummaryViewModel>> LoadVendorOrderSummariesAsync(
        string vendorCode,
        CancellationToken cancellationToken)
    {
        var trimmed = vendorCode.Trim();
        var padded = trimmed.PadLeft(10, '0');
        var candidates = new[] { trimmed, padded, trimmed.TrimStart('0') }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rows = await context.BarcodePrints
            .AsNoTracking()
            .Where(x => x.VendorCode != null && candidates.Contains(x.VendorCode))
            .GroupBy(x => x.SalesOrderNo)
            .Select(g => new
            {
                PurchaseOrderNo = g.Key,
                LineCount = g.Select(x => x.LineNo).Distinct().Count(),
                SampleMaterial = g.OrderByDescending(x => x.PrintDate)
                    .Select(x => x.MaterialName)
                    .FirstOrDefault(),
                VendorCode = g.Select(x => x.VendorCode)
                    .FirstOrDefault(v => v != null && v != ""),
                LastPrint = g.Max(x => x.PrintDate)
            })
            .OrderByDescending(x => x.LastPrint)
            .Take(100)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new SasOrderSummaryViewModel
        {
            PurchaseOrderNo = x.PurchaseOrderNo,
            LineCount = x.LineCount,
            SampleMaterial = x.SampleMaterial,
            VendorCode = x.VendorCode ?? trimmed
        }).ToList();
    }

    private async Task<Dictionary<string, string>> LoadVendorCodesByOrdersAsync(
        IReadOnlyList<string> orderNos,
        CancellationToken cancellationToken)
    {
        if (orderNos.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var rows = await context.BarcodePrints
            .AsNoTracking()
            .Where(x => orderNos.Contains(x.SalesOrderNo) && x.VendorCode != null && x.VendorCode != "")
            .Select(x => new { x.SalesOrderNo, x.VendorCode, x.PrintDate })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.SalesOrderNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.PrintDate).Select(x => x.VendorCode!).First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string?> FindVendorCodeForOrderAsync(
        string purchaseOrderNo,
        CancellationToken cancellationToken)
    {
        return await context.BarcodePrints
            .AsNoTracking()
            .Where(x => x.SalesOrderNo == purchaseOrderNo && x.VendorCode != null && x.VendorCode != "")
            .OrderByDescending(x => x.PrintDate)
            .Select(x => x.VendorCode)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<List<BarcodePrintListItemViewModel>> LoadRecentPrintsAsync(
        string purchaseOrderNo,
        string? materialCode,
        string? vendorCode,
        string? lineNo,
        CancellationToken cancellationToken)
    {
        var query = context.BarcodePrints
            .AsNoTracking()
            .Include(x => x.PrintedByUser)
            .Where(x => x.SalesOrderNo == purchaseOrderNo);

        if (!string.IsNullOrWhiteSpace(materialCode))
        {
            query = query.Where(x => x.MaterialCode == materialCode);
        }

        if (!string.IsNullOrWhiteSpace(lineNo))
        {
            query = query.Where(x => x.LineNo == lineNo);
        }

        if (!string.IsNullOrWhiteSpace(vendorCode))
        {
            query = query.Where(x => x.VendorCode == vendorCode);
        }

        return await query
            .OrderByDescending(x => x.PrintDate)
            .Take(20)
            .Select(x => new BarcodePrintListItemViewModel
            {
                Id = x.Id,
                BarcodeNo = x.BarcodeNo,
                SalesOrderNo = x.SalesOrderNo,
                LineNo = x.LineNo,
                VendorCode = x.VendorCode,
                LabelType = x.LabelType,
                MaterialCode = x.MaterialCode,
                MaterialName = x.MaterialName,
                Quantity = x.Quantity,
                PrintDate = x.PrintDate,
                PrintedBy = x.PrintedByUser != null ? x.PrintedByUser.FullName ?? x.PrintedByUser.Email : null,
                SerialNumber = x.SerialNumber,
                Status = x.Status,
                SapSent = x.SapSent
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<string?> ResolveLatestSerialForLineAsync(
        string purchaseOrderNo,
        string? lineNo,
        string? materialCode,
        string? vendorCode,
        CancellationToken cancellationToken)
    {
        var query = context.BarcodePrints
            .AsNoTracking()
            .Where(x => x.SalesOrderNo == purchaseOrderNo && x.SapSent);

        if (!string.IsNullOrWhiteSpace(lineNo))
        {
            query = query.Where(x => x.LineNo == lineNo);
        }

        if (!string.IsNullOrWhiteSpace(materialCode))
        {
            query = query.Where(x => x.MaterialCode == materialCode);
        }

        if (!string.IsNullOrWhiteSpace(vendorCode))
        {
            query = query.Where(x => x.VendorCode == vendorCode);
        }

        var latest = await query
            .OrderByDescending(x => x.PrintDate)
            .Select(x => new { x.SerialNumber, x.BarcodeNo })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(latest.SerialNumber)
            ? latest.SerialNumber.Trim()
            : latest.BarcodeNo?.Trim();
    }

    private static Dictionary<string, string> BuildSerialLookup(SapBarcodeResult result)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (result.Labels is not { Count: > 0 })
        {
            return map;
        }

        foreach (var label in result.Labels)
        {
            var barcode = ResolvePrimaryBarcode(label);
            var serial = label.SerialNumber?.Trim();
            if (string.IsNullOrWhiteSpace(barcode))
            {
                continue;
            }

            // SAP SERNR boşsa tekrar basımda barkod numarası kullanılır.
            map[barcode] = !string.IsNullOrWhiteSpace(serial) ? serial : barcode;
        }

        return map;
    }

    private bool IsAllowedMockBarcodeResult(SapBarcodeResult result) =>
        _testingOptions.AllowMockBarcodes &&
        result.BarcodeNumbers.Count > 0 &&
        result.BarcodeNumbers.All(static b =>
            b.StartsWith("MOCK-", StringComparison.OrdinalIgnoreCase));
}
