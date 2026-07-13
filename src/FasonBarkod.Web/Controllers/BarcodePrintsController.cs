using FasonBarkod.Infrastructure.Data;
using FasonBarkod.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FasonBarkod.Web.Controllers;

[Authorize]
public class BarcodePrintsController(ApplicationDbContext context) : Controller
{    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var prints = await context.BarcodePrints
            .Where(x => !x.BarcodeNo.StartsWith("MOCK-") && !x.BarcodeNo.StartsWith("FSN-"))
            .Include(x => x.PrintedByUser)
            .OrderByDescending(x => x.PrintDate)
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

        return View(prints);
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

        return View(print);
    }
}
