using FasonBarkod.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FasonBarkod.Infrastructure.Services;

public class BarcodeNumberService(ApplicationDbContext context)
{
    public async Task<string> GenerateNextAsync(string salesOrderNo, CancellationToken cancellationToken = default)
    {
        var prefix = $"FSN-{DateTime.UtcNow:yyyy}-";
        var lastBarcode = await context.BarcodePrints
            .Where(b => b.BarcodeNo.StartsWith(prefix))
            .OrderByDescending(b => b.BarcodeNo)
            .Select(b => b.BarcodeNo)
            .FirstOrDefaultAsync(cancellationToken);

        var nextSequence = 1;
        if (lastBarcode is not null && lastBarcode.Length > prefix.Length)
        {
            var sequencePart = lastBarcode[prefix.Length..];
            if (int.TryParse(sequencePart, out var lastSequence))
            {
                nextSequence = lastSequence + 1;
            }
        }

        return $"{prefix}{nextSequence:D8}";
    }
}
