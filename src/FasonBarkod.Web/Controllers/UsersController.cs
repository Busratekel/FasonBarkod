using FasonBarkod.Core.Entities;
using FasonBarkod.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FasonBarkod.Web.Controllers;

[Authorize(Roles = "Admin")]
public class UsersController(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager) : Controller
{
    public static readonly string[] AllowedRoles = ["Admin", "Operator"];

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var users = await userManager.Users
            .OrderBy(x => x.Email)
            .ToListAsync();

        var items = new List<UserListItemViewModel>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            items.Add(new UserListItemViewModel
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? string.Empty,
                FullName = user.FullName,
                VendorCode = user.VendorCode,
                Role = roles.FirstOrDefault() ?? "—",
                IsActive = user.IsActive
            });
        }

        return View(items);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewBag.Roles = AllowedRoles;
        return View(new CreateUserViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        ViewBag.Roles = AllowedRoles;
        NormalizeVendor(model);

        if (!AllowedRoles.Contains(model.Role, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.Role), "Geçersiz rol.");
        }

        ValidateVendorForRole(model.Role, model.VendorCode, nameof(model.VendorCode));

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

        await userManager.AddToRoleAsync(user, model.Role);
        TempData["Success"] = "Kullanıcı oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var roles = await userManager.GetRolesAsync(user);
        ViewBag.Roles = AllowedRoles;
        return View(new EditUserViewModel
        {
            Id = user.Id,
            Email = user.Email ?? user.UserName ?? string.Empty,
            FullName = user.FullName,
            VendorCode = user.VendorCode,
            Role = roles.FirstOrDefault() ?? "Operator",
            IsActive = user.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model)
    {
        ViewBag.Roles = AllowedRoles;
        NormalizeVendor(model);

        if (!AllowedRoles.Contains(model.Role, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.Role), "Geçersiz rol.");
        }

        ValidateVendorForRole(model.Role, model.VendorCode, nameof(model.VendorCode));

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByIdAsync(model.Id);
        if (user is null)
        {
            return NotFound();
        }

        var currentUserId = userManager.GetUserId(User);
        var currentRoles = await userManager.GetRolesAsync(user);
        var wasAdmin = currentRoles.Contains("Admin");

        if (user.Id == currentUserId && !model.IsActive)
        {
            ModelState.AddModelError(nameof(model.IsActive), "Kendi hesabınızı pasif yapamazsınız.");
            return View(model);
        }

        if (user.Id == currentUserId
            && wasAdmin
            && !model.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.Role), "Kendi Admin rolünüzü kaldıramazsınız.");
            return View(model);
        }

        if (wasAdmin
            && !model.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            && await CountAdminsAsync() <= 1)
        {
            ModelState.AddModelError(nameof(model.Role), "Sistemde en az bir Admin kalmalıdır.");
            return View(model);
        }

        if (wasAdmin && !model.IsActive && await CountAdminsAsync() <= 1)
        {
            ModelState.AddModelError(nameof(model.IsActive), "Son Admin hesabı pasif yapılamaz.");
            return View(model);
        }

        user.FullName = string.IsNullOrWhiteSpace(model.FullName) ? null : model.FullName.Trim();
        user.VendorCode = model.VendorCode;
        user.IsActive = model.IsActive;

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

        await userManager.AddToRoleAsync(user, model.Role);
        TempData["Success"] = "Kullanıcı güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
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
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByIdAsync(model.Id);
        if (user is null)
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

    private void ValidateVendorForRole(string role, string? vendorCode, string fieldName)
    {
        if (role.Equals("Operator", StringComparison.OrdinalIgnoreCase)
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
        foreach (var role in AllowedRoles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private async Task<int> CountAdminsAsync()
    {
        var admins = await userManager.GetUsersInRoleAsync("Admin");
        return admins.Count(x => x.IsActive);
    }
}
