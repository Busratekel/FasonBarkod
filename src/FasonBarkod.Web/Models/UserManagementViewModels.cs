using System.ComponentModel.DataAnnotations;

namespace FasonBarkod.Web.Models;

public class UserListItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? VendorCode { get; set; }
    public string? CompanyName { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>False ise satır salt okunur (ör. SuperAdmin — yalnızca SuperAdmin düzenler).</summary>
    public bool CanManage { get; set; } = true;
}

public class CreateUserViewModel
{
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta girin.")]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Ad Soyad")]
    [StringLength(100)]
    public string? FullName { get; set; }

    [Display(Name = "Şirket")]
    public int? CompanyId { get; set; }

    [Display(Name = "Satıcı Kodu")]
    [StringLength(20, ErrorMessage = "Satıcı kodu en fazla {1} karakter olabilir.")]
    public string? VendorCode { get; set; }

    [Required(ErrorMessage = "Rol zorunludur.")]
    [Display(Name = "Rol")]
    public string Role { get; set; } = "Operator";

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Şifre en az {2} karakter olmalıdır.")]
    [Display(Name = "Şifre")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Şifre tekrar")]
    [Compare(nameof(Password), ErrorMessage = "Şifreler eşleşmiyor.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;
}

public class EditUserViewModel
{
    public string Id { get; set; } = string.Empty;

    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Ad Soyad")]
    [StringLength(100)]
    public string? FullName { get; set; }

    [Display(Name = "Şirket")]
    public int? CompanyId { get; set; }

    [Display(Name = "Satıcı Kodu")]
    [StringLength(20, ErrorMessage = "Satıcı kodu en fazla {1} karakter olabilir.")]
    public string? VendorCode { get; set; }

    [Required(ErrorMessage = "Rol zorunludur.")]
    [Display(Name = "Rol")]
    public string Role { get; set; } = "Operator";

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;
}

public class ResetPasswordViewModel
{
    public string Id { get; set; } = string.Empty;

    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Şifre en az {2} karakter olmalıdır.")]
    [Display(Name = "Yeni şifre")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Şifre tekrar")]
    [Compare(nameof(Password), ErrorMessage = "Şifreler eşleşmiyor.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class CompanyListItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int UserCount { get; set; }
}

public class CompanyEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Şirket adı zorunludur.")]
    [StringLength(200)]
    [Display(Name = "Şirket adı")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şirket kodu zorunludur.")]
    [StringLength(50)]
    [Display(Name = "Kod")]
    [RegularExpression(@"^[A-Za-z0-9_-]+$", ErrorMessage = "Kod yalnızca harf, rakam, _ ve - içerebilir.")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;
}
