using System.Text;
using FasonBarkod.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FasonBarkod.Web.Controllers;

/// <summary>
/// QZ Tray imzalama — Halil'in tarif ettiği model:
/// istemci: setCertificatePromise + fetch /api/qz/sign
/// sunucu: private-key.pem ile SHA512 imza
/// </summary>
[Authorize]
[Route("api/qz")]
public class QzApiController(IQzSigningService signing, ILogger<QzApiController> logger) : Controller
{
    [HttpGet("certificate")]
    [AllowAnonymous]
    public IActionResult Certificate()
    {
        var cert = signing.GetCertificatePem();
        if (string.IsNullOrWhiteSpace(cert))
        {
            return NotFound("QZ sertifikası yapılandırılmamış.");
        }

        return Content(cert, "text/plain", Encoding.UTF8);
    }

    [HttpGet("sign")]
    public IActionResult SignRequest([FromQuery] string request)
    {
        if (string.IsNullOrEmpty(request))
        {
            return BadRequest("request boş");
        }

        var signature = signing.Sign(request);
        if (string.IsNullOrEmpty(signature))
        {
            logger.LogWarning("QZ imza üretilemedi");
            return NotFound("İmzalama yapılandırılmamış veya hatalı.");
        }

        return Content(signature, "text/plain", Encoding.UTF8);
    }
}
