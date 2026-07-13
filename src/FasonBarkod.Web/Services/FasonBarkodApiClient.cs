using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;
using FasonBarkod.Web.Configuration;
using Microsoft.Extensions.Options;

namespace FasonBarkod.Web.Services;

public interface IFasonBarkodApiClient
{
    Task<IReadOnlyList<SasLineDto>> ListSasAsync(
        string purchaseOrderNo,
        string? vendorCode = null,
        CancellationToken cancellationToken = default);

    Task<SapBarcodeResult> CreateSasBarcodeAsync(
        CreateSasBarcodeRequest request,
        CancellationToken cancellationToken = default);

    Task<SapBarcodeResult> ReprintSasBarcodeAsync(
        ReprintSasBarcodeRequest request,
        CancellationToken cancellationToken = default);
}

public record CreateSasBarcodeRequest(
    string PurchaseOrderNo,
    string LineNo,
    string MaterialNumber,
    decimal PackageQuantity,
    decimal PrintQuantity,
    LabelType LabelType,
    string? PrintedBy,
    string? VendorCode = null);

public record ReprintSasBarcodeRequest(
    string PurchaseOrderNo,
    string LineNo,
    decimal PackageQuantity,
    LabelType LabelType,
    string SerialNumber,
    string? PrintedBy,
    string? VendorCode = null);

public class FasonBarkodApiClient(
    HttpClient httpClient,
    ILogger<FasonBarkodApiClient> logger) : IFasonBarkodApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<SasLineDto>> ListSasAsync(
        string purchaseOrderNo,
        string? vendorCode = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/sas/{Uri.EscapeDataString(purchaseOrderNo)}/lines";
        if (!string.IsNullOrWhiteSpace(vendorCode))
        {
            url += $"?vendorCode={Uri.EscapeDataString(vendorCode)}";
        }

        var response = await httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var lines = await response.Content.ReadFromJsonAsync<List<SasLineDto>>(JsonOptions, cancellationToken);
        return lines ?? [];
    }

    public async Task<SapBarcodeResult> CreateSasBarcodeAsync(
        CreateSasBarcodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/sas/barcodes", request, cancellationToken);
        return await ReadBarcodeResultAsync(response, "SAS barkod", cancellationToken);
    }

    public async Task<SapBarcodeResult> ReprintSasBarcodeAsync(
        ReprintSasBarcodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/sas/barcodes/reprint", request, cancellationToken);
        return await ReadBarcodeResultAsync(response, "SAS tekrar basım", cancellationToken);
    }

    private async Task<SapBarcodeResult> ReadBarcodeResultAsync(
        HttpResponseMessage response,
        string operationName,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("{Operation} API hatası: {Status} {Body}", operationName, response.StatusCode, body);

            try
            {
                var error = JsonSerializer.Deserialize<ApiErrorResponse>(body, JsonOptions);
                return new SapBarcodeResult(false, error?.Error ?? body, [], null);
            }
            catch
            {
                return new SapBarcodeResult(false, body, [], null);
            }
        }

        var result = await response.Content.ReadFromJsonAsync<SapBarcodeResult>(JsonOptions, cancellationToken);
        return result ?? new SapBarcodeResult(false, "API boş yanıt döndü.", [], null);
    }

    private record ApiErrorResponse(string? Error);
}

public static class ApiClientRegistration
{
    public static IServiceCollection AddFasonBarkodApiClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiClientOptions>(configuration.GetSection(ApiClientOptions.SectionName));

        services.AddHttpClient<IFasonBarkodApiClient, FasonBarkodApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ApiClientOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
