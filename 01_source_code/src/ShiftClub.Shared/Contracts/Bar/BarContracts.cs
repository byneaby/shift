using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Shared.Contracts.Bar;

public sealed record ProductCategoryDto(Guid Id, string Name, string Code, int SortOrder, bool IsActive);

public sealed record CreateProductCategoryRequest(string Name, string? Code, int SortOrder = 0);

public sealed record UpdateProductCategoryRequest(
    string? Name,
    string? Code,
    int? SortOrder,
    bool? IsActive);

public sealed record ProductDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string Sku,
    string? Barcode,
    string Unit,
    decimal CostPrice,
    decimal SalePrice,
    decimal StockQty,
    decimal MinStockQty,
    bool IsActive,
    bool IsLowStock,
    string? ImageUrl = null);

public sealed record CreateProductRequest(
    Guid CategoryId,
    string Name,
    string Sku,
    string? Barcode,
    string Unit,
    decimal CostPrice,
    decimal SalePrice,
    decimal InitialStock,
    decimal MinStockQty,
    string? ImageUrl = null);

public sealed record UpdateProductRequest(
    Guid? CategoryId,
    string? Name,
    string? Sku,
    string? Barcode,
    string? Unit,
    decimal? CostPrice,
    decimal? SalePrice,
    decimal? MinStockQty,
    bool? IsActive,
    string? ImageUrl);

public sealed record AdjustStockRequest(
    decimal QuantityDelta,
    InventoryMovementType Type,
    string? Comment,
    string? IdempotencyKey);

public sealed record CreateBarOrderItemRequest(Guid ProductId, decimal Quantity);

public sealed record CreateBarOrderRequest(
    Guid? ComputerId,
    Guid? GamingSessionId,
    BarOrderPaymentMode PaymentMode,
    string? Comment,
    string? IdempotencyKey,
    IReadOnlyList<CreateBarOrderItemRequest> Items);

public sealed record BarOrderItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record BarOrderDto(
    Guid Id,
    string Number,
    BarOrderStatus Status,
    BarOrderPaymentMode PaymentMode,
    Guid? ComputerId,
    string? ComputerName,
    decimal Total,
    string? Comment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? CompletedAt,
    Guid? ReceiptId,
    IReadOnlyList<BarOrderItemDto> Items);

public sealed record UpdateBarOrderStatusRequest(BarOrderStatus Status, PaymentMethod? PaymentMethod = null);

public sealed record SellBarItemsRequest(
    IReadOnlyList<CreateBarOrderItemRequest> Items,
    PaymentMethod PaymentMethod,
    Guid? ComputerId,
    string? Comment,
    string? IdempotencyKey,
    Guid? GamingSessionId = null,
    Guid? CustomerId = null,
    IReadOnlyList<PaymentPartDto>? Payments = null);
