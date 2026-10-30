using Erp.Domain.Commerce;
using Erp.Domain.MasterData;
using Erp.Domain.Operations;
using Erp.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Infrastructure.Persistence;

internal sealed class DispatchShipmentConfiguration : IEntityTypeConfiguration<DispatchShipment>
{
    public void Configure(EntityTypeBuilder<DispatchShipment> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "DispatchShipments");
        entity.Property(value => value.ShipmentNumber).HasMaxLength(40).IsRequired();
        entity.Property(value => value.Status).HasMaxLength(24).IsRequired();
        entity.Property(value => value.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.Property(value => value.AuthorizedBySubject).HasMaxLength(200);
        entity.Property(value => value.DispatchedBySubject).HasMaxLength(200);
        entity.HasIndex(value => new { value.CompanyId, value.ShipmentNumber }).IsUnique();
        entity.HasOne<SalesOrder>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.SalesOrderId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<SalesOrderRevision>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.SalesOrderRevisionId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DispatchShipmentLineConfiguration : IEntityTypeConfiguration<DispatchShipmentLine>
{
    public void Configure(EntityTypeBuilder<DispatchShipmentLine> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "DispatchShipmentLines");
        entity.Property(value => value.Quantity).HasPrecision(20, 6);
        entity.HasOne<DispatchShipment>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.DispatchShipmentId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<SalesOrderLine>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.SalesOrderLineId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ProductVariant>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.ProductVariantId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Item>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.ItemId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Warehouse>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.WarehouseId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DispatchCartonConfiguration : IEntityTypeConfiguration<DispatchCarton>
{
    public void Configure(EntityTypeBuilder<DispatchCarton> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "DispatchCartons");
        entity.Property(value => value.CartonNumber).HasMaxLength(60).IsRequired();
        entity.Property(value => value.Weight).HasPrecision(20, 6);
        entity.HasIndex(value => new { value.CompanyId, value.CartonNumber }).IsUnique();
        entity.HasOne<DispatchShipment>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.DispatchShipmentId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DispatchCartonLineConfiguration : IEntityTypeConfiguration<DispatchCartonLine>
{
    public void Configure(EntityTypeBuilder<DispatchCartonLine> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "DispatchCartonLines");
        entity.Property(value => value.Quantity).HasPrecision(20, 6);
        entity.HasOne<DispatchCarton>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.DispatchCartonId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<DispatchShipmentLine>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.DispatchShipmentLineId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class HrDepartmentConfiguration : IEntityTypeConfiguration<HrDepartment>
{
    public void Configure(EntityTypeBuilder<HrDepartment> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "HrDepartments");
        MappingRules.ConfigureCodeAndName(entity);
        entity.HasIndex(value => new { value.CompanyId, value.Code }).IsUnique();
    }
}

internal sealed class HrDesignationConfiguration : IEntityTypeConfiguration<HrDesignation>
{
    public void Configure(EntityTypeBuilder<HrDesignation> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "HrDesignations");
        MappingRules.ConfigureCodeAndName(entity);
        entity.HasIndex(value => new { value.CompanyId, value.Code }).IsUnique();
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "Employees");
        entity.Property(value => value.EmployeeNumber).HasMaxLength(40).IsRequired();
        entity.Property(value => value.FirstName).HasMaxLength(100).IsRequired();
        entity.Property(value => value.LastName).HasMaxLength(100).IsRequired();
        entity.Property(value => value.PersonalEmail).HasMaxLength(254);
        entity.Property(value => value.Phone).HasMaxLength(40);
        entity.HasIndex(value => new { value.CompanyId, value.EmployeeNumber }).IsUnique();
        entity.HasOne<HrDepartment>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.DepartmentId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<HrDesignation>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.DesignationId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class HrShiftConfiguration : IEntityTypeConfiguration<HrShift>
{
    public void Configure(EntityTypeBuilder<HrShift> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "HrShifts");
        entity.Property(value => value.Code).HasMaxLength(40).IsRequired();
        entity.Property(value => value.Name).HasMaxLength(160).IsRequired();
        entity.HasIndex(value => new { value.CompanyId, value.Code }).IsUnique();
    }
}

internal sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "AttendanceRecords");
        entity.Property(value => value.Status).HasMaxLength(24).IsRequired();
        entity.Property(value => value.RecordedBySubject).HasMaxLength(200).IsRequired();
        entity.HasIndex(value => new { value.CompanyId, value.EmployeeId, value.AttendanceDate }).IsUnique();
        entity.HasOne<Employee>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.EmployeeId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<HrShift>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.ShiftId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LeaveRecordConfiguration : IEntityTypeConfiguration<LeaveRecord>
{
    public void Configure(EntityTypeBuilder<LeaveRecord> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "LeaveRecords");
        entity.Property(value => value.LeaveType).HasMaxLength(60).IsRequired();
        entity.Property(value => value.Status).HasMaxLength(24).IsRequired();
        entity.Property(value => value.Reason).HasMaxLength(500);
        entity.Property(value => value.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.Property(value => value.ReviewedBySubject).HasMaxLength(200);
        entity.HasOne<Employee>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.EmployeeId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EmployeeSkillConfiguration : IEntityTypeConfiguration<EmployeeSkill>
{
    public void Configure(EntityTypeBuilder<EmployeeSkill> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "EmployeeSkills");
        entity.Property(value => value.Skill).HasMaxLength(120).IsRequired();
        entity.Property(value => value.Proficiency).HasMaxLength(24).IsRequired();
        entity.HasIndex(value => new { value.CompanyId, value.EmployeeId, value.Skill }).IsUnique();
        entity.HasOne<Employee>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.EmployeeId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TrainingRecordConfiguration : IEntityTypeConfiguration<TrainingRecord>
{
    public void Configure(EntityTypeBuilder<TrainingRecord> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "TrainingRecords");
        entity.Property(value => value.CourseName).HasMaxLength(160).IsRequired();
        entity.Property(value => value.CertificateReference).HasMaxLength(100);
        entity.HasOne<Employee>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.EmployeeId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InAppNotificationConfiguration : IEntityTypeConfiguration<InAppNotification>
{
    public void Configure(EntityTypeBuilder<InAppNotification> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "InAppNotifications");
        entity.Property(value => value.Category).HasMaxLength(40).IsRequired();
        entity.Property(value => value.Title).HasMaxLength(160).IsRequired();
        entity.Property(value => value.Message).HasMaxLength(1000).IsRequired();
        entity.Property(value => value.SourceType).HasMaxLength(60).IsRequired();
        entity.HasIndex(value => new { value.CompanyId, value.RecipientUserId, value.CreatedAt });
    }
}

internal sealed class NotificationReadReceiptConfiguration : IEntityTypeConfiguration<NotificationReadReceipt>
{
    public void Configure(EntityTypeBuilder<NotificationReadReceipt> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "NotificationReadReceipts");
        entity.HasIndex(value => new { value.CompanyId, value.NotificationId, value.UserId }).IsUnique();
        entity.HasOne<InAppNotification>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.NotificationId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class NotificationDeliveryLogConfiguration : IEntityTypeConfiguration<NotificationDeliveryLog>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryLog> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "NotificationDeliveryLogs");
        entity.Property(value => value.Recipient).HasMaxLength(254).IsRequired();
        entity.Property(value => value.Status).HasMaxLength(24).IsRequired();
        entity.Property(value => value.Error).HasMaxLength(1000);
        entity.HasIndex(value => new { value.CompanyId, value.NotificationId, value.Recipient }).IsUnique();
        entity.HasOne<InAppNotification>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.NotificationId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
