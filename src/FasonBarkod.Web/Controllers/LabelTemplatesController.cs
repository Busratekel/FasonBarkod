using FasonBarkod.Core.Entities;
using FasonBarkod.Core.Enums;
using FasonBarkod.Infrastructure.Services;
using FasonBarkod.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FasonBarkod.Web.Controllers;

[Authorize(Roles = "Admin")]
public class LabelTemplatesController(
    ILabelTemplateService labelTemplateService,
    UserManager<ApplicationUser> userManager) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var templates = await labelTemplateService.ListAsync(cancellationToken);
        var model = templates.Select(x => new LabelTemplateListItemViewModel
        {
            Id = x.Id,
            Name = x.Name,
            BrandCode = x.BrandCode,
            LabelType = x.LabelType,
            IsActive = x.IsActive,
            UpdatedAt = x.UpdatedAt
        }).ToList();

        return View(model);
    }

    [HttpGet]
    public IActionResult Create(LabelType? labelType)
    {
        return View(new LabelTemplateEditViewModel
        {
            LabelType = labelType ?? LabelType.KoliUstu,
            Content = GetStarterTemplate(labelType ?? LabelType.KoliUstu)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LabelTemplateEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.GetUserAsync(User);
        var entity = MapToEntity(model, user?.Id);
        var result = await labelTemplateService.SaveAsync(entity, cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(model);
        }

        TempData["Success"] = "Etiket tasarımı kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var template = await labelTemplateService.GetByIdAsync(id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        return View(MapToEditModel(template));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(LabelTemplateEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existing = await labelTemplateService.GetByIdAsync(model.Id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var user = await userManager.GetUserAsync(User);
        var entity = MapToEntity(model, user?.Id);
        var result = await labelTemplateService.SaveAsync(entity, cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(model);
        }

        TempData["Success"] = "Etiket tasarımı güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await labelTemplateService.DeleteAsync(id, cancellationToken);
        TempData["Success"] = "Etiket tasarımı silindi.";
        return RedirectToAction(nameof(Index));
    }

    private static LabelTemplate MapToEntity(LabelTemplateEditViewModel model, string? userId) =>
        new()
        {
            Id = model.Id,
            Name = model.Name,
            BrandCode = model.BrandCode,
            LabelType = model.LabelType,
            Content = model.Content,
            IsActive = model.IsActive,
            UpdatedByUserId = userId
        };

    private static LabelTemplateEditViewModel MapToEditModel(LabelTemplate template) =>
        new()
        {
            Id = template.Id,
            Name = template.Name,
            BrandCode = template.BrandCode,
            LabelType = template.LabelType,
            Content = template.Content,
            IsActive = template.IsActive
        };

    // NOT: Gerçek SBPL (<ESC>=0x1B). Her şablonda A1 medya boyutu gömülü (appsettings'ten bağımsız).
    // Koli üstü: dikey ~110x80mm (V0880 H0640 @203dpi).
    // Koli içi: yatay ~50x100mm (V0400 H0800).
    // QR: BQ abcc — a=ECC, b=concat, cc=hücre(2 hane).
    private static string GetStarterTemplate(LabelType labelType) =>
        labelType == LabelType.KoliUstu
            ? "\u001BA\n" +
              "\u001BA1V00880H00640\n" +
              "\u001BV0030\u001BH0010\u001BXM@ZYIL / @ZHAFTA  @C\n" +
              "\u001BV0020\u001BH0340\u001BXB1@BEZEI\n" +
              "\u001BV0020\u001BH0660\u001BBQ2005,1@QR\n" +
              "\u001BV0120\u001BH0010\u001BXB1@MATNR\n" +
              "\u001BV0190\u001BH0010\u001BXB1@MAKTX\n" +
              "\u001BV0270\u001BH0100\u001BXB1KOLI ( @ZPAKET ADET )\n" +
              "\u001BV0350\u001BH0010\u001BXS@TARIH\n" +
              "\u001BV0350\u001BH0200\u001BXS@T\n" +
              "\u001BV0400\u001BH0010\u001BBG02160@BARKOD3T\n" +
              "\u001BV0575\u001BH0010\u001BXM@BARKOD3T\n" +
              "\u001BV0620\u001BH0010\u001BBG02160@BARKOD4T\n" +
              "\u001BV0795\u001BH0010\u001BXM@BARKOD4T\n" +
              "\u001BV0840\u001BH0010\u001BXMKod: @MATNR\n" +
              "\u001BQ000001\n" +
              "\u001BZ"
            // Sol: MAKTX + barkod1+metin + barkod2+metin
            // Sağ üst: küçük QR + yanında marka + altında counter/T
            : "\u001BA\n" +
              "\u001BA1V00400H00800\n" +
              "\u001BV0015\u001BH0015\u001BXS@MAKTX\n" +
              "\u001BV0015\u001BH0580\u001BBQ2003,1@QR\n" +
              "\u001BV0015\u001BH0660\u001BXS@BEZEI\n" +
              "\u001BV0045\u001BH0660\u001BXS@Counter / @T\n" +
              "\u001BV0045\u001BH0015\u001BBG02048@BARKOD1\n" +
              "\u001BV0100\u001BH0015\u001BXS@BARKOD1\n" +
              "\u001BV0125\u001BH0015\u001BBG02050@BARKOD2\n" +
              "\u001BV0185\u001BH0015\u001BXS@BARKOD2\n" +
              "\u001BQ000001\n" +
              "\u001BZ";
}
