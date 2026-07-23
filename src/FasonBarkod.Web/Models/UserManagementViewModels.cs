using System.ComponentModel.DataAnnotations;

namespace FasonBarkod.Web.Models;

public class UserListItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? VendorCode { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
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
