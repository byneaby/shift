using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("product_categories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Sku).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Barcode).HasMaxLength(64);
        builder.Property(x => x.Unit).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CostPrice).HasPrecision(18, 2);
        builder.Property(x => x.SalePrice).HasPrecision(18, 2);
        builder.Property(x => x.StockQty).HasPrecision(18, 3);
        builder.Property(x => x.MinStockQty).HasPrecision(18, 3);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.Property(x => x.ImageUrl).HasMaxLength(500);
        builder.HasIndex(x => new { x.BranchId, x.Sku }).IsUnique();
        builder.HasOne(x => x.Category).WithMany(x => x.Products).HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("inventory_movements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.QuantityDelta).HasPrecision(18, 3);
        builder.Property(x => x.StockAfter).HasPrecision(18, 3);
        builder.Property(x => x.UnitCost).HasPrecision(18, 2);
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.HasIndex(x => x.IdempotencyKey);
        builder.HasIndex(x => new { x.ProductId, x.CreatedAt });
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BarOrderConfiguration : IEntityTypeConfiguration<BarOrder>
{
    public void Configure(EntityTypeBuilder<BarOrder> builder)
    {
        builder.ToTable("bar_orders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.PaymentMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Total).HasPrecision(18, 2);
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.IdempotencyKey);
    }
}

public class BarOrderItemConfiguration : IEntityTypeConfiguration<BarOrderItem>
{
    public void Configure(EntityTypeBuilder<BarOrderItem> builder)
    {
        builder.ToTable("bar_order_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 3);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.LineTotal).HasPrecision(18, 2);
        builder.HasOne(x => x.BarOrder).WithMany(x => x.Items).HasForeignKey(x => x.BarOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
