using FasonBarkod.Core.Entities;
using FasonBarkod.Core.Enums;

namespace FasonBarkod.Infrastructure.Services;

public interface ILabelTemplateService
{
    Task<string> ResolveTemplateContentAsync(
        LabelType labelType,
        string? brandCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LabelTemplate>> ListAsync(CancellationToken cancellationToken = default);

    Task<LabelTemplate?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<(bool Success, string? Error)> SaveAsync(LabelTemplate template, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task EnsureSeededFromFilesAsync(string templateRoot, CancellationToken cancellationToken = default);
}

public static class LabelTemplatePlaceholders
{
    public static IReadOnlyList<(string Token, string Description)> All { get; } =
    [
        ("@MAKTX", "Malzeme açıklaması"),
        ("@MATNR", "Malzeme kodu"),
        ("@ZPAKET", "Paket miktarı"),
        ("@ZYIL", "Yıl"),
        ("@ZHAFTA", "Hafta"),
        ("@BARKOD1", "Barkod 1"),
        ("@BARKOD2", "Barkod 2"),
        ("@BARKOD3", "Barkod 3"),
        ("@BARKOD4", "Barkod 4"),
        ("@BRAND", "Marka / malzeme grubu kodu"),
        ("@BEZEI", "Marka açıklaması"),
        ("@SERNR", "Seri numarası"),
        ("@EBELN", "SAS numarası"),
        ("@EBELP", "SAS kalem no"),
        ("@KUNNR", "Cari / satıcı kodu"),
        ("@LIFNR", "Cari / satıcı kodu"),
        ("@BARKOD3T", "Barkod 3 (koli üstü)"),
        ("@BARKOD4T", "Barkod 4 (koli üstü)"),
        ("@QR", "QR kod içeriği (Barkod1)"),
        ("@Counter", "Sıra no"),
        ("@C", "Sıra no (kısa)"),
        ("@T", "Saat (HH:mm)"),
        ("@ZBRKD_YIL", "Yıl"),
        ("@ZBRKD_SAAT", "Saat (HH:mm)")
    ];
}
