using Erp.Domain.Commerce;
using Erp.Domain.MasterData;
using Erp.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Infrastructure.Persistence;

internal sealed class InventoryLedgerEntryConfiguration : IEntityTypeConfiguration<InventoryLedgerEntry>
{
    public void Configure(EntityTypeBuilder<InventoryLedgerEntry> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "InventoryLedgerEntries");
        entity.Property(value => value.StockStatus).HasMaxLength(20).IsRequired();
        entity.Property(value => value.TransactionType).HasMaxLength(24).IsRequired();
        entity.Property(value => value.ReferenceType).HasMaxLength(40).IsRequired();
        entity.Property(value => value.ActorSubject).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Quantity).HasPrecision(20, 6);
        entity.HasIndex(value => new { value.CompanyId, value.ItemId, value.WarehouseId, value.StockStatus });
        entity.HasIndex(value => new
        {
            value.CompanyId,
            value.ReferenceType,
            value.ReferenceId,
            value.ReferenceLineId,
            value.TransactionType,
        }).IsUnique();
        entity.HasOne<Item>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.ItemId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.WarehouseId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_InventoryLedgerEntries_Quantity",
                """ CAST("Quantity" AS NUMERIC) <> 0 """);
            table.HasCheckConstraint(
                "CK_InventoryLedgerEntries_StockStatus",
                """ "StockStatus" IN ('available', 'hold', 'quarantine') """);
            table.HasCheckConstraint(
                "CK_InventoryLedgerEntries_TransactionType",
                """ "TransactionType" IN ('receipt', 'issue', 'adjustment') """);
        });
    }
}

internal sealed class InventoryStockBalanceConfiguration :
    IEntityTypeConfiguration<InventoryStockBalance>
{
    public void Configure(EntityTypeBuilder<InventoryStockBalance> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "InventoryStockBalances");
        entity.Property(value => value.StockStatus).HasMaxLength(20).IsRequired();
        entity.Property(value => value.OnHandQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ReservedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ConcurrencyVersion).IsConcurrencyToken();
        entity.HasIndex(value => new
        {
            value.CompanyId,
            value.ItemId,
            value.WarehouseId,
            value.StockStatus,
        }).IsUnique();
        entity.HasOne<Item>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.ItemId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.WarehouseId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_InventoryStockBalances_OnHand",
                """ CAST("OnHandQuantity" AS NUMERIC) >= 0 """);
            table.HasCheckConstraint(
                "CK_InventoryStockBalances_Reserved",
                """ CAST("ReservedQuantity" AS NUMERIC) >= 0 AND "ReservedQuantity" <= "OnHandQuantity" """);
            table.HasCheckConstraint(
                "CK_InventoryStockBalances_StockStatus",
                """ "StockStatus" IN ('available', 'hold', 'quarantine') """);
            table.HasCheckConstraint(
                "CK_InventoryStockBalances_ConcurrencyVersion",
                """ "ConcurrencyVersion" >= 0 """);
        });
    }
}

internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "WorkOrders");
        entity.Property(value => value.WorkOrderNumber).HasMaxLength(40).IsRequired();
        entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
        entity.Property(value => value.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.Property(value => value.SubmittedBySubject).HasMaxLength(200);
        entity.Property(value => value.ApprovedBySubject).HasMaxLength(200);
        entity.Property(value => value.RejectionReason).HasMaxLength(500);
        entity.Property(value => value.PlannedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ConcurrencyVersion).IsConcurrencyToken();
        entity.Property(value => value.Priority).HasDefaultValue(3);
        entity.HasIndex(value => new { value.CompanyId, value.WorkOrderNumber }).IsUnique();
        entity.HasIndex(value => new { value.CompanyId, value.SalesOrderLineId, value.Status });
        entity.HasOne<SalesOrderRevision>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.SalesOrderRevisionId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<SalesOrderLine>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.SalesOrderLineId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<BomVersion>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.BomVersionId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.OutputWarehouseId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_WorkOrders_Status",
                """ "Status" IN ('draft', 'submitted', 'approved', 'rejected', 'released', 'on_hold', 'completed', 'cancelled') """);
            table.HasCheckConstraint(
                "CK_WorkOrders_PlannedQuantity",
                """ CAST("PlannedQuantity" AS NUMERIC) > 0 """);
            table.HasCheckConstraint(
                "CK_WorkOrders_Priority",
                """ "Priority" BETWEEN 1 AND 5 """);
            table.HasCheckConstraint(
                "CK_WorkOrders_PlannedDates",
                """ "PlannedEndDate" >= "PlannedStartDate" """);
            table.HasCheckConstraint(
                "CK_WorkOrders_ConcurrencyVersion",
                """ "ConcurrencyVersion" >= 0 """);
        });
    }
}

internal sealed class WorkOrderMaterialRequirementConfiguration :
    IEntityTypeConfiguration<WorkOrderMaterialRequirement>
{
    public void Configure(EntityTypeBuilder<WorkOrderMaterialRequirement> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "WorkOrderMaterialRequirements");
        entity.Property(value => value.SourcePath).HasMaxLength(1000).IsRequired();
        entity.Property(value => value.QuantityPerFinishedUnit).HasPrecision(20, 10);
        entity.Property(value => value.WastePercent).HasPrecision(9, 6);
        entity.Property(value => value.RequiredQuantity).HasPrecision(20, 6);
        entity.HasIndex(value => new { value.CompanyId, value.WorkOrderId, value.ItemId });
        entity.HasOne<WorkOrder>()
            .WithMany(value => value.Materials)
            .HasForeignKey(value => new { value.CompanyId, value.WorkOrderId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<BomVersion>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.BomVersionId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<BomComponent>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.BomComponentId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Item>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.ItemId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.UnitOfMeasureId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_WorkOrderMaterialRequirements_QuantityPerUnit",
                """ CAST("QuantityPerFinishedUnit" AS NUMERIC) > 0 """);
            table.HasCheckConstraint(
                "CK_WorkOrderMaterialRequirements_RequiredQuantity",
                """ CAST("RequiredQuantity" AS NUMERIC) > 0 """);
            table.HasCheckConstraint(
                "CK_WorkOrderMaterialRequirements_Waste",
                """ CAST("WastePercent" AS NUMERIC) >= 0 AND CAST("WastePercent" AS NUMERIC) <= 100 """);
        });
    }
}

internal sealed class WorkOrderOperationConfiguration : IEntityTypeConfiguration<WorkOrderOperation>
{
    public void Configure(EntityTypeBuilder<WorkOrderOperation> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "WorkOrderOperations");
        entity.Property(value => value.OperationCodeSnapshot).HasMaxLength(40).IsRequired();
        entity.Property(value => value.OperationNameSnapshot).HasMaxLength(160).IsRequired();
        entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
        entity.Property(value => value.CompletedBySubject).HasMaxLength(200);
        entity.Property(value => value.PlannedTargetQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ProductionLine).HasMaxLength(100);
        entity.Property(value => value.AssignedTeam).HasMaxLength(100);
        entity.HasIndex(value => new { value.CompanyId, value.WorkOrderId, value.Sequence }).IsUnique();
        entity.HasOne<WorkOrder>()
            .WithMany(value => value.Operations)
            .HasForeignKey(value => new { value.CompanyId, value.WorkOrderId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ProductionOperation>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.ProductionOperationId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.MachineId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_WorkOrderOperations_Sequence",
                """ "Sequence" > 0 """);
            table.HasCheckConstraint(
                "CK_WorkOrderOperations_Status",
                """ "Status" IN ('pending', 'completed') """);
            table.HasCheckConstraint(
                "CK_WorkOrderOperations_PlannedDates",
                """ "PlannedEndDate" >= "PlannedStartDate" """);
            table.HasCheckConstraint(
                "CK_WorkOrderOperations_PlannedTargetQuantity",
                """ CAST("PlannedTargetQuantity" AS NUMERIC) > 0 """);
        });
    }
}

internal sealed class JobCardConfiguration : IEntityTypeConfiguration<JobCard>
{
    public void Configure(EntityTypeBuilder<JobCard> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "JobCards");
        entity.Property(value => value.JobCardNumber).HasMaxLength(50).IsRequired();
        entity.Property(value => value.PlannedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ProductionLine).HasMaxLength(100);
        entity.Property(value => value.AssignedTeam).HasMaxLength(100);
        entity.Property(value => value.Status).HasMaxLength(24).IsRequired();
        entity.Property(value => value.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.Property(value => value.ConcurrencyVersion).IsConcurrencyToken();
        entity.HasIndex(value => new { value.CompanyId, value.JobCardNumber }).IsUnique();
        entity.HasIndex(value => new { value.CompanyId, value.WorkOrderOperationId }).IsUnique();
        entity.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.WorkOrderId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<WorkOrderOperation>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.WorkOrderOperationId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.MachineId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_JobCards_PlannedQuantity",
                """ CAST("PlannedQuantity" AS NUMERIC) > 0 """);
            table.HasCheckConstraint(
                "CK_JobCards_Status",
                """ "Status" IN ('ready', 'in_progress', 'on_hold', 'partially_completed', 'completed', 'cancelled') """);
            table.HasCheckConstraint(
                "CK_JobCards_ConcurrencyVersion",
                """ "ConcurrencyVersion" >= 0 """);
        });
    }
}

internal sealed class ProductionConfirmationConfiguration :
    IEntityTypeConfiguration<ProductionConfirmation>
{
    public void Configure(EntityTypeBuilder<ProductionConfirmation> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "ProductionConfirmations");
        entity.Property(value => value.GoodQuantity).HasPrecision(20, 6);
        entity.Property(value => value.RejectedQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ReworkQuantity).HasPrecision(20, 6);
        entity.Property(value => value.ScrapQuantity).HasPrecision(20, 6);
        entity.Property(value => value.Shift).HasMaxLength(80);
        entity.Property(value => value.ProductionLine).HasMaxLength(100);
        entity.Property(value => value.Team).HasMaxLength(100);
        entity.Property(value => value.Status).HasMaxLength(24).IsRequired();
        entity.Property(value => value.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.Property(value => value.ApprovedBySubject).HasMaxLength(200);
        entity.Property(value => value.CorrectionReason).HasMaxLength(500);
        entity.Property(value => value.RejectionReason).HasMaxLength(500);
        entity.HasIndex(value => new { value.CompanyId, value.JobCardId, value.CreatedAt });
        entity.HasIndex(value => new { value.CompanyId, value.CorrectsConfirmationId }).IsUnique();
        entity.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.WorkOrderId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<JobCard>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.JobCardId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ProductionConfirmation>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.CorrectsConfirmationId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.MachineId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_ProductionConfirmations_Quantities",
                """ CAST("GoodQuantity" AS NUMERIC) >= 0 AND CAST("RejectedQuantity" AS NUMERIC) >= 0 AND CAST("ReworkQuantity" AS NUMERIC) >= 0 AND CAST("ScrapQuantity" AS NUMERIC) >= 0 AND ("GoodQuantity" + "RejectedQuantity" + "ReworkQuantity" + "ScrapQuantity") > 0 """);
            table.HasCheckConstraint(
                "CK_ProductionConfirmations_Status",
                """ "Status" IN ('pending_approval', 'approved', 'rejected', 'superseded') """);
            table.HasCheckConstraint(
                "CK_ProductionConfirmations_Timestamps",
                """ "CompletedAt" >= "StartedAt" """);
        });
    }
}

internal sealed class InventoryReservationConfiguration :
    IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "InventoryReservations");
        entity.Property(value => value.Quantity).HasPrecision(20, 6);
        entity.Property(value => value.Status).HasMaxLength(16).IsRequired();
        entity.HasIndex(value => new { value.CompanyId, value.WarehouseId, value.Status });
        entity.HasOne<WorkOrderMaterialRequirement>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.WorkOrderMaterialRequirementId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.WarehouseId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_InventoryReservations_Quantity",
                """ CAST("Quantity" AS NUMERIC) > 0 """);
            table.HasCheckConstraint(
                "CK_InventoryReservations_Status",
                """ "Status" IN ('active', 'released', 'consumed') """);
        });
    }
}

internal sealed class WorkOrderTransactionEventConfiguration :
    IEntityTypeConfiguration<WorkOrderTransactionEvent>
{
    public void Configure(EntityTypeBuilder<WorkOrderTransactionEvent> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "WorkOrderTransactionEvents");
        entity.Property(value => value.Action).HasMaxLength(80).IsRequired();
        entity.Property(value => value.ActorSubject).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Reason).HasMaxLength(500);
        entity.Property(value => value.DetailsJson).HasColumnType("jsonb").IsRequired();
        entity.HasIndex(value => new { value.CompanyId, value.WorkOrderId, value.OccurredAt });
        entity.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(value => new { value.CompanyId, value.WorkOrderId })
            .HasPrincipalKey(value => new { value.CompanyId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
