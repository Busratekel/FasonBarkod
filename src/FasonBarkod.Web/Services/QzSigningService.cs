using System.Security.Cryptography;
using System.Text;
using FasonBarkod.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FasonBarkod.Web.Services;

public interface IQzSigningService
{
    bool IsConfigured { get; }

    string? GetCertificatePem();

    string? Sign(string request);
}

/// <summary>
/// QZ Tray: public sertifika istemciye gömülür, private key yalnızca sunucuda kalır.
/// </summary>
public class QzSigningService(
    IOptions<PrinterOptions> printerOptions,
    IWebHostEnvironment env,
    ILogger<QzSigningService> logger) : IQzSigningService
{
    private readonly PrinterOptions _options = printerOptions.Value;
    private string? _cachedCert;
    private RSA? _cachedRsa;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(GetCertificatePem()) && TryLoadPrivateKey(out _);

    public string? GetCertificatePem()
    {
        if (_cachedCert is not null)
        {
            return _cachedCert;
        }

        if (!string.IsNullOrWhiteSpace(_options.QzCertificatePem))
        {
            _cachedCert = _options.QzCertificatePem.Replace("\\n", "\n", StringComparison.Ordinal).Trim();
            return _cachedCert;
        }

        var path = ResolvePath(_options.QzCertificatePath, "QzSigning/digital-certificate.txt");
        if (path is not null && File.Exists(path))
        {
            _cachedCert = File.ReadAllText(path).Trim();
            return _cachedCert;
        }

        return null;
    }

    public string? Sign(string request)
    {
        if (string.IsNullOrEmpty(request))
        {
            return null;
        }

        if (!TryLoadPrivateKey(out var rsa))
        {
            logger.LogWarning("QZ private key yüklenemedi");
            return null;
        }

        try
        {
            var data = Encoding.UTF8.GetBytes(request);
            var signature = rsa.SignData(data, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(signature);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "QZ imzalama hatası");
            return null;
        }
    }

    private bool TryLoadPrivateKey(out RSA rsa)
    {
        if (_cachedRsa is not null)
        {
            rsa = _cachedRsa;
            return true;
        }

        var keyPath = ResolvePath(_options.QzPrivateKeyPath, "QzSigning/private-key.pem");
        if (keyPath is null || !File.Exists(keyPath))
        {
            rsa = null!;
            return false;
        }

        try
        {
            var pem = File.ReadAllText(keyPath);
            _cachedRsa = RSA.Create();
            _cachedRsa.ImportFromPem(pem);
            rsa = _cachedRsa;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "QZ private key okunamadı: {Path}", keyPath);
            rsa = null!;
            return false;
        }
    }

    private string? ResolvePath(string? configured, string relativeDefault)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(env.ContentRootPath, configured);
        }

        return Path.Combine(env.ContentRootPath, relativeDefault);
    }
}
