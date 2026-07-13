namespace FasonBarkod.Web.Configuration;

public class ApiClientOptions
{
    public const string SectionName = "ApiClient";

    public string BaseUrl { get; set; } = "http://localhost:5135";

    public string ApiKey { get; set; } = string.Empty;
}
