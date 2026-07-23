using FasonBarkod.Core.Entities;
using FasonBarkod.Infrastructure.Data;
using FasonBarkod.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FasonBarkod.Web.Controllers;

[Authorize]
public class BarcodePrintsController(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager) : Controller
{
    public async Task<IActionResult> Index(
        string? vendorCode,
        string? purchaseOrderNo,
        string sort = "date",
        string sortDir = "desc",
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var isAdmin = User.IsInRole("Admin");
        var userVendor = await GetUserVendorCodeAsync();
        var resolvedVendor = isAdmin
            ? (string.IsNullOrWhiteSpace(vendorCode) ? null : vendorCode.Trim())
            : userVendor;

        if (!isAdmin && string.IsNullOrWhiteSpace(resolvedVendor))
        {
            return View(new BarcodePrintIndexViewModel
            {
                VendorCodeLocked = true,
                Sort = sort,
                SortDir = sortDir,
                Page = page,
                PageSize = pageSize
            });
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var query = context.BarcodePrints.AsNoTracking();

        // Cari/operatör: kendi satıcı kodundaki tüm basımlar (aynı firmanın ortak geçmişi).
        // Admin: tümü; isteğe bağlı satıcı filtresi.
        if (!string.IsNullOrWhiteSpace(resolvedVendor))
        {
            query = query.Where(x => x.VendorCode == resolvedVendor);
        }

        if (!string.IsNullOrWhiteSpace(purchaseOrderNo))
        {
            var po = purchaseOrderNo.Trim();
            query = query.Where(x => x.SalesOrderNo == po);
        }

        query = ApplySort(query, sort, sortDir);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Include(x => x.PrintedByUser)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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

        return View(new BarcodePrintIndexViewModel
        {
            Items = items,
            VendorCode = resolvedVendor,
            PurchaseOrderNo = purchaseOrderNo,
            VendorCodeLocked = !isAdmin,
            Sort = sort,
            SortDir = sortDir.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc",
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int orderLineId, CancellationToken cancellationToken)
    {
        var line = await context.SalesOrderLines.FindAsync([orderLineId], cancellationToken);
        if (line is null)
        {
            return RedirectToAction("Index", "Sas");
        }

        TempData.Remove("Error");
        TempData.Remove("Success");
        return RedirectToAction("Detail", "Sas", new { id = line.SalesOrderNo });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(CreateBarcodePrintViewModel model)
    {
        return RedirectToAction("Index", "Sas");
    }

    /// <summary>
    /// QZ Tray tarayıcıda basımı bitince (veya hata verince) durumu günceller.
    /// Job hazırlanınca Pending yazılır; gerçek Basıldı/Hata buradan gelir.
    /// </summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ConfirmQzPrint(
        [FromBody] ConfirmQzPrintRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.Ids is not { Count: > 0 })
        {
            return BadRequest(new { ok = false, message = "Kayıt id listesi boş." });
        }

        var success = request.Success;
        var ids = request.Ids.Distinct().Take(200).ToList();
        var prints = await context.BarcodePrints
            .Where(x => ids.Contains(x.Id) && x.Status == Core.Enums.BarcodePrintStatus.Pending)
            .ToListAsync(cancellationToken);

        var updated = 0;
        foreach (var print in prints)
        {
            if (!await CanAccessPrintAsync(print, cancellationToken))
            {
                continue;
            }

            print.Status = success
                ? Core.Enums.BarcodePrintStatus.Printed
                : Core.Enums.BarcodePrintStatus.Failed;
            if (!success && !string.IsNullOrWhiteSpace(request.ErrorMessage))
            {
                context.SapTransferLogs.Add(new SapTransferLog
                {
                    BarcodePrintId = print.Id,
                    Result = Core.Enums.SapTransferResult.Failed,
                    ErrorMessage = request.ErrorMessage.Length > 500
                        ? request.ErrorMessage[..500]
                        : request.ErrorMessage,
                    RequestPayload = "QZ Tray basım onayı",
                    ResponsePayload = "Başarısız"
                });
            }

            updated++;
        }

        if (updated > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return Json(new { ok = true, updated, success });
    }

    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken)
    {
        var print = await context.BarcodePrints
            .Include(x => x.PrintedByUser)
            .Include(x => x.TransferLogs.OrderByDescending(t => t.TransferDate))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (print is null)
        {
            return NotFound();
        }

        if (!await CanAccessPrintAsync(print, cancellationToken))
        {
            return Forbid();
        }

        return View(print);
    }

    private async Task<string?> GetUserVendorCodeAsync()
    {
        var user = await userManager.GetUserAsync(User);
        return string.IsNullOrWhiteSpace(user?.VendorCode) ? null : user.VendorCode.Trim();
    }

    private async Task<bool> CanAccessPrintAsync(BarcodePrint print, CancellationToken cancellationToken)
    {
        if (User.IsInRole("Admin"))
        {
            return true;
        }

        var userVendor = await GetUserVendorCodeAsync();
        if (string.IsNullOrWhiteSpace(userVendor))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(print.VendorCode))
        {
            return print.VendorCode.Equals(userVendor, StringComparison.OrdinalIgnoreCase);
        }

        var user = await userManager.GetUserAsync(User);
        return user?.Id != null && print.PrintedByUserId == user.Id;
    }

    private static IQueryable<BarcodePrint> ApplySort(IQueryable<BarcodePrint> query, string sort, string sortDir)
    {
        var ascending = sortDir.Equals("asc", StringComparison.OrdinalIgnoreCase);

        return sort.ToLowerInvariant() switch
        {
            "order" => ascending
                ? query.OrderBy(x => x.SalesOrderNo).ThenByDescending(x => x.PrintDate)
                : query.OrderByDescending(x => x.SalesOrderNo).ThenByDescending(x => x.PrintDate),
            "barcode" => ascending
                ? query.OrderBy(x => x.BarcodeNo).ThenByDescending(x => x.PrintDate)
                : query.OrderByDescending(x => x.BarcodeNo).ThenByDescending(x => x.PrintDate),
            _ => ascending
                ? query.OrderBy(x => x.PrintDate)
                : query.OrderByDescending(x => x.PrintDate)
        };
    }
}
