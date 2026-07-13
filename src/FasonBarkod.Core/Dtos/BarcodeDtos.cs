using FasonBarkod.Core.Enums;

namespace FasonBarkod.Core.Dtos;

public record CreateBarcodeRequest(
    int OrderLineId,
    decimal Quantity,
    string? PrintedBy);

public record BarcodePrintDto(
    int Id,
    string BarcodeNo,
    string SalesOrderNo,
    string MaterialCode,
    string? MaterialName,
    decimal Quantity,
    DateTime PrintDate,
    string? PrintedBy,
    BarcodePrintStatus Status,
    bool SapSent);

public record SapBarcodePayload(
    string BarcodeNo,
    string SalesOrderNo,
    string MaterialCode,
    decimal Quantity,
    DateTime PrintDate,
    string? PrintedBy);

public record SapTransferResultDto(
    bool Success,
    string? Message);
