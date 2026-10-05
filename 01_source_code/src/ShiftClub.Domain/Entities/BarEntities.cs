using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

public class ProductCategory : Common.Entity
{
    public Guid BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public class Product : Common.Entity
{
    public Guid BranchId { get; set; }
    public Guid CategoryId { get; set; }
    public ProductCategory Category { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Unit { get; set; } = "шт";

    public decimal CostPrice { get; set; }
    public decimal SalePrice { get; set; }
    public decimal StockQty { get; set; }
    public decimal MinStockQty { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    /// <summary>Relative URL like /media/bar/{id}.jpg or absolute http(s) URL.</summary>
    public string? ImageUrl { get; set; }
}

public class InventoryMovement : Common.Entity
{
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public InventoryMovementType Type { get; set; }
    public decimal QuantityDelta { get; set; }
    public decimal StockAfter { get; set; }
    public decimal? UnitCost { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Comment { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class BarOrder : Common.Entity
{
    public Guid BranchId { get; set; }
    public string Number { get; set; } = string.Empty;
    public BarOrderStatus Status { get; set; } = BarOrderStatus.New;
    public BarOrderPaymentMode PaymentMode { get; set; } = BarOrderPaymentMode.PayAtCashier;

    public Guid? ComputerId { get; set; }
    public Guid? GamingSessionId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? AssignedEmployeeId { get; set; }
    public Guid? CreatedByEmployeeId { get; set; }

    public decimal Total { get; set; }
    public string? Comment { get; set; }
    public string? IdempotencyKey { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? ReceiptId { get; set; }
    public bool StockDeducted { get; set; }

    public ICollection<BarOrderItem> Items { get; set; } = new List<BarOrderItem>();
}

public class BarOrderItem : Common.Entity
{
    public Guid BarOrderId { get; set; }
    public BarOrder BarOrder { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
