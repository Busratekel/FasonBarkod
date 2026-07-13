using FasonBarkod.Infrastructure.Sap;
using Microsoft.AspNetCore.Mvc;

namespace FasonBarkod.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class SapHealthController(ISapConnectionFactory connectionFactory) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        if (!connectionFactory.IsConfigured)
        {
            return Ok(new
            {
                status = "not_configured",
                message = "Sap:Enabled=true ve bağlantı bilgileri gerekli."
            });
        }

        try
        {
            using var connection = connectionFactory.OpenConnection();
            return Ok(new
            {
                status = "connected",
                message = "SAP RFC bağlantısı başarılı."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new
            {
                status = "error",
                message = ex.Message
            });
        }
    }
}
