namespace FasonBarkod.Core.Entities;

/// <summary>
/// Uygulama geneli yazıcı ayarı (tek kayıt).
/// </summary>
public class AppPrintSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>
    /// Boş ise Windows varsayılan yazıcısı kullanılır.
    /// </summary>
    public string? PrinterName { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? UpdatedByUserId { get; set; }
}
