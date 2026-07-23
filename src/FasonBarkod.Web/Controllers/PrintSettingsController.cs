using FasonBarkod.Core.Entities;
using FasonBarkod.Infrastructure.Configuration;
using FasonBarkod.Web.Models;
using FasonBarkod.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FasonBarkod.Web.Controllers;

[Authorize(Roles = "Admin,Operator")]
public class PrintSettingsController(
    IPrintSettingsAdminService adminService,
    IPrintSettingsStore settingsStore,
    UserManager<ApplicationUser> userManager,
    IOptions<PrinterOptions> printerOptions) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await adminService.BuildViewModelAsync(cancellationToken);
        model.UseQzTray = printerOptions.Value.UseQzTray;
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SavePrintSettingsViewModel model, CancellationToken cancellationToken)
    {
        var useQz = printerOptions.Value.UseQzTray;

        if (!model.UseWindowsDefaultPrinter && string.IsNullOrWhiteSpace(model.SelectedPrinterName))
        {
            ModelState.AddModelError(nameof(model.SelectedPrinterName), "Varsayılan yazıcı kullanılmıyorsa bir yazıcı seçmelisiniz.");
            var vm = await adminService.BuildViewModelAsync(cancellationToken);
            vm.UseQzTray = useQz;
            return View(vm);
        }

        // QZ Tray: tercih tarayıcı localStorage'da tutulur; DB'ye de kaydedilir (bilgi / yedek).
        var user = await userManager.GetUserAsync(User);
        await settingsStore.SaveAsync(
            new PrintSettings
            {
                PrinterName = model.UseWindowsDefaultPrinter ? null : model.SelectedPrinterName?.Trim()
            },
            user?.Id,
            cancellationToken);

        TempData["Success"] = useQz
            ? "Yazıcı tercihi kaydedildi. Bu bilgisayarda QZ Tray ile basım bu yazıcıya gidecek."
            : "Yazıcı ayarları kaydedildi.";
        return RedirectToAction(nameof(Index));
    }
}
