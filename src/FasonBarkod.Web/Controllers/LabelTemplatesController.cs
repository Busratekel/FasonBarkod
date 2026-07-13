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

    private static string GetStarterTemplate(LabelType labelType) =>
        labelType == LabelType.KoliUstu
            ? """
              ^L
              Dy2-me-dd
              H10,30,@BARKOD1
              A40,30,0,3,3,0,0,@MAKTX
              A40,70,0,2,2,0,0,@MATNR
              A40,100,0,2,2,0,0,KOLI @ZPAKET ADET
              A40,130,0,2,2,0,0,SAS @EBELN / @EBELP
              A40,160,0,2,2,0,0,@BRAND @ZYIL-H@ZHAFTA
              ^E
              """
            : """
              ^L
              Dy2-me-dd
              H10,30,@BARKOD1
              H10,90,@BARKOD2
              A40,30,0,2,2,0,0,@MAKTX
              A40,60,0,2,2,0,0,@MATNR
              A40,90,0,2,2,0,0,SERNR @SERNR
              A40,120,0,2,2,0,0,SAS @EBELN / @EBELP
              A40,150,0,2,2,0,0,@BRAND
              ^E
              """;
}
