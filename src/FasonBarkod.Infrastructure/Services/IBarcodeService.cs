using FasonBarkod.Core.Dtos;

namespace FasonBarkod.Infrastructure.Services;

public interface IBarcodeService
{
    Task<IReadOnlyList<BarcodePrintDto>> GetPrintsAsync(CancellationToken cancellationToken = default);

    Task<BarcodePrintDto?> GetPrintAsync(int id, CancellationToken cancellationToken = default);

    Task<BarcodePrintDto?> CreatePrintAsync(CreateBarcodeRequest request, CancellationToken cancellationToken = default);
}
