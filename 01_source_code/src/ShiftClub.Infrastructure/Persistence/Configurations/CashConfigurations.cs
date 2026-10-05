using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class CashRegisterConfiguration : IEntityTypeConfiguration<CashRegister>
{
    public void Configure(EntityTypeBuilder<CashRegister> builder)
    {
        builder.ToTable("cash_registers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CashShiftConfiguration : IEntityTypeConfiguration<CashShift>
{
    public void Configure(EntityTypeBuilder<CashShift> builder)
    {
        builder.ToTable("cash_shifts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.OpeningCash).HasPrecision(18, 2);
        builder.Property(x => x.ClosingCashActual).HasPrecision(18, 2);
        builder.Property(x => x.ClosingCashExpected).HasPrecision(18, 2);
        builder.Property(x => x.Discrepancy).HasPrecision(18, 2);
        builder.Property(x => x.SalesCash).HasPrecision(18, 2);
        builder.Property(x => x.SalesCard).HasPrecision(18, 2);
        builder.Property(x => x.SalesKaspi).HasPrecision(18, 2);
        builder.Property(x => x.SalesTransfer).HasPrecision(18, 2);
        builder.Property(x => x.SalesOther).HasPrecision(18, 2);
        builder.Property(x => x.RefundsCash).HasPrecision(18, 2);
        builder.Property(x => x.RefundsCard).HasPrecision(18, 2);
        builder.Property(x => x.RefundsKaspi).HasPrecision(18, 2);
        builder.Property(x => x.RefundsTransfer).HasPrecision(18, 2);
        builder.Property(x => x.RefundsOther).HasPrecision(18, 2);
        builder.Property(x => x.DepositsTotal).HasPrecision(18, 2);
        builder.Property(x => x.CashInTotal).HasPrecision(18, 2);
        builder.Property(x => x.CashOutTotal).HasPrecision(18, 2);
        builder.Property(x => x.ExpenseTotal).HasPrecision(18, 2);
        builder.Property(x => x.OpenComment).HasMaxLength(500);
        builder.Property(x => x.CloseComment).HasMaxLength(500);
        builder.Property(x => x.DiscrepancyReason).HasMaxLength(500);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => new { x.CashRegisterId, x.Status });
        builder.HasOne(x => x.CashRegister).WithMany(x => x.Shifts).HasForeignKey(x => x.CashRegisterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("cash_movements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Category).HasMaxLength(100);
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.HasOne(x => x.CashShift).WithMany(x => x.Movements).HasForeignKey(x => x.CashShiftId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("receipts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Subtotal).HasPrecision(18, 2);
        builder.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.Total).HasPrecision(18, 2);
        builder.Property(x => x.PaidTotal).HasPrecision(18, 2);
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.IdempotencyKey);
        builder.HasIndex(x => x.CashShiftId);
        builder.HasOne(x => x.CashShift).WithMany(x => x.Receipts).HasForeignKey(x => x.CashShiftId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ReceiptItemConfiguration : IEntityTypeConfiguration<ReceiptItem>
{
    public void Configure(EntityTypeBuilder<ReceiptItem> builder)
    {
        builder.ToTable("receipt_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ItemType).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Name).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 3);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.LineTotal).HasPrecision(18, 2);
        builder.HasOne(x => x.Receipt).WithMany(x => x.Items).HasForeignKey(x => x.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.ExternalReference).HasMaxLength(200);
        builder.HasOne(x => x.Receipt).WithMany(x => x.Payments).HasForeignKey(x => x.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> builder)
    {
        builder.ToTable("document_sequences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(x => new { x.BranchId, x.Type, x.Year }).IsUnique();
    }
}
