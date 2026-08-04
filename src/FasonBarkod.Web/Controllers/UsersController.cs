using FasonBarkod.Core.Entities;
using FasonBarkod.Core.Security;
using FasonBarkod.Infrastructure.Data;
using FasonBarkod.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FasonBarkod.Web.Controllers;

[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.SuperAdmin},{AppRoles.Operator}")]
public class UsersController(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var actor = await GetActorAsync();
        if (actor is null)
        {
            return Forbid();
        }

        var users = await LoadVisibleUsersAsync(actor);
        var items = new List<UserListItemViewModel>();
        foreach (var user in users.OrderBy(x => x.Email))
        {
            var roles = await userManager.GetRolesAsync(user);
            var isTargetSuperAdmin = roles.Contains(AppRoles.SuperAdmin);
            items.Add(new UserListItemViewModel
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? string.Empty,
                FullName = user.FullName,
                VendorCode = user.VendorCode,
                CompanyName = user.Company?.Name,
                Role = FormatPrimaryRole(roles),
                IsActive = user.IsActive,
                // SuperAdmin hesabını yalnızca SuperAdmin düzenler / şifre değiştirir.
                CanManage = actor.CanManageUsers && (actor.IsSuperAdmin || !isTargetSuperAdmin)
            });
        }

        ViewBag.IsSuperAdmin = actor.IsSuperAdmin;
        ViewBag.CanManageUsers = actor.CanManageUsers;
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var actor = await GetActorAsync();
        if (actor is null || !actor.CanManageUsers)
        {
            return Forbid();
        }

        await PopulateFormLookupsAsync(actor, null);
        return View(new CreateUserViewModel
        {
            CompanyId = actor.IsSuperAdmin ? null : actor.CompanyId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        var actor = await GetActorAsync();
        if (actor is null || !actor.CanManageUsers)
        {
            return Forbid();
        }

        NormalizeVendor(model);
        await PopulateFormLookupsAsync(actor, model.CompanyId);

        if (!IsAssignableRole(actor, model.Role))
        {
            ModelState.AddModelError(nameof(model.Role), "Geçersiz rol.");
        }

        ValidateVendorForRole(model.Role, model.VendorCode, nameof(model.VendorCode));
        ResolveCompanyForCreate(actor, model);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "Bu e-posta zaten kayıtlı.");
            return View(model);
        }

        await EnsureRolesExistAsync();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = string.IsNullOrWhiteSpace(model.FullName) ? null : model.FullName.Trim(),
            VendorCode = model.VendorCode,
            CompanyId = model.CompanyId,
            IsActive = model.IsActive
        };

        var createResult = await userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        await AssignRolesAsync(user, model.Role);
        TempData["Success"] = "Kullanıcı oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var actor = await GetActorAsync();
        if (actor is null || !actor.CanManageUsers)
        {
            return Forbid();
        }

        var user = await userManager.FindByIdAsync(id);
        if (user is null || !await CanManageUserAsync(actor, user))
        {
            return NotFound();
        }

        var roles = await userManager.GetRolesAsync(user);
        await PopulateFormLookupsAsync(actor, user.CompanyId);
        return View(new EditUserViewModel
        {
            Id = user.Id,
            Email = user.Email ?? user.UserName ?? string.Empty,
            FullName = user.FullName,
            VendorCode = user.VendorCode,
            CompanyId = user.CompanyId,
            Role = FormatPrimaryRole(roles),
            IsActive = user.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model)
    {
        var actor = await GetActorAsync();
        if (actor is null || !actor.CanManageUsers)
        {
            return Forbid();
        }

        NormalizeVendor(model);
        await PopulateFormLookupsAsync(actor, model.CompanyId);

        if (!IsAssignableRole(actor, model.Role))
        {
            ModelState.AddModelError(nameof(model.Role), "Geçersiz rol.");
        }

        ValidateVendorForRole(model.Role, model.VendorCode, nameof(model.VendorCode));

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByIdAsync(model.Id);
        if (user is null || !await CanManageUserAsync(actor, user))
        {
            return NotFound();
        }

        ResolveCompanyForEdit(actor, user, model);

        var currentUserId = actor.User.Id;
        var currentRoles = await userManager.GetRolesAsync(user);
        var wasAdmin = currentRoles.Contains(AppRoles.Admin) || currentRoles.Contains(AppRoles.SuperAdmin);
        var willBeAdmin = model.Role is AppRoles.Admin or AppRoles.SuperAdmin;

        if (user.Id == currentUserId && !model.IsActive)
        {
            ModelState.AddModelError(nameof(model.IsActive), "Kendi hesabınızı pasif yapamazsınız.");
            return View(model);
        }

        if (user.Id == currentUserId
            && wasAdmin
            && !willBeAdmin)
        {
            ModelState.AddModelError(nameof(model.Role), "Kendi yönetici rolünüzü kaldıramazsınız.");
            return View(model);
        }

        if (wasAdmin && !willBeAdmin && !await HasOtherCompanyAdminAsync(user))
        {
            ModelState.AddModelError(nameof(model.Role), "Şirkette en az bir Admin kalmalıdır.");
            return View(model);
        }

        if (wasAdmin && !model.IsActive && !await HasOtherCompanyAdminAsync(user))
        {
            ModelState.AddModelError(nameof(model.IsActive), "Son Admin hesabı pasif yapılamaz.");
            return View(model);
        }

        if (currentRoles.Contains(AppRoles.SuperAdmin)
            && model.Role != AppRoles.SuperAdmin
            && await CountSuperAdminsAsync() <= 1)
        {
            ModelState.AddModelError(nameof(model.Role), "Sistemde en az bir SuperAdmin kalmalıdır.");
            return View(model);
        }

        user.FullName = string.IsNullOrWhiteSpace(model.FullName) ? null : model.FullName.Trim();
        user.VendorCode = model.VendorCode;
        user.IsActive = model.IsActive;
        if (actor.IsSuperAdmin)
        {
            user.CompanyId = model.CompanyId;
        }

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        await EnsureRolesExistAsync();
        if (currentRoles.Count > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                foreach (var error in removeResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }
        }

        await AssignRolesAsync(user, model.Role);
        TempData["Success"] = "Kullanıcı güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string id)
    {
        var actor = await GetActorAsync();
        if (actor is null || !actor.CanManageUsers)
        {
            return Forbid();
        }

        var user = await userManager.FindByIdAsync(id);
        if (user is null || !await CanManageUserAsync(actor, user))
        {
            return NotFound();
        }

        return View(new ResetPasswordViewModel
        {
            Id = user.Id,
            Email = user.Email ?? user.UserName ?? string.Empty
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        var actor = await GetActorAsync();
        if (actor is null || !actor.CanManageUsers)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByIdAsync(model.Id);
        if (user is null || !await CanManageUserAsync(actor, user))
        {
            return NotFound();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        TempData["Success"] = "Şifre güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<ActorContext?> GetActorAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        return new ActorContext(
            user,
            AppRoles.IsSuperAdmin(User),
            AppRoles.CanManageUsers(User),
            user.CompanyId);
    }

    private async Task<List<ApplicationUser>> LoadVisibleUsersAsync(ActorContext actor)
    {
        if (actor.IsSuperAdmin)
        {
            return await userManager.Users.Include(u => u.Company).ToListAsync();
        }

        var byId = new Dictionary<string, ApplicationUser>(StringComparer.Ordinal);

        if (actor.CompanyId is int companyId)
        {
            var companyUsers = await userManager.Users
                .Include(u => u.Company)
                .Where(u => u.CompanyId == companyId)
                .ToListAsync();
            foreach (var u in companyUsers)
            {
                byId[u.Id] = u;
            }
        }

        // SuperAdmin hesapları şirket Admin/Operatör tarafından da görülür (salt okunur).
        var superAdmins = await userManager.GetUsersInRoleAsync(AppRoles.SuperAdmin);
        foreach (var super in superAdmins)
        {
            if (byId.ContainsKey(super.Id))
            {
                continue;
            }

            var full = await userManager.Users
                .Include(u => u.Company)
                .FirstOrDefaultAsync(u => u.Id == super.Id);
            if (full is not null)
            {
                byId[full.Id] = full;
            }
        }

        return byId.Values.ToList();
    }

    private async Task<bool> CanManageUserAsync(ActorContext actor, ApplicationUser target)
    {
        if (!actor.CanManageUsers)
        {
            return false;
        }

        if (actor.IsSuperAdmin)
        {
            return true;
        }

        var roles = await userManager.GetRolesAsync(target);
        if (roles.Contains(AppRoles.SuperAdmin))
        {
            return false;
        }

        return actor.CompanyId is not null && target.CompanyId == actor.CompanyId;
    }

    private void ResolveCompanyForCreate(ActorContext actor, CreateUserViewModel model)
    {
        if (!actor.IsSuperAdmin)
        {
            model.CompanyId = actor.CompanyId;
        }

        if (model.Role == AppRoles.SuperAdmin)
        {
            // Platform admin şirket zorunlu değil.
            return;
        }

        if (model.CompanyId is null or <= 0)
        {
            ModelState.AddModelError(nameof(model.CompanyId), "Şirket seçimi zorunludur.");
            return;
        }

        if (!db.Companies.Any(c => c.Id == model.CompanyId && c.IsActive))
        {
            ModelState.AddModelError(nameof(model.CompanyId), "Geçerli bir aktif şirket seçin.");
        }
    }

    private void ResolveCompanyForEdit(ActorContext actor, ApplicationUser user, EditUserViewModel model)
    {
        if (!actor.IsSuperAdmin)
        {
            model.CompanyId = user.CompanyId;
            return;
        }

        if (model.Role == AppRoles.SuperAdmin)
        {
            return;
        }

        if (model.CompanyId is null or <= 0)
        {
            ModelState.AddModelError(nameof(model.CompanyId), "Şirket seçimi zorunludur.");
        }
    }

    private async Task PopulateFormLookupsAsync(ActorContext actor, int? selectedCompanyId)
    {
        ViewBag.IsSuperAdmin = actor.IsSuperAdmin;
        ViewBag.Roles = actor.IsSuperAdmin ? AppRoles.All : AppRoles.CompanyAssignable;

        if (actor.IsSuperAdmin)
        {
            var companies = await db.Companies
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            ViewBag.Companies = new SelectList(companies, "Id", "Name", selectedCompanyId);
        }
        else if (actor.CompanyId is int cid)
        {
            var name = await db.Companies.AsNoTracking()
                .Where(c => c.Id == cid)
                .Select(c => c.Name)
                .FirstOrDefaultAsync() ?? "Şirketiniz";
            ViewBag.CompanyName = name;
        }
    }

    private static bool IsAssignableRole(ActorContext actor, string role)
    {
        var allowed = actor.IsSuperAdmin ? AppRoles.All : AppRoles.CompanyAssignable;
        return allowed.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    private async Task AssignRolesAsync(ApplicationUser user, string role)
    {
        // SuperAdmin aynı zamanda Admin yetkilerine sahip olsun (SAS vb. mevcut kontroller).
        if (role.Equals(AppRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase))
        {
            await userManager.AddToRoleAsync(user, AppRoles.SuperAdmin);
            await userManager.AddToRoleAsync(user, AppRoles.Admin);
            return;
        }

        await userManager.AddToRoleAsync(user, role);
    }

    private static string FormatPrimaryRole(IList<string> roles)
    {
        if (roles.Contains(AppRoles.SuperAdmin))
        {
            return AppRoles.SuperAdmin;
        }

        if (roles.Contains(AppRoles.Admin))
        {
            return AppRoles.Admin;
        }

        if (roles.Contains(AppRoles.Operator))
        {
            return AppRoles.Operator;
        }

        return roles.FirstOrDefault() ?? "—";
    }

    private void ValidateVendorForRole(string role, string? vendorCode, string fieldName)
    {
        if (role.Equals(AppRoles.Operator, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(vendorCode))
        {
            ModelState.AddModelError(fieldName, "Operatör için satıcı kodu zorunludur.");
        }
    }

    private static void NormalizeVendor(CreateUserViewModel model) =>
        model.VendorCode = NormalizeVendorCode(model.VendorCode);

    private static void NormalizeVendor(EditUserViewModel model) =>
        model.VendorCode = NormalizeVendorCode(model.VendorCode);

    private static string? NormalizeVendorCode(string? vendorCode)
    {
        if (string.IsNullOrWhiteSpace(vendorCode))
        {
            return null;
        }

        return vendorCode.Trim();
    }

    private async Task EnsureRolesExistAsync()
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private async Task<bool> HasOtherCompanyAdminAsync(ApplicationUser user)
    {
        if (user.CompanyId is null)
        {
            return await CountSuperAdminsAsync() > 1;
        }

        var admins = await userManager.GetUsersInRoleAsync(AppRoles.Admin);
        return admins.Any(a =>
            a.Id != user.Id
            && a.IsActive
            && a.CompanyId == user.CompanyId);
    }

    private async Task<int> CountSuperAdminsAsync()
    {
        var supers = await userManager.GetUsersInRoleAsync(AppRoles.SuperAdmin);
        return supers.Count(x => x.IsActive);
    }

    private sealed record ActorContext(
        ApplicationUser User,
        bool IsSuperAdmin,
        bool CanManageUsers,
        int? CompanyId);
}
