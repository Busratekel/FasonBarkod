using FasonBarkod.Core.Sap;



namespace FasonBarkod.Infrastructure.Services;



public interface ISapService

{

    Task<IReadOnlyList<SasLineDto>> ListSasAsync(

        string purchaseOrderNo,

        string? vendorCode = null,

        CancellationToken cancellationToken = default);



    Task<SapBarcodeResult> CreateBarcodeAsync(

        SapBarcodeRequest request,

        CancellationToken cancellationToken = default);



    Task<SapBarcodeResult> ReprintBarcodeAsync(

        SapReprintRequest request,

        CancellationToken cancellationToken = default);

}

