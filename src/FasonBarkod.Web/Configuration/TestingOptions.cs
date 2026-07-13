namespace FasonBarkod.Web.Configuration;

/// <summary>
/// Geliştirme ortamında SAP yerine mock barkod ile test.
/// </summary>
public class TestingOptions
{
    public const string SectionName = "Testing";

    public bool AllowMockBarcodes { get; set; }
}
