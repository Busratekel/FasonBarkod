using FasonBarkod.Core.Enums;

namespace FasonBarkod.Core.Entities;

/// <summary>
/// SATO yazıcı komutları (SBPL) ile etiket tasarımı. Admin panelinden düzenlenir.
/// </summary>
public class LabelTemplate
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Malzeme grubu / marka kodu. Boş ise ilgili etiket tipi için varsayılan şablondur.
    /// </summary>
    public string? BrandCode { get; set; }

    public LabelType LabelType { get; set; }

    /// <summary>
    /// SATO SBPL şablon metni. @MAKTX, @BARKOD1 gibi alanlar basımda doldurulur.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? UpdatedByUserId { get; set; }
}
