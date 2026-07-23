using Microsoft.AspNetCore.Identity;

namespace FasonBarkod.Core.Entities;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// SAP satıcı / cari kodu (LIFNR). Operatör yalnızca bu koda ait SAS'ları görür.
    /// Admin için boş bırakılabilir (tüm cariler).
    /// </summary>
    public string? VendorCode { get; set; }
}
