using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence.Configurations;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.FloorBackgroundHex).HasMaxLength(16);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

public class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable("zones");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ColorHex).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        builder.HasOne(x => x.Branch).WithMany(x => x.Zones).HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Login).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.Iin).HasMaxLength(12);
        builder.Property(x => x.PayType).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.HourlyRate).HasPrecision(18, 2);
        builder.Property(x => x.MonthlySalary).HasPrecision(18, 2);
        builder.Property(x => x.ShiftRate).HasPrecision(18, 2);
        builder.HasIndex(x => x.Login).IsUnique();
        builder.HasIndex(x => x.TelegramUserId);
        builder.HasIndex(x => x.Iin).IsUnique().HasFilter("\"Iin\" IS NOT NULL");
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Credential).WithOne(x => x.Employee)
            .HasForeignKey<EmployeeCredential>(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EmployeeCredentialConfiguration : IEntityTypeConfiguration<EmployeeCredential>
{
    public void Configure(EntityTypeBuilder<EmployeeCredential> builder)
    {
        builder.ToTable("employee_credentials");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PinHash).HasMaxLength(500);
        builder.HasIndex(x => x.EmployeeId).IsUnique();
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.GroupName).HasMaxLength(100);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(x => new { x.RoleId, x.PermissionId });
        builder.HasOne(x => x.Role).WithMany(x => x.RolePermissions).HasForeignKey(x => x.RoleId);
        builder.HasOne(x => x.Permission).WithMany(x => x.RolePermissions).HasForeignKey(x => x.PermissionId);
    }
}

public class EmployeeRoleConfiguration : IEntityTypeConfiguration<EmployeeRole>
{
    public void Configure(EntityTypeBuilder<EmployeeRole> builder)
    {
        builder.ToTable("employee_roles");
        builder.HasKey(x => new { x.EmployeeId, x.RoleId });
        builder.HasOne(x => x.Employee).WithMany(x => x.EmployeeRoles).HasForeignKey(x => x.EmployeeId);
        builder.HasOne(x => x.Role).WithMany(x => x.EmployeeRoles).HasForeignKey(x => x.RoleId);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(100);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasMaxLength(500);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Value).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasIndex(x => new { x.BranchId, x.Key }).IsUnique();
    }
}
