namespace FasonBarkod.Core.Dtos;

public record OrderSummaryDto(
    string SalesOrderNo,
    string? CustomerName,
    DateTime? OrderDate,
    int LineCount,
    decimal TotalQuantity);

public record OrderLineDto(
    int Id,
    string SalesOrderNo,
    string? CustomerCode,
    string? CustomerName,
    string MaterialCode,
    string? MaterialName,
    string? Color,
    string? BatchNo,
    decimal Quantity,
    DateTime? OrderDate);

public record OrderDetailDto(
    string SalesOrderNo,
    string? CustomerName,
    DateTime? OrderDate,
    IReadOnlyList<OrderLineDto> Lines);
