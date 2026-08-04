using Microsoft.AspNetCore.Identity;

namespace FasonBarkod.Core.Entities;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// SAP satıcı / cari kodu (LIFNR). Operatör yalnızca bu koda ait SAS'ları görür.
    /// Admin için boş bırakılabilir (şirket içi tüm cariler).
    /// </summary>
    public string? VendorCode { get; set; }

    /// <summary>
    /// Kullanıcının şirketi. SuperAdmin için boş olabilir (platform düzeyi).
    /// Admin/Operator için zorunlu.
    /// </summary>
    public int? CompanyId { get; set; }

    public Company? Company { get; set; }
}
