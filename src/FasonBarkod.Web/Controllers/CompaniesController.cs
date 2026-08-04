using FasonBarkod.Core.Entities;
using FasonBarkod.Core.Security;
using FasonBarkod.Infrastructure.Data;
using FasonBarkod.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FasonBarkod.Web.Controllers;

[Authorize(Roles = AppRoles.SuperAdmin)]
public class CompaniesController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var items = await db.Companies
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CompanyListItemViewModel
            {
                Id = c.Id,
                Name = c.Name,
                Code = c.Code,
                IsActive = c.IsActive,
                UserCount = c.Users.Count
            })
            .ToListAsync(cancellationToken);

        return View(items);
    }

    [HttpGet]
    public IActionResult Create() => View(new CompanyEditViewModel { IsActive = true });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CompanyEditViewModel model, CancellationToken cancellationToken)
    {
        Normalize(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (await db.Companies.AnyAsync(c => c.Code == model.Code, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Code), "Bu şirket kodu zaten kullanılıyor.");
            return View(model);
        }

        db.Companies.Add(new Company
        {
            Name = model.Name,
            Code = model.Code,
            IsActive = model.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "Şirket oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var company = await db.Companies.FindAsync([id], cancellationToken);
        if (company is null)
        {
            return NotFound();
        }

        return View(new CompanyEditViewModel
        {
            Id = company.Id,
            Name = company.Name,
            Code = company.Code,
            IsActive = company.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CompanyEditViewModel model, CancellationToken cancellationToken)
    {
        Normalize(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var company = await db.Companies.FindAsync([model.Id], cancellationToken);
        if (company is null)
        {
            return NotFound();
        }

        if (await db.Companies.AnyAsync(c => c.Code == model.Code && c.Id != model.Id, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Code), "Bu şirket kodu zaten kullanılıyor.");
            return View(model);
        }

        if (!model.IsActive && company.IsActive)
        {
            var hasActiveUsers = await db.Users.AnyAsync(
                u => u.CompanyId == company.Id && u.IsActive,
                cancellationToken);
            if (hasActiveUsers)
            {
                ModelState.AddModelError(
                    nameof(model.IsActive),
                    "Aktif kullanıcıları olan şirket pasif yapılamaz. Önce kullanıcıları taşıyın veya pasifleştirin.");
                return View(model);
            }
        }

        company.Name = model.Name;
        company.Code = model.Code;
        company.IsActive = model.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "Şirket güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    private static void Normalize(CompanyEditViewModel model)
    {
        model.Name = model.Name?.Trim() ?? string.Empty;
        model.Code = (model.Code?.Trim() ?? string.Empty).ToUpperInvariant();
    }
}
