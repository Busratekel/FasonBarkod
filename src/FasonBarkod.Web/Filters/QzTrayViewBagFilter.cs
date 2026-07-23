using System.Text;
using System.Text.Json;
using FasonBarkod.Infrastructure.Configuration;
using FasonBarkod.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace FasonBarkod.Web.Filters;

/// <summary>
/// QZ Tray ayarını, sertifikayı ve bekleyen etiket job'larını ViewBag'a taşır.
/// </summary>
public class QzTrayViewBagFilter(
    IOptions<PrinterOptions> printerOptions,
    IQzSigningService signingService) : IResultFilter
{
    public const string PendingJobsSessionKey = "QzPendingJobsBase64";
    public const string PendingPrintIdsSessionKey = "QzPendingPrintIds";

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Controller is not Controller controller)
        {
            return;
        }

        var useQz = printerOptions.Value.UseQzTray && printerOptions.Value.Enabled;
        controller.ViewBag.QzTrayEnabled = useQz;
        controller.ViewBag.QzSigningConfigured = signingService.IsConfigured;
        controller.ViewBag.QzCertificatePem = signingService.GetCertificatePem();

        // Job'ları yalnızca gerçek View cevabında çek.
        // RedirectToAction üzerinde çekilirse session boşa boşalır, Detail sayfasında toast/basım hiç çalışmaz.
        if (context.Result is not (ViewResult or PartialViewResult))
        {
            return;
        }

        var session = context.HttpContext.Session;
        var json = session.GetString(PendingJobsSessionKey);
        if (!string.IsNullOrWhiteSpace(json))
        {
            controller.ViewBag.QzPendingJobsJson = json;
            session.Remove(PendingJobsSessionKey);
        }

        var idsJson = session.GetString(PendingPrintIdsSessionKey);
        if (!string.IsNullOrWhiteSpace(idsJson))
        {
            controller.ViewBag.QzPendingPrintIdsJson = idsJson;
            session.Remove(PendingPrintIdsSessionKey);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    public static void QueueJobs(
        Controller controller,
        IReadOnlyList<string> rawJobs,
        IReadOnlyList<int>? printIds = null)
    {
        var payload = rawJobs
            .Select(j => Convert.ToBase64String(Encoding.UTF8.GetBytes(j)))
            .ToList();
        controller.HttpContext.Session.SetString(
            PendingJobsSessionKey,
            JsonSerializer.Serialize(payload));

        if (printIds is { Count: > 0 })
        {
            controller.HttpContext.Session.SetString(
                PendingPrintIdsSessionKey,
                JsonSerializer.Serialize(printIds));
        }
    }
}
