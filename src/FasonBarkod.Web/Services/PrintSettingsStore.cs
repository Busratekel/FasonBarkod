using FasonBarkod.Core.Entities;
using FasonBarkod.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FasonBarkod.Web.Services;

public class PrintSettings
{
    /// <summary>
    /// Boş ise Windows varsayılan yazıcısı kullanılır.
    /// </summary>
    public string? PrinterName { get; set; }
}

public interface IPrintSettingsStore
{
    Task<PrintSettings> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(PrintSettings settings, string? updatedByUserId, CancellationToken cancellationToken = default);
}

public class DbPrintSettingsStore(ApplicationDbContext context) : IPrintSettingsStore
{
    public async Task<PrintSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await context.AppPrintSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == AppPrintSettings.SingletonId, cancellationToken);

        return new PrintSettings
        {
            PrinterName = row?.PrinterName
        };
    }

    public async Task SaveAsync(
        PrintSettings settings,
        string? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var row = await context.AppPrintSettings
            .FirstOrDefaultAsync(x => x.Id == AppPrintSettings.SingletonId, cancellationToken);

        if (row is null)
        {
            row = new AppPrintSettings { Id = AppPrintSettings.SingletonId };
            context.AppPrintSettings.Add(row);
        }

        row.PrinterName = string.IsNullOrWhiteSpace(settings.PrinterName)
            ? null
            : settings.PrinterName.Trim();
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedByUserId = updatedByUserId;

        await context.SaveChangesAsync(cancellationToken);
    }
}
