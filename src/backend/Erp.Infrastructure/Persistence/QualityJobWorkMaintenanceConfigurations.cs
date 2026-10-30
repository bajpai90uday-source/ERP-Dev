using Erp.Domain.MasterData;
using Erp.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Infrastructure.Persistence;

internal sealed class QualityDefectCodeConfiguration : IEntityTypeConfiguration<QualityDefectCode>
{
    public void Configure(EntityTypeBuilder<QualityDefectCode> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "QualityDefectCodes");
        entity.Property(value => value.Code).HasMaxLength(40).IsRequired();
        entity.Property(value => value.Description).HasMaxLength(240).IsRequired();
        entity.HasIndex(value => new { value.CompanyId, value.Code }).IsUnique();
    }
}

internal sealed class QualityInspectionConfiguration : IEntityTypeConfiguration<QualityInspection>
{
    public void Configure(EntityTypeBuilder<QualityInspection> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "QualityInspections");
        entity.Property(value => value.InspectionNumber).HasMaxLength(40).IsRequired();
        entity.Property(value => value.Stage).HasMaxLength(24).IsRequired();
        entity.Property(value => value.SourceType).HasMaxLength(32).IsRequired();
        entity.Property(value => value.Status).HasMaxLength(24).IsRequired();
        entity.Property(value => value.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.Property(value => value.ApprovedBySubject).HasMaxLength(200);
        entity.Property(value => value.Reason).HasMaxLength(500);
        entity.Property(value => value.InspectedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.AcceptedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.RejectedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ReworkQuantity).HasPrecision(20, 6);
        entity.Property(value => value.HoldQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ConcurrencyVersion).IsConcurrencyToken();
        entity.HasIndex(value => new { value.CompanyId, value.InspectionNumber }).IsUnique();
        entity.HasIndex(value => new
        {
            value.CompanyId, value.SourceType, value.SourceLineId, value.Stage,
        }).IsUnique();
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

internal sealed class QualityInspectionDefectConfiguration : IEntityTypeConfiguration<QualityInspectionDefect>
{
    public void Configure(EntityTypeBuilder<QualityInspectionDefect> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "QualityInspectionDefects");
        entity.Property(value => value.Quantity).HasPrecision(20, 6);
        entity.Property(value => value.Notes).HasMaxLength(500);
        entity.HasOne<QualityInspection>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.InspectionId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<QualityDefectCode>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.DefectCodeId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SubcontractorProfileConfiguration : IEntityTypeConfiguration<SubcontractorProfile>
{
    public void Configure(EntityTypeBuilder<SubcontractorProfile> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "SubcontractorProfiles");
        entity.Property(value => value.RegistrationCode).HasMaxLength(80).IsRequired();
        entity.HasIndex(value => new { value.CompanyId, value.SupplierId }).IsUnique();
        entity.HasOne<Supplier>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.SupplierId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class JobWorkOrderConfiguration : IEntityTypeConfiguration<JobWorkOrder>
{
    public void Configure(EntityTypeBuilder<JobWorkOrder> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "JobWorkOrders");
        entity.Property(value => value.OrderNumber).HasMaxLength(40).IsRequired();
        entity.Property(value => value.Status).HasMaxLength(24).IsRequired();
        entity.Property(value => value.CurrencyCode).HasMaxLength(3).IsRequired();
        entity.Property(value => value.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.Property(value => value.PlannedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ProcessedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.RejectedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.UnitCharge).HasPrecision(20, 6);
        entity.Property(value => value.ConcurrencyVersion).IsConcurrencyToken();
        entity.HasIndex(value => new { value.CompanyId, value.OrderNumber }).IsUnique();
        entity.HasOne<SubcontractorProfile>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.SubcontractorId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Item>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.OutputItemId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Warehouse>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.OutputWarehouseId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class JobWorkMaterialIssueConfiguration : IEntityTypeConfiguration<JobWorkMaterialIssue>
{
    public void Configure(EntityTypeBuilder<JobWorkMaterialIssue> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "JobWorkMaterialIssues");
        entity.Property(value => value.IssuedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ReturnedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ConcurrencyVersion).IsConcurrencyToken();
        entity.HasOne<JobWorkOrder>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.JobWorkOrderId })
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

internal sealed class SubcontractorCustodyTransactionConfiguration : IEntityTypeConfiguration<SubcontractorCustodyTransaction>
{
    public void Configure(EntityTypeBuilder<SubcontractorCustodyTransaction> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "SubcontractorCustodyTransactions");
        entity.Property(value => value.TransactionType).HasMaxLength(16).IsRequired();
        entity.Property(value => value.Quantity).HasPrecision(20, 6);
        entity.Property(value => value.ActorSubject).HasMaxLength(200).IsRequired();
        entity.HasIndex(value => new { value.CompanyId, value.JobWorkOrderId, value.ItemId });
        entity.HasOne<JobWorkOrder>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.JobWorkOrderId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class JobWorkReceiptConfiguration : IEntityTypeConfiguration<JobWorkReceipt>
{
    public void Configure(EntityTypeBuilder<JobWorkReceipt> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "JobWorkReceipts");
        entity.Property(value => value.Reference).HasMaxLength(80).IsRequired();
        entity.Property(value => value.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.Property(value => value.ProcessedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.RejectedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ChargeAmount).HasPrecision(20, 6);
        entity.HasIndex(value => new { value.CompanyId, value.JobWorkOrderId, value.Reference }).IsUnique();
        entity.HasOne<JobWorkOrder>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.JobWorkOrderId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MaintenanceRequestConfiguration : IEntityTypeConfiguration<MaintenanceRequest>
{
    public void Configure(EntityTypeBuilder<MaintenanceRequest> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "MaintenanceRequests");
        entity.Property(value => value.RequestNumber).HasMaxLength(40).IsRequired();
        entity.Property(value => value.Description).HasMaxLength(1000).IsRequired();
        entity.Property(value => value.Priority).HasMaxLength(16).IsRequired();
        entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
        entity.Property(value => value.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.HasIndex(value => new { value.CompanyId, value.RequestNumber }).IsUnique();
        entity.HasOne<Machine>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.MachineId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MaintenanceJobCardConfiguration : IEntityTypeConfiguration<MaintenanceJobCard>
{
    public void Configure(EntityTypeBuilder<MaintenanceJobCard> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "MaintenanceJobCards");
        entity.Property(value => value.CardNumber).HasMaxLength(40).IsRequired();
        entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
        entity.Property(value => value.AssignedTeam).HasMaxLength(160).IsRequired();
        entity.Property(value => value.CompletedBySubject).HasMaxLength(200);
        entity.Property(value => value.ConcurrencyVersion).IsConcurrencyToken();
        entity.HasIndex(value => new { value.CompanyId, value.CardNumber }).IsUnique();
        entity.HasIndex(value => new { value.CompanyId, value.MaintenanceRequestId }).IsUnique();
        entity.HasOne<MaintenanceRequest>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.MaintenanceRequestId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PreventiveMaintenanceScheduleConfiguration : IEntityTypeConfiguration<PreventiveMaintenanceSchedule>
{
    public void Configure(EntityTypeBuilder<PreventiveMaintenanceSchedule> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "PreventiveMaintenanceSchedules");
        entity.Property(value => value.Name).HasMaxLength(160).IsRequired();
        entity.HasIndex(value => new { value.CompanyId, value.MachineId, value.Name }).IsUnique();
        entity.HasOne<Machine>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.MachineId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MaintenanceSpareTransactionConfiguration : IEntityTypeConfiguration<MaintenanceSpareTransaction>
{
    public void Configure(EntityTypeBuilder<MaintenanceSpareTransaction> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "MaintenanceSpareTransactions");
        entity.Property(value => value.Quantity).HasPrecision(20, 6);
        entity.Property(value => value.ActorSubject).HasMaxLength(200).IsRequired();
        entity.HasOne<MaintenanceJobCard>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.MaintenanceJobCardId })
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

internal sealed class MachineDowntimeConfiguration : IEntityTypeConfiguration<MachineDowntime>
{
    public void Configure(EntityTypeBuilder<MachineDowntime> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "MachineDowntimes");
        entity.Property(value => value.Reason).HasMaxLength(500).IsRequired();
        entity.Property(value => value.RecordedBySubject).HasMaxLength(200).IsRequired();
        entity.HasIndex(value => new { value.CompanyId, value.MachineId, value.StartedAt });
        entity.Property(value => value.ConcurrencyVersion).IsConcurrencyToken();
        entity.HasIndex(value => new { value.CompanyId, value.MachineId })
            .IsUnique().HasFilter("\"EndedAt\" IS NULL");
        entity.HasOne<Machine>().WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.MachineId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
