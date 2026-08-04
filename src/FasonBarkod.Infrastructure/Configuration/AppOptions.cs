namespace FasonBarkod.Infrastructure.Configuration;

public class SapOptions
{
    public const string SectionName = "Sap";

    public bool Enabled { get; set; }

    public bool UseMockFallback { get; set; } = true;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string AppServerHost { get; set; } = string.Empty;

    public string SystemNumber { get; set; } = "00";

    public string Client { get; set; } = "100";

    public string Language { get; set; } = "TR";

    public int Port { get; set; } = 3300;

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(AppServerHost) &&
        !string.IsNullOrWhiteSpace(User) &&
        !string.IsNullOrWhiteSpace(Password);
}

/// <summary>
/// Eski Doqu ASMX (barcode.doqu.com.tr) — seri listesi için GetSAPSASBarcodeSerials.
/// UserName/Password boşsa Sap.User / Sap.Password kullanılır.
/// </summary>
public class SapBarcodeSoapOptions
{
    public const string SectionName = "SapBarcodeSoap";

    public bool Enabled { get; set; }

    public string Url { get; set; } = "https://barcode.doqu.com.tr/service.asmx";

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>SAS seri listesi Durum bayrağı (eski kod: '*').</summary>
    public string SasDurum { get; set; } = "*";
}

public class LdapOptions
{
    public const string SectionName = "Ldap";

    public bool Enabled { get; set; }

    public string Server { get; set; } = string.Empty;

    public string BarcodePrintGroup { get; set; } = "Barcode Basma";

    public string BarcodeReprintGroup { get; set; } = "Barcode Basma_Tekrar";
}

public class PrinterOptions
{
    public const string SectionName = "Printer";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// true: etiket sunucuda üretilir, basım operatör PC'sindeki QZ Tray ile yapılır.
    /// false: sunucu Windows spooler'a raw gönderir (eski davranış).
    /// </summary>
    public bool UseQzTray { get; set; } = true;

    /// <summary>
    /// Geliştirme ortamında fiziksel yazıcı yerine .prn dosyası üretir.
    /// </summary>
    public bool SimulatePrint { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Boş bırakılırsa Windows varsayılan yazıcısı veya panelden seçilen yazıcı kullanılır.
    /// </summary>

    public string TemplateFolder { get; set; } = "Templates";

    public string SimulateOutputFolder { get; set; } = "PrintOutput";

    /// <summary>
    /// Şablonda A1 yoksa yedek medya yüksekliği (mm). Koli üstü/içi şablonlarında A1 gömülü;
    /// bu ayar yalnızca A1'siz özel şablon / test baskısı için kullanılır.
    /// </summary>
    public int LabelHeightMm { get; set; } = 110;

    /// <summary>
    /// Şablonda A1 yoksa yedek medya genişliği (mm).
    /// </summary>
    public int LabelWidthMm { get; set; } = 80;

    public int Dpi { get; set; } = 203;

    /// <summary>
    /// QZ Tray digital-certificate.txt yolu (ContentRoot göreli veya mutlak).
    /// QzCertificatePem doluysa dosya yerine bu kullanılır.
    /// </summary>
    public string QzCertificatePath { get; set; } = "QzSigning/digital-certificate.txt";

    /// <summary>
    /// Sertifika PEM metni (appsettings'e gömülebilir). Public key — istemciye gider.
    /// </summary>
    public string QzCertificatePem { get; set; } = string.Empty;

    /// <summary>
    /// QZ Tray private-key.pem yolu (sunucuda kalır, wwwroot'a koyulmaz).
    /// </summary>
    public string QzPrivateKeyPath { get; set; } = "QzSigning/private-key.pem";
}
