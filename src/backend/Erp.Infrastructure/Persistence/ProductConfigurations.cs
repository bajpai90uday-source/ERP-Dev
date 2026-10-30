using Erp.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Infrastructure.Persistence;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "Products");
        MappingRules.ConfigureCodeAndName(entity);
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.IndustryCode).HasMaxLength(40).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "ProductVariants");
        entity.HasAlternateKey(x => new { x.CompanyId, x.ProductId, x.Id });
        MappingRules.ConfigureCodeAndName(entity);
        entity.Property(x => x.SizeCode).HasMaxLength(40);
        entity.Property(x => x.ColorCode).HasMaxLength(40);
        entity.Property(x => x.ColorName).HasMaxLength(100);
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.ProductId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.ProductId, x.SizeCode, x.ColorCode })
            .IsUnique()
            .HasFilter("\"SizeCode\" IS NOT NULL AND \"ColorCode\" IS NOT NULL");
        entity.HasIndex(x => new { x.CompanyId, x.ItemId }).IsUnique();
        entity.HasOne(x => x.Product)
            .WithMany(x => x.Variants)
            .HasForeignKey(x => new { x.CompanyId, x.ProductId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Erp.Domain.MasterData.Item>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ItemId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductVariantAttributeConfiguration : IEntityTypeConfiguration<ProductVariantAttribute>
{
    public void Configure(EntityTypeBuilder<ProductVariantAttribute> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "ProductVariantAttributes");
        entity.Property(x => x.AttributeCode).HasMaxLength(60).IsRequired();
        entity.Property(x => x.Value).HasMaxLength(160).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.ProductVariantId, x.AttributeCode }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.AttributeCode, x.Value });
        entity.HasOne<ProductVariant>()
            .WithMany(x => x.Attributes)
            .HasForeignKey(x => new { x.CompanyId, x.ProductVariantId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable("ProductVariantAttributes", table =>
        {
            table.HasCheckConstraint(
                "CK_ProductVariantAttributes_AttributeCode_NotBlank",
                """length(trim("AttributeCode")) > 0""");
            table.HasCheckConstraint(
                "CK_ProductVariantAttributes_Value_NotBlank",
                """length(trim("Value")) > 0""");
        });
    }
}

internal sealed class BomConfiguration : IEntityTypeConfiguration<Bom>
{
    public void Configure(EntityTypeBuilder<Bom> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "Boms");
        MappingRules.ConfigureCodeAndName(entity);
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ProductId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BomVersionConfiguration : IEntityTypeConfiguration<BomVersion>
{
    public void Configure(EntityTypeBuilder<BomVersion> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "BomVersions");
        entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
        entity.Property(x => x.ApprovedBySubject).HasMaxLength(200);
        entity.HasIndex(x => new { x.CompanyId, x.BomId, x.VersionNumber }).IsUnique();
        entity.HasOne<Bom>()
            .WithMany(x => x.Versions)
            .HasForeignKey(x => new { x.CompanyId, x.BomId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint("CK_BomVersions_VersionNumber", """ "VersionNumber" > 0 """);
            table.HasCheckConstraint(
                "CK_BomVersions_Status",
                """ "Status" IN ('draft', 'approved', 'retired') """);
            table.HasCheckConstraint(
                "CK_BomVersions_EffectiveDates",
                """ "EffectiveTo" IS NULL OR "EffectiveTo" >= "EffectiveFrom" """);
            table.HasCheckConstraint(
                "CK_BomVersions_Approval",
                """(("Status" = 'draft' AND "ApprovedAt" IS NULL AND "ApprovedBySubject" IS NULL) OR ("Status" IN ('approved', 'retired') AND "ApprovedAt" IS NOT NULL AND length(trim("ApprovedBySubject")) > 0))""");
        });
    }
}

internal sealed class BomComponentConfiguration : IEntityTypeConfiguration<BomComponent>
{
    public void Configure(EntityTypeBuilder<BomComponent> entity)
    {
        MappingRules.ConfigureCompanyScoped(entity, "BomComponents");
        entity.Property(x => x.Quantity).HasPrecision(20, 6);
        entity.Property(x => x.WastePercent).HasPrecision(9, 6);
        entity.Property(x => x.AlternativeGroupCode).HasMaxLength(60);
        entity.HasIndex(x => new { x.BomVersionId, x.LineNumber }).IsUnique();
        entity.HasOne<BomVersion>()
            .WithMany(x => x.Components)
            .HasForeignKey(x => new { x.CompanyId, x.BomVersionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Erp.Domain.MasterData.Item>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ItemId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Erp.Domain.MasterData.UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.UnitOfMeasureId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint("CK_BomComponents_LineNumber", """ "LineNumber" > 0 """);
            table.HasCheckConstraint(
                "CK_BomComponents_Quantity",
                """ CAST("Quantity" AS NUMERIC) > 0 """);
            table.HasCheckConstraint(
                "CK_BomComponents_WastePercent",
                """ CAST("WastePercent" AS NUMERIC) >= 0 AND CAST("WastePercent" AS NUMERIC) <= 100 """);
        });
    }
}
