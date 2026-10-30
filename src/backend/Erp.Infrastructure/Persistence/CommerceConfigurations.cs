using Erp.Domain.Commerce;
using Erp.Domain.MasterData;
using Erp.Domain.Platform;
using Erp.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Infrastructure.Persistence;

internal sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "SalesOrders");
        entity.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
        entity.Property(x => x.CustomerPurchaseOrderReference).HasMaxLength(80).IsRequired();
        entity.Property(x => x.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.OrderNumber }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.CustomerId, x.CustomerPurchaseOrderReference })
            .IsUnique();
        entity.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CustomerId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_SalesOrders_CurrentRevision",
                """ "CurrentRevisionNumber" > 0 """);
            table.HasCheckConstraint(
                "CK_SalesOrders_PurchaseOrderReference",
                """length(trim("CustomerPurchaseOrderReference")) > 0""");
        });
    }
}

internal sealed class SalesOrderRevisionConfiguration : IEntityTypeConfiguration<SalesOrderRevision>
{
    public void Configure(EntityTypeBuilder<SalesOrderRevision> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "SalesOrderRevisions");
        entity.Property(x => x.CustomerPurchaseOrderReference).HasMaxLength(80).IsRequired();
        entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
        entity.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        entity.Property(x => x.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.Property(x => x.SubmittedBySubject).HasMaxLength(200);
        entity.Property(x => x.ApprovedBySubject).HasMaxLength(200);
        entity.Property(x => x.RejectionReason).HasMaxLength(500);
        entity.HasIndex(x => new { x.CompanyId, x.SalesOrderId, x.RevisionNumber }).IsUnique();
        entity.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SalesOrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CustomerId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_SalesOrderRevisions_Number",
                """ "RevisionNumber" > 0 """);
            table.HasCheckConstraint(
                "CK_SalesOrderRevisions_ConcurrencyVersion",
                """ "ConcurrencyVersion" >= 0 """);
            table.HasCheckConstraint(
                "CK_SalesOrderRevisions_Status",
                """ "Status" IN ('draft', 'submitted', 'approved', 'rejected', 'confirmed') """);
            table.HasCheckConstraint(
                "CK_SalesOrderRevisions_Currency",
                """length("CurrencyCode") = 3 AND "CurrencyCode" = upper("CurrencyCode")""");
        });
    }
}

internal sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "SalesOrderLines");
        entity.Property(x => x.Quantity).HasPrecision(20, 6);
        entity.Property(x => x.UnitPrice).HasPrecision(20, 6);
        entity.HasIndex(x => new { x.CompanyId, x.SalesOrderRevisionId });
        entity.HasOne<SalesOrderRevision>()
            .WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.CompanyId, x.SalesOrderRevisionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ProductId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ProductId, x.ProductVariantId })
            .HasPrincipalKey(x => new { x.CompanyId, x.ProductId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint("CK_SalesOrderLines_Quantity", """ CAST("Quantity" AS NUMERIC) > 0 """);
            table.HasCheckConstraint("CK_SalesOrderLines_UnitPrice", """ CAST("UnitPrice" AS NUMERIC) >= 0 """);
        });
    }
}

internal sealed class SalesOrderFulfillmentConfiguration : IEntityTypeConfiguration<SalesOrderFulfillment>
{
    public void Configure(EntityTypeBuilder<SalesOrderFulfillment> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "SalesOrderFulfillments");
        entity.Property(x => x.Reference).HasMaxLength(80).IsRequired();
        entity.Property(x => x.Quantity).HasPrecision(20, 6);
        entity.Property(x => x.ActorSubject).HasMaxLength(200).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.SalesOrderLineId });
        entity.HasIndex(x => new { x.CompanyId, x.Reference, x.SalesOrderLineId }).IsUnique();
        entity.HasOne<SalesOrderRevision>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SalesOrderRevisionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<SalesOrderLine>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SalesOrderLineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
            table.HasCheckConstraint(
                "CK_SalesOrderFulfillments_Quantity",
                """ CAST("Quantity" AS NUMERIC) > 0 """));
    }
}

internal sealed class PurchaseRequisitionConfiguration : IEntityTypeConfiguration<PurchaseRequisition>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisition> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "PurchaseRequisitions");
        entity.Property(x => x.RequisitionNumber).HasMaxLength(40).IsRequired();
        entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
        entity.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        entity.Property(x => x.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.Property(x => x.SubmittedBySubject).HasMaxLength(200);
        entity.Property(x => x.ApprovedBySubject).HasMaxLength(200);
        entity.Property(x => x.RejectionReason).HasMaxLength(500);
        entity.HasIndex(x => new { x.CompanyId, x.RequisitionNumber }).IsUnique();
        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_PurchaseRequisitions_Status",
                """ "Status" IN ('draft', 'submitted', 'approved', 'rejected') """);
            table.HasCheckConstraint(
                "CK_PurchaseRequisitions_ConcurrencyVersion",
                """ "ConcurrencyVersion" >= 0 """);
        });
    }
}

internal sealed class PurchaseRequisitionLineConfiguration : IEntityTypeConfiguration<PurchaseRequisitionLine>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisitionLine> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "PurchaseRequisitionLines");
        entity.Property(x => x.Quantity).HasPrecision(20, 6);
        entity.HasIndex(x => new { x.CompanyId, x.PurchaseRequisitionId });
        entity.HasOne<PurchaseRequisition>()
            .WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.CompanyId, x.PurchaseRequisitionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Item>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ItemId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.UnitOfMeasureId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
            table.HasCheckConstraint(
                "CK_PurchaseRequisitionLines_Quantity",
                """ CAST("Quantity" AS NUMERIC) > 0 """));
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "PurchaseOrders");
        entity.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
        entity.Property(x => x.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.OrderNumber }).IsUnique();
        entity.ToTable(table =>
            table.HasCheckConstraint("CK_PurchaseOrders_CurrentRevision", """ "CurrentRevisionNumber" > 0 """));
    }
}

internal sealed class PurchaseOrderRevisionConfiguration : IEntityTypeConfiguration<PurchaseOrderRevision>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderRevision> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "PurchaseOrderRevisions");
        entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
        entity.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        entity.Property(x => x.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.Property(x => x.SubmittedBySubject).HasMaxLength(200);
        entity.Property(x => x.ApprovedBySubject).HasMaxLength(200);
        entity.Property(x => x.RejectionReason).HasMaxLength(500);
        entity.HasIndex(x => new { x.CompanyId, x.PurchaseOrderId, x.RevisionNumber }).IsUnique();
        entity.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PurchaseOrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SupplierId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PurchaseRequisition>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PurchaseRequisitionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrderRevisions_Number", """ "RevisionNumber" > 0 """);
            table.HasCheckConstraint(
                "CK_PurchaseOrderRevisions_ConcurrencyVersion",
                """ "ConcurrencyVersion" >= 0 """);
            table.HasCheckConstraint(
                "CK_PurchaseOrderRevisions_Status",
                """ "Status" IN ('draft', 'submitted', 'approved', 'rejected', 'issued', 'partially_received', 'received') """);
            table.HasCheckConstraint(
                "CK_PurchaseOrderRevisions_Currency",
                """length("CurrencyCode") = 3 AND "CurrencyCode" = upper("CurrencyCode")""");
        });
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "PurchaseOrderLines");
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PurchaseOrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PurchaseRequisitionLine>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PurchaseRequisitionLineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.CompanyId, x.PurchaseOrderId });
    }
}

internal sealed class PurchaseOrderLineRevisionConfiguration : IEntityTypeConfiguration<PurchaseOrderLineRevision>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLineRevision> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "PurchaseOrderLineRevisions");
        entity.Property(x => x.Quantity).HasPrecision(20, 6);
        entity.Property(x => x.UnitPrice).HasPrecision(20, 6);
        entity.HasIndex(x => new { x.CompanyId, x.PurchaseOrderRevisionId, x.PurchaseOrderLineId }).IsUnique();
        entity.HasOne<PurchaseOrderRevision>()
            .WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.CompanyId, x.PurchaseOrderRevisionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PurchaseOrderLine>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PurchaseOrderLineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Item>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ItemId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.UnitOfMeasureId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_PurchaseOrderLineRevisions_Quantity",
                """ CAST("Quantity" AS NUMERIC) > 0 """);
            table.HasCheckConstraint(
                "CK_PurchaseOrderLineRevisions_UnitPrice",
                """ CAST("UnitPrice" AS NUMERIC) >= 0 """);
        });
    }
}

internal sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "GoodsReceipts");
        entity.Property(x => x.ReceiptNumber).HasMaxLength(40).IsRequired();
        entity.Property(x => x.SupplierDeliveryReference).HasMaxLength(80).IsRequired();
        entity.Property(x => x.CreatedBySubject).HasMaxLength(200).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.ReceiptNumber }).IsUnique();
        entity.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.WarehouseId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PurchaseOrderRevision>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PurchaseOrderRevisionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "GoodsReceiptLines");
        entity.Property(x => x.Quantity).HasPrecision(20, 6);
        entity.HasIndex(x => new { x.CompanyId, x.GoodsReceiptId, x.PurchaseOrderLineId }).IsUnique();
        entity.HasOne<GoodsReceipt>()
            .WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.CompanyId, x.GoodsReceiptId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PurchaseOrderLine>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PurchaseOrderLineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
            table.HasCheckConstraint("CK_GoodsReceiptLines_Quantity", """ CAST("Quantity" AS NUMERIC) > 0 """));
    }
}

internal sealed class BusinessTransactionEventConfiguration : IEntityTypeConfiguration<BusinessTransactionEvent>
{
    public void Configure(EntityTypeBuilder<BusinessTransactionEvent> entity)
    {
        entity.HasKey(x => x.Id);
        entity.Property(x => x.AggregateType).HasMaxLength(60).IsRequired();
        entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
        entity.Property(x => x.ActorSubject).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(500);
        entity.Property(x => x.DetailsJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(100);
        entity.HasIndex(x => new { x.CompanyId, x.AggregateType, x.AggregateId, x.OccurredAt });
        entity.HasOne<Company>()
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable("BusinessTransactionEvents");
    }
}

internal sealed class TransactionIdempotencyRecordConfiguration : IEntityTypeConfiguration<TransactionIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<TransactionIdempotencyRecord> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "TransactionIdempotencyRecords");
        entity.Property(x => x.Key).HasMaxLength(100).IsRequired();
        entity.Property(x => x.Operation).HasMaxLength(100).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ResponseJson).HasColumnType("jsonb");
        entity.Property(x => x.StatusCode);
        entity.Property(x => x.Location).HasMaxLength(300);
        entity.HasIndex(x => new { x.CompanyId, x.ActorUserId, x.Key }).IsUnique();
        entity.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
            table.HasCheckConstraint(
                "CK_TransactionIdempotencyRecords_StatusCode",
                """ "StatusCode" IS NULL OR "StatusCode" BETWEEN 100 AND 599 """));
    }
}
