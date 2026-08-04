namespace FasonBarkod.Core.Entities;

/// <summary>
/// Çok şirketli (tenant) izolasyon. Kullanıcılar yalnızca kendi şirketlerini yönetir.
/// </summary>
public class Company
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Kısa benzersiz kod (örn. BOYDAK, DEFAULT).</summary>
    public string Code { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}
