using FasonBarkod.Core.Dtos;
using FasonBarkod.Core.Entities;
using FasonBarkod.Core.Enums;
using FasonBarkod.Core.Sap;
using FasonBarkod.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FasonBarkod.Infrastructure.Services;

public class BarcodeService(
    ApplicationDbContext context,
    ISapService sapService) : IBarcodeService
{
    public async Task<IReadOnlyList<BarcodePrintDto>> GetPrintsAsync(CancellationToken cancellationToken = default)
    {
        return await context.BarcodePrints
            .Include(x => x.PrintedByUser)
            .OrderByDescending(x => x.PrintDate)
            .Select(x => MapToDto(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<BarcodePrintDto?> GetPrintAsync(int id, CancellationToken cancellationToken = default)
    {
        var print = await context.BarcodePrints
            .Include(x => x.PrintedByUser)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return print is null ? null : MapToDto(print);
    }

    public async Task<BarcodePrintDto?> CreatePrintAsync(CreateBarcodeRequest request, CancellationToken cancellationToken = default)
    {
        var line = await context.SalesOrderLines.FindAsync([request.OrderLineId], cancellationToken);
        if (line is null || request.Quantity <= 0 || request.Quantity > line.Quantity)
        {
            return null;
        }

        var sapResult = await sapService.CreateBarcodeAsync(new SapBarcodeRequest(
            BarcodeModuleType.Sas,
            LabelType.KoliUstu,
            line.SalesOrderNo,
            line.Id.ToString(),
            line.MaterialCode,
            PackageQuantity: 10,
            PrintQuantity: request.Quantity,
            BrandCode: null), cancellationToken);

        if (!sapResult.Success && sapResult.BarcodeNumbers.Count == 0)
        {
            return null;
        }

        var barcodeNo = sapResult.BarcodeNumbers.FirstOrDefault() ?? $"PENDING-{line.SalesOrderNo}";

        var print = new BarcodePrint
        {
            BarcodeNo = barcodeNo,
            SalesOrderNo = line.SalesOrderNo,
            MaterialCode = line.MaterialCode,
            MaterialName = line.MaterialName,
            Quantity = request.Quantity,
            PrintDate = DateTime.UtcNow,
            Status = sapResult.Success ? BarcodePrintStatus.Printed : BarcodePrintStatus.Pending,
            SapSent = false
        };

        context.BarcodePrints.Add(print);

        if (!string.IsNullOrWhiteSpace(sapResult.ErrorMessage))
        {
            context.SapTransferLogs.Add(new SapTransferLog
            {
                BarcodePrint = print,
                Result = SapTransferResult.Pending,
                ErrorMessage = sapResult.ErrorMessage,
                RequestPayload = $"ZMM_N_SAS_B mock çağrısı — {line.SalesOrderNo}"
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        return MapToDto(print, request.PrintedBy);
    }

    private static BarcodePrintDto MapToDto(BarcodePrint print, string? printedByOverride = null)
    {
        return new BarcodePrintDto(
            print.Id,
            print.BarcodeNo,
            print.SalesOrderNo,
            print.MaterialCode,
            print.MaterialName,
            print.Quantity,
            print.PrintDate,
            printedByOverride ?? print.PrintedByUser?.FullName ?? print.PrintedByUser?.Email,
            print.Status,
            print.SapSent);
    }
}
