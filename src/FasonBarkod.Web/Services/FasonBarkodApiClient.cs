using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;
using FasonBarkod.Web.Configuration;
using Microsoft.Extensions.Options;

namespace FasonBarkod.Web.Services;

public record SasListResponse(
    IReadOnlyList<SasLineDto> Lines,
    string? Error = null,
    string? Hint = null,
    string? Diagnostic = null,
    string? SapMessage = null);

public interface IFasonBarkodApiClient
{
    Task<SasListResponse> ListSasAsync(
        string purchaseOrderNo,
        string? vendorCode = null,
        CancellationToken cancellationToken = default);

    Task<SapBarcodeResult> CreateSasBarcodeAsync(
        CreateSasBarcodeRequest request,
        CancellationToken cancellationToken = default);

    Task<SapBarcodeResult> ReprintSasBarcodeAsync(
        ReprintSasBarcodeRequest request,
        CancellationToken cancellationToken = default);

    Task<SasSerialListResult> ListSasSerialsAsync(
        string purchaseOrderNo,
        string lineNo,
        bool boxInside = false,
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

    public async Task<SasListResponse> ListSasAsync(
        string purchaseOrderNo,
        string? vendorCode = null,
        CancellationToken cancellationToken = default)
    {
        string url;
        if (string.IsNullOrWhiteSpace(purchaseOrderNo))
        {
            url = "api/sas/lines";
            if (!string.IsNullOrWhiteSpace(vendorCode))
            {
                url += $"?vendorCode={Uri.EscapeDataString(vendorCode)}";
            }
        }
        else
        {
            url = $"api/sas/{Uri.EscapeDataString(purchaseOrderNo)}/lines";
            if (!string.IsNullOrWhiteSpace(vendorCode))
            {
                url += $"?vendorCode={Uri.EscapeDataString(vendorCode)}";
            }
        }

        logger.LogDebug("SAS listesi: {Url}", url);

        try
        {
            var response = await httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var notFound = TryDeserializeNotFound(body);
                logger.LogWarning(
                    "SAS bulunamadı: {OrderNo}, cari={Vendor}, API={Error}",
                    string.IsNullOrWhiteSpace(purchaseOrderNo) ? "(boş)" : purchaseOrderNo,
                    vendorCode ?? "(boş)",
                    notFound?.Error ?? body);

                return new SasListResponse([], notFound?.Error, notFound?.Hint, notFound?.Diagnostic, notFound?.SapMessage);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError(
                    "SAS API hata: {Status} {OrderNo} body={Body}",
                    (int)response.StatusCode,
                    string.IsNullOrWhiteSpace(purchaseOrderNo) ? "(boş)" : purchaseOrderNo,
                    body.Length > 300 ? body[..300] : body);
                return new SasListResponse(
                    [],
                    $"API yanıt vermedi ({(int)response.StatusCode}). API uygulamasını kontrol edin.");
            }

            var lines = await response.Content.ReadFromJsonAsync<List<SasLineDto>>(JsonOptions, cancellationToken);
            return new SasListResponse(lines ?? []);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or SocketException)
        {
            logger.LogError(ex, "SAS API çağrısı başarısız: {Url}", url);
            return new SasListResponse(
                [],
                "API'ye bağlanılamadı (localhost:5135 kapalı olabilir). API projesini de çalıştırın.");
        }
    }

    private SasNotFoundResponse? TryDeserializeNotFound(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<SasNotFoundResponse>(body, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<SapBarcodeResult> CreateSasBarcodeAsync(
        CreateSasBarcodeRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("api/sas/barcodes", request, cancellationToken);
            return await ReadBarcodeResultAsync(response, "SAS barkod", cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or SocketException)
        {
            logger.LogError(ex, "SAS barkod API bağlantı hatası");
            return new SapBarcodeResult(false, "API'ye bağlanılamadı. API projesini çalıştırın.", [], null);
        }
    }

    public async Task<SapBarcodeResult> ReprintSasBarcodeAsync(
        ReprintSasBarcodeRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("api/sas/barcodes/reprint", request, cancellationToken);
            return await ReadBarcodeResultAsync(response, "SAS tekrar basım", cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or SocketException)
        {
            logger.LogError(ex, "SAS tekrar basım API bağlantı hatası");
            return new SapBarcodeResult(false, "API'ye bağlanılamadı. API projesini çalıştırın.", [], null);
        }
    }

    public async Task<SasSerialListResult> ListSasSerialsAsync(
        string purchaseOrderNo,
        string lineNo,
        bool boxInside = false,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"api/sas/{Uri.EscapeDataString(purchaseOrderNo)}/serials" +
            $"?lineNo={Uri.EscapeDataString(lineNo)}" +
            $"&boxInside={boxInside.ToString().ToLowerInvariant()}";

        try
        {
            var response = await httpClient.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("SAS seri listesi API hatası: {Status} {Body}", response.StatusCode, body);
                try
                {
                    var error = JsonSerializer.Deserialize<ApiErrorResponse>(body, JsonOptions);
                    return new SasSerialListResult(false, error?.Error ?? body, []);
                }
                catch
                {
                    return new SasSerialListResult(false, body, []);
                }
            }

            var result = JsonSerializer.Deserialize<SasSerialListResult>(body, JsonOptions);
            return result ?? new SasSerialListResult(false, "API boş yanıt döndü.", []);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or SocketException)
        {
            logger.LogError(ex, "SAS seri listesi API bağlantı hatası");
            return new SasSerialListResult(false, "API'ye bağlanılamadı. API projesini çalıştırın.", []);
        }
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

    private record SasNotFoundResponse(string? Error, string? Hint, string? Diagnostic, string? SapMessage);
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
            client.Timeout = TimeSpan.FromSeconds(90);
        });

        return services;
    }
}
