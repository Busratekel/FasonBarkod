using FasonBarkod.Core.Entities;
using FasonBarkod.Web.Models;
using FasonBarkod.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FasonBarkod.Web.Controllers;

[Authorize(Roles = "Admin")]
public class PrintSettingsController(
    IPrintSettingsAdminService adminService,
    IPrintSettingsStore settingsStore,
    UserManager<ApplicationUser> userManager) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await adminService.BuildViewModelAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SavePrintSettingsViewModel model, CancellationToken cancellationToken)
    {
        if (!model.UseWindowsDefaultPrinter && string.IsNullOrWhiteSpace(model.SelectedPrinterName))
        {
            ModelState.AddModelError(nameof(model.SelectedPrinterName), "Varsayılan yazıcı kullanılmıyorsa bir yazıcı seçmelisiniz.");
            return View(await adminService.BuildViewModelAsync(cancellationToken));
        }

        var user = await userManager.GetUserAsync(User);
        await settingsStore.SaveAsync(
            new PrintSettings
            {
                PrinterName = model.UseWindowsDefaultPrinter ? null : model.SelectedPrinterName?.Trim()
            },
            user?.Id,
            cancellationToken);

        TempData["Success"] = "Yazıcı ayarları kaydedildi.";
        return RedirectToAction(nameof(Index));
    }
}
