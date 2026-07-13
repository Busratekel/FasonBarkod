using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FasonBarkod.Web.Controllers;

/// <summary>
/// Eski prototip ekranı — artık SAS modülü kullanılıyor.
/// </summary>
[Authorize]
public class OrdersController : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Sas");

    public IActionResult Detail(string id) =>
        string.IsNullOrWhiteSpace(id)
            ? RedirectToAction("Index", "Sas")
            : RedirectToAction("Detail", "Sas", new { id });
}
