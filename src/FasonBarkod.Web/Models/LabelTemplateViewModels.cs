using System.ComponentModel.DataAnnotations;
using FasonBarkod.Core.Enums;

namespace FasonBarkod.Web.Models;

public class LabelTemplateListItemViewModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? BrandCode { get; set; }

    public LabelType LabelType { get; set; }

    public string LabelTypeDisplay => LabelType == LabelType.KoliUstu ? "Koli Üstü" : "Koli İçi";

    public bool IsActive { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public class LabelTemplateEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Tasarım adı zorunludur.")]
    [Display(Name = "Tasarım Adı")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Marka / Malzeme Grubu")]
    public string? BrandCode { get; set; }

    [Required]
    [Display(Name = "Etiket Tipi")]
    public LabelType LabelType { get; set; }

    [Required(ErrorMessage = "Şablon içeriği zorunludur.")]
    [Display(Name = "Şablon İçeriği (SATO komutları)")]
    public string Content { get; set; } = string.Empty;

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;
}
