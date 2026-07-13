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
    /// Geliştirme ortamında fiziksel yazıcı yerine .prn dosyası üretir.
    /// </summary>
    public bool SimulatePrint { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Boş bırakılırsa Windows varsayılan yazıcısı veya panelden seçilen yazıcı kullanılır.
    /// </summary>

    public string TemplateFolder { get; set; } = "Templates";

    public string SimulateOutputFolder { get; set; } = "PrintOutput";
}
