using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class WorkShiftConfiguration : IEntityTypeConfiguration<WorkShift>
{
    public void Configure(EntityTypeBuilder<WorkShift> builder)
    {
        builder.ToTable("work_shifts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.HasIndex(x => new { x.EmployeeId, x.WorkDate });
        builder.HasIndex(x => new { x.BranchId, x.WorkDate });
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Notes).WithOne(n => n.WorkShift).HasForeignKey(n => n.WorkShiftId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class WorkShiftNoteConfiguration : IEntityTypeConfiguration<WorkShiftNote>
{
    public void Configure(EntityTypeBuilder<WorkShiftNote> builder)
    {
        builder.ToTable("work_shift_notes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AuthorName).HasMaxLength(200);
        builder.Property(x => x.Kind).HasMaxLength(32);
        builder.Property(x => x.Text).HasMaxLength(2000);
        builder.HasIndex(x => x.WorkShiftId);
        builder.HasIndex(x => x.CreatedAt);
    }
}

public class PayrollAccrualConfiguration : IEntityTypeConfiguration<PayrollAccrual>
{
    public void Configure(EntityTypeBuilder<PayrollAccrual> builder)
    {
        builder.ToTable("payroll_accruals");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Basis).HasMaxLength(300);
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.HasIndex(x => new { x.EmployeeId, x.PeriodFrom, x.PeriodTo });
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
