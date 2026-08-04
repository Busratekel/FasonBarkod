using System.Security.Claims;

namespace FasonBarkod.Core.Security;

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Operator = "Operator";

    public static readonly string[] CompanyAssignable = [Admin, Operator];
    public static readonly string[] All = [SuperAdmin, Admin, Operator];

    public static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(SuperAdmin);

    /// <summary>Şirket kullanıcısı yönetimi: SuperAdmin veya şirket Admin.</summary>
    public static bool CanManageUsers(ClaimsPrincipal user) =>
        user.IsInRole(SuperAdmin) || user.IsInRole(Admin);
}
