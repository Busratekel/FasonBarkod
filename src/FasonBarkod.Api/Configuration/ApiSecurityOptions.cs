namespace FasonBarkod.Api.Configuration;

public class ApiSecurityOptions
{
    public const string SectionName = "ApiSecurity";

    public string ApiKey { get; set; } = string.Empty;
}
