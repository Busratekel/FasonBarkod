using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using FasonBarkod.Core.Sap;
using FasonBarkod.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FasonBarkod.Infrastructure.Sap.Soap;

/// <summary>
/// barcode.doqu.com.tr ASMX — GetSAPSASBarcodeSerials (ZMMIST14000 listesi).
/// </summary>
public interface ISapBarcodeSoapClient
{
    bool IsConfigured { get; }

    Task<SasSerialListResult> GetSasSerialsAsync(
        string purchaseOrderNo,
        string lineNo,
        bool boxInside,
        CancellationToken cancellationToken = default);
}

public class SapBarcodeSoapClient(
    IOptions<SapBarcodeSoapOptions> soapOptions,
    IOptions<SapOptions> sapOptions,
    ILogger<SapBarcodeSoapClient> logger) : ISapBarcodeSoapClient
{
    private readonly SapBarcodeSoapOptions _soap = soapOptions.Value;
    private readonly SapOptions _sap = sapOptions.Value;

    public bool IsConfigured
    {
        get
        {
            if (!_soap.Enabled || string.IsNullOrWhiteSpace(_soap.Url))
            {
                return false;
            }

            var (user, password) = ResolveCredentials();
            return !string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(password);
        }
    }

    public async Task<SasSerialListResult> GetSasSerialsAsync(
        string purchaseOrderNo,
        string lineNo,
        bool boxInside,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new SasSerialListResult(
                false,
                "SAP barkod SOAP servisi yapılandırılmamış (SapBarcodeSoap).",
                []);
        }

        var (user, password) = ResolveCredentials();
        if (!int.TryParse(lineNo.Trim(), out var ebelp))
        {
            return new SasSerialListResult(false, $"Geçersiz kalem no: {lineNo}", []);
        }

        // RFC ile aynı: EBELN CHAR10 (baştaki sıfırlar).
        var ebeln = SapAccountNumber.Pad10(purchaseOrderNo);
        // SAS: Durum='*' ; koli üstü KoliIci=' ' ; koli içi KoliIci='X'
        // Char alanlar SOAP XML'de boşluk kaybolunca "Input string was not in a correct format" verir.
        // Bu yüzden WSDL'deki HTTP POST (form-urlencoded) kullanılır.
        var durum = string.IsNullOrWhiteSpace(_soap.SasDurum) ? "*" : _soap.SasDurum.Trim()[0].ToString();
        var koliIci = boxInside ? "X" : " ";

        var baseUrl = _soap.Url.TrimEnd('/');
        var url = $"{baseUrl}/GetSAPSASBarcodeSerials";

        logger.LogInformation(
            "GetSAPSASBarcodeSerials: EBELN={Order} EBELP={Line} Durum={Durum} KoliIci='{KoliIci}' Url={Url}",
            ebeln,
            ebelp,
            durum,
            koliIci,
            baseUrl);

        var parsed = await PostSerialsFormAsync(
            url, user, password, ebeln, ebelp, durum, koliIci, cancellationToken);

        // Durum='*' boşsa boşluk ile bir kez daha dene (bazı Doqu kurulumları).
        if (parsed.Success && parsed.Serials.Count == 0 && durum == "*")
        {
            logger.LogInformation("GetSAPSASBarcodeSerials Durum='*' boş; Durum=' ' deneniyor.");
            var retry = await PostSerialsFormAsync(
                url, user, password, ebeln, ebelp, " ", koliIci, cancellationToken);
            if (retry.Success && retry.Serials.Count > 0)
            {
                return retry;
            }
        }

        return parsed;
    }

    private async Task<SasSerialListResult> PostSerialsFormAsync(
        string url,
        string user,
        string password,
        string ebeln,
        int ebelp,
        string durum,
        string koliIci,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = user,
            ["Password"] = password,
            ["Ebeln"] = ebeln,
            ["Ebelp"] = ebelp.ToString(CultureInfo.InvariantCulture),
            ["Durum"] = durum,
            ["KoliIci"] = koliIci
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "GetSAPSASBarcodeSerials form HTTP {Status}, SOAP denenecek. Body={Body}",
                (int)response.StatusCode,
                body.Length > 300 ? body[..300] : body);

            var soapResult = await TrySoap11Async(
                httpClient,
                url.Contains("/GetSAPSASBarcodeSerials", StringComparison.OrdinalIgnoreCase)
                    ? url[..url.LastIndexOf("/GetSAPSASBarcodeSerials", StringComparison.OrdinalIgnoreCase)]
                    : url.TrimEnd('/'),
                user,
                password,
                ebeln,
                ebelp,
                durum,
                koliIci == "X",
                cancellationToken);
            if (soapResult is not null)
            {
                return soapResult;
            }

            return new SasSerialListResult(
                false,
                $"GetSAPSASBarcodeSerials hata ({(int)response.StatusCode}).",
                []);
        }

        var parsed = ParseSerialsXml(body);
        logger.LogInformation(
            "GetSAPSASBarcodeSerials sonuç: Success={Success} Count={Count} Error={Error}",
            parsed.Success,
            parsed.Serials.Count,
            parsed.ErrorMessage ?? "(yok)");
        return parsed;
    }

    private async Task<SasSerialListResult?> TrySoap11Async(
        HttpClient httpClient,
        string serviceUrl,
        string user,
        string password,
        string ebeln,
        int ebelp,
        string durum,
        bool boxInside,
        CancellationToken cancellationToken)
    {
        // KoliIci boşluk: &#32; — ASMX char deserialize için kritik.
        var koliIciXml = boxInside ? "X" : "&#32;";
        var soapBody =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
            "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
            "xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
            "<soap:Body>" +
            "<GetSAPSASBarcodeSerials xmlns=\"http://tempuri.org/\">" +
            $"<UserName>{System.Security.SecurityElement.Escape(user)}</UserName>" +
            $"<Password>{System.Security.SecurityElement.Escape(password)}</Password>" +
            $"<Ebeln>{System.Security.SecurityElement.Escape(ebeln)}</Ebeln>" +
            $"<Ebelp>{ebelp.ToString(CultureInfo.InvariantCulture)}</Ebelp>" +
            $"<Durum>{System.Security.SecurityElement.Escape(durum)}</Durum>" +
            $"<KoliIci>{koliIciXml}</KoliIci>" +
            "</GetSAPSASBarcodeSerials>" +
            "</soap:Body></soap:Envelope>";

        using var content = new StringContent(soapBody, Encoding.UTF8, "text/xml");
        using var request = new HttpRequestMessage(HttpMethod.Post, serviceUrl) { Content = content };
        request.Headers.TryAddWithoutValidation("SOAPAction", "\"http://tempuri.org/GetSAPSASBarcodeSerials\"");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "GetSAPSASBarcodeSerials SOAP HTTP {Status}: {Body}",
                (int)response.StatusCode,
                body.Length > 300 ? body[..300] : body);
            return null;
        }

        return ParseSerialsXml(body);
    }

    private (string User, string Password) ResolveCredentials()
    {
        var user = !string.IsNullOrWhiteSpace(_soap.UserName) ? _soap.UserName : _sap.User;
        var password = !string.IsNullOrWhiteSpace(_soap.Password) ? _soap.Password : _sap.Password;
        return (user ?? string.Empty, password ?? string.Empty);
    }

    private static SasSerialListResult ParseSerialsXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null)
            {
                return new SasSerialListResult(false, "SOAP boş XML.", []);
            }

            var resultNode = root.Name.LocalName.Equals("ZMM_SAS_SERIALS_DATA", StringComparison.OrdinalIgnoreCase)
                ? root
                : doc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName.Equals("GetSAPSASBarcodeSerialsResult", StringComparison.OrdinalIgnoreCase)
                    || e.Name.LocalName.Equals("ZMM_SAS_SERIALS_DATA", StringComparison.OrdinalIgnoreCase))
                  ?? root;

            var error = resultNode.Elements().FirstOrDefault(e =>
                    e.Name.LocalName.Equals("errorMessage", StringComparison.OrdinalIgnoreCase))
                ?.Value?.Trim();

            var serialList = resultNode.Elements().FirstOrDefault(e =>
                e.Name.LocalName.Equals("serialList", StringComparison.OrdinalIgnoreCase));

            var items = new List<SasSerialDto>();
            if (serialList is not null)
            {
                foreach (var row in serialList.Elements().Where(e =>
                             e.Name.LocalName.Equals("ZMMIST14000", StringComparison.OrdinalIgnoreCase)))
                {
                    var sernr = Child(row, "SERNR");
                    if (string.IsNullOrWhiteSpace(sernr))
                    {
                        continue;
                    }

                    _ = int.TryParse(Child(row, "EBELP"), out var ebelp);
                    _ = decimal.TryParse(
                        Child(row, "MIKTAR"),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var miktar);

                    items.Add(new SasSerialDto(
                        Child(row, "EBELN"),
                        ebelp.ToString(CultureInfo.InvariantCulture),
                        sernr.Trim(),
                        miktar,
                        Child(row, "MEINS"),
                        Child(row, "DURUM"),
                        Child(row, "KOLIB")));
                }
            }

            if (!string.IsNullOrWhiteSpace(error) && items.Count == 0)
            {
                // Doqu: boş sonuç da errorMessage ile gelir — UI'da hata değil boş grid.
                if (IsEmptyResultMessage(error))
                {
                    return new SasSerialListResult(true, null, []);
                }

                var msg = error.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
                    ? "Doqu barkod SOAP kimlik doğrulaması başarısız (Authentication Failed). " +
                      "SapBarcodeSoap:UserName / Password (Doqu servis hesabı) ayarlayın; SAP RFC kullanıcısı yetmeyebilir."
                    : error;
                return new SasSerialListResult(false, msg, []);
            }

            return new SasSerialListResult(true, string.IsNullOrWhiteSpace(error) ? null : error, items);
        }
        catch (Exception ex)
        {
            return new SasSerialListResult(false, $"SOAP XML parse hatası: {ex.Message}", []);
        }
    }

    private static bool IsEmptyResultMessage(string error) =>
        error.Contains("kayıt bulunamad", StringComparison.OrdinalIgnoreCase)
        || error.Contains("kayit bulunamad", StringComparison.OrdinalIgnoreCase)
        || error.Contains("no record", StringComparison.OrdinalIgnoreCase)
        || error.Contains("not found", StringComparison.OrdinalIgnoreCase);

    private static string Child(XElement row, string localName) =>
        row.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value?.Trim() ?? string.Empty;
}
