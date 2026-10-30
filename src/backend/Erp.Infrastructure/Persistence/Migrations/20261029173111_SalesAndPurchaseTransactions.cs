using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesAndPurchaseTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProductVariants_CompanyId_ProductId_Id",
                table: "ProductVariants",
                columns: new[] { "CompanyId", "ProductId", "Id" });

            migrationBuilder.CreateTable(
                name: "BusinessTransactionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessTransactionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessTransactionEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessTransactionEvents_UserAccounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CurrentRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseOrders_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PurchaseOrders_CurrentRevision", " \"CurrentRevisionNumber\" > 0 ");
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequisitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequisitions", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseRequisitions_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PurchaseRequisitions_ConcurrencyVersion", " \"ConcurrencyVersion\" >= 0 ");
                    table.CheckConstraint("CK_PurchaseRequisitions_Status", " \"Status\" IN ('draft', 'submitted', 'approved', 'rejected') ");
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerPurchaseOrderReference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CurrentRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.Id);
                    table.UniqueConstraint("AK_SalesOrders_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SalesOrders_CurrentRevision", " \"CurrentRevisionNumber\" > 0 ");
                    table.CheckConstraint("CK_SalesOrders_PurchaseOrderReference", "length(trim(\"CustomerPurchaseOrderReference\")) > 0");
                    table.ForeignKey(
                        name: "FK_SalesOrders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Customers_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionIdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionIdempotencyRecords", x => x.Id);
                    table.UniqueConstraint("AK_TransactionIdempotencyRecords_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_TransactionIdempotencyRecords_StatusCode", " \"StatusCode\" IS NULL OR \"StatusCode\" BETWEEN 100 AND 599 ");
                    table.ForeignKey(
                        name: "FK_TransactionIdempotencyRecords_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderRevisions", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseOrderRevisions_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PurchaseOrderRevisions_ConcurrencyVersion", " \"ConcurrencyVersion\" >= 0 ");
                    table.CheckConstraint("CK_PurchaseOrderRevisions_Currency", "length(\"CurrencyCode\") = 3 AND \"CurrencyCode\" = upper(\"CurrencyCode\")");
                    table.CheckConstraint("CK_PurchaseOrderRevisions_Number", " \"RevisionNumber\" > 0 ");
                    table.CheckConstraint("CK_PurchaseOrderRevisions_Status", " \"Status\" IN ('draft', 'submitted', 'approved', 'rejected', 'issued', 'partially_received', 'received') ");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderRevisions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderRevisions_PurchaseOrders_CompanyId_PurchaseOrd~",
                        columns: x => new { x.CompanyId, x.PurchaseOrderId },
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderRevisions_Suppliers_CompanyId_SupplierId",
                        columns: x => new { x.CompanyId, x.SupplierId },
                        principalTable: "Suppliers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequisitionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseRequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitOfMeasureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    RequiredByDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequisitionLines", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseRequisitionLines_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PurchaseRequisitionLines_Quantity", " CAST(\"Quantity\" AS NUMERIC) > 0 ");
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionLines_Items_CompanyId_ItemId",
                        columns: x => new { x.CompanyId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionLines_PurchaseRequisitions_CompanyId_Pur~",
                        columns: x => new { x.CompanyId, x.PurchaseRequisitionId },
                        principalTable: "PurchaseRequisitions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionLines_UnitsOfMeasure_CompanyId_UnitOfMea~",
                        columns: x => new { x.CompanyId, x.UnitOfMeasureId },
                        principalTable: "UnitsOfMeasure",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerPurchaseOrderReference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CustomerPurchaseOrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderRevisions", x => x.Id);
                    table.UniqueConstraint("AK_SalesOrderRevisions_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SalesOrderRevisions_ConcurrencyVersion", " \"ConcurrencyVersion\" >= 0 ");
                    table.CheckConstraint("CK_SalesOrderRevisions_Currency", "length(\"CurrencyCode\") = 3 AND \"CurrencyCode\" = upper(\"CurrencyCode\")");
                    table.CheckConstraint("CK_SalesOrderRevisions_Number", " \"RevisionNumber\" > 0 ");
                    table.CheckConstraint("CK_SalesOrderRevisions_Status", " \"Status\" IN ('draft', 'submitted', 'approved', 'rejected', 'confirmed') ");
                    table.ForeignKey(
                        name: "FK_SalesOrderRevisions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderRevisions_Customers_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderRevisions_SalesOrders_CompanyId_SalesOrderId",
                        columns: x => new { x.CompanyId, x.SalesOrderId },
                        principalTable: "SalesOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PurchaseOrderRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierDeliveryReference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceipts", x => x.Id);
                    table.UniqueConstraint("AK_GoodsReceipts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_PurchaseOrderRevisions_CompanyId_PurchaseOrde~",
                        columns: x => new { x.CompanyId, x.PurchaseOrderRevisionId },
                        principalTable: "PurchaseOrderRevisions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseRequisitionLineId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseOrderLines_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_CompanyId_PurchaseOrderId",
                        columns: x => new { x.CompanyId, x.PurchaseOrderId },
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseRequisitionLines_CompanyId_Purch~",
                        columns: x => new { x.CompanyId, x.PurchaseRequisitionLineId },
                        principalTable: "PurchaseRequisitionLines",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    RequestedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderLines", x => x.Id);
                    table.UniqueConstraint("AK_SalesOrderLines_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SalesOrderLines_Quantity", " CAST(\"Quantity\" AS NUMERIC) > 0 ");
                    table.CheckConstraint("CK_SalesOrderLines_UnitPrice", " CAST(\"UnitPrice\" AS NUMERIC) >= 0 ");
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_ProductVariants_CompanyId_ProductId_Product~",
                        columns: x => new { x.CompanyId, x.ProductId, x.ProductVariantId },
                        principalTable: "ProductVariants",
                        principalColumns: new[] { "CompanyId", "ProductId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_Products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_SalesOrderRevisions_CompanyId_SalesOrderRev~",
                        columns: x => new { x.CompanyId, x.SalesOrderRevisionId },
                        principalTable: "SalesOrderRevisions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceiptLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoodsReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptLines", x => x.Id);
                    table.UniqueConstraint("AK_GoodsReceiptLines_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_GoodsReceiptLines_Quantity", " CAST(\"Quantity\" AS NUMERIC) > 0 ");
                    table.ForeignKey(
                        name: "FK_GoodsReceiptLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptLines_GoodsReceipts_CompanyId_GoodsReceiptId",
                        columns: x => new { x.CompanyId, x.GoodsReceiptId },
                        principalTable: "GoodsReceipts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptLines_PurchaseOrderLines_CompanyId_PurchaseOrde~",
                        columns: x => new { x.CompanyId, x.PurchaseOrderLineId },
                        principalTable: "PurchaseOrderLines",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLineRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitOfMeasureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    ExpectedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLineRevisions", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseOrderLineRevisions_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PurchaseOrderLineRevisions_Quantity", " CAST(\"Quantity\" AS NUMERIC) > 0 ");
                    table.CheckConstraint("CK_PurchaseOrderLineRevisions_UnitPrice", " CAST(\"UnitPrice\" AS NUMERIC) >= 0 ");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLineRevisions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLineRevisions_Items_CompanyId_ItemId",
                        columns: x => new { x.CompanyId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLineRevisions_PurchaseOrderLines_CompanyId_Pur~",
                        columns: x => new { x.CompanyId, x.PurchaseOrderLineId },
                        principalTable: "PurchaseOrderLines",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLineRevisions_PurchaseOrderRevisions_CompanyId~",
                        columns: x => new { x.CompanyId, x.PurchaseOrderRevisionId },
                        principalTable: "PurchaseOrderRevisions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLineRevisions_UnitsOfMeasure_CompanyId_UnitOfM~",
                        columns: x => new { x.CompanyId, x.UnitOfMeasureId },
                        principalTable: "UnitsOfMeasure",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderFulfillments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    FulfilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderFulfillments", x => x.Id);
                    table.UniqueConstraint("AK_SalesOrderFulfillments_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SalesOrderFulfillments_Quantity", " CAST(\"Quantity\" AS NUMERIC) > 0 ");
                    table.ForeignKey(
                        name: "FK_SalesOrderFulfillments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderFulfillments_SalesOrderLines_CompanyId_SalesOrder~",
                        columns: x => new { x.CompanyId, x.SalesOrderLineId },
                        principalTable: "SalesOrderLines",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderFulfillments_SalesOrderRevisions_CompanyId_SalesO~",
                        columns: x => new { x.CompanyId, x.SalesOrderRevisionId },
                        principalTable: "SalesOrderRevisions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessTransactionEvents_ActorUserId",
                table: "BusinessTransactionEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessTransactionEvents_CompanyId_AggregateType_Aggregate~",
                table: "BusinessTransactionEvents",
                columns: new[] { "CompanyId", "AggregateType", "AggregateId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLines_CompanyId_GoodsReceiptId_PurchaseOrderLin~",
                table: "GoodsReceiptLines",
                columns: new[] { "CompanyId", "GoodsReceiptId", "PurchaseOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLines_CompanyId_PurchaseOrderLineId",
                table: "GoodsReceiptLines",
                columns: new[] { "CompanyId", "PurchaseOrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_CompanyId_PurchaseOrderRevisionId",
                table: "GoodsReceipts",
                columns: new[] { "CompanyId", "PurchaseOrderRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_CompanyId_ReceiptNumber",
                table: "GoodsReceipts",
                columns: new[] { "CompanyId", "ReceiptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLineRevisions_CompanyId_ItemId",
                table: "PurchaseOrderLineRevisions",
                columns: new[] { "CompanyId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLineRevisions_CompanyId_PurchaseOrderLineId",
                table: "PurchaseOrderLineRevisions",
                columns: new[] { "CompanyId", "PurchaseOrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLineRevisions_CompanyId_PurchaseOrderRevisionI~",
                table: "PurchaseOrderLineRevisions",
                columns: new[] { "CompanyId", "PurchaseOrderRevisionId", "PurchaseOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLineRevisions_CompanyId_UnitOfMeasureId",
                table: "PurchaseOrderLineRevisions",
                columns: new[] { "CompanyId", "UnitOfMeasureId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_CompanyId_PurchaseOrderId",
                table: "PurchaseOrderLines",
                columns: new[] { "CompanyId", "PurchaseOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_CompanyId_PurchaseRequisitionLineId",
                table: "PurchaseOrderLines",
                columns: new[] { "CompanyId", "PurchaseRequisitionLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderRevisions_CompanyId_PurchaseOrderId_RevisionNu~",
                table: "PurchaseOrderRevisions",
                columns: new[] { "CompanyId", "PurchaseOrderId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderRevisions_CompanyId_SupplierId",
                table: "PurchaseOrderRevisions",
                columns: new[] { "CompanyId", "SupplierId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CompanyId_OrderNumber",
                table: "PurchaseOrders",
                columns: new[] { "CompanyId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_CompanyId_ItemId",
                table: "PurchaseRequisitionLines",
                columns: new[] { "CompanyId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_CompanyId_PurchaseRequisitionId",
                table: "PurchaseRequisitionLines",
                columns: new[] { "CompanyId", "PurchaseRequisitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_CompanyId_UnitOfMeasureId",
                table: "PurchaseRequisitionLines",
                columns: new[] { "CompanyId", "UnitOfMeasureId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitions_CompanyId_RequisitionNumber",
                table: "PurchaseRequisitions",
                columns: new[] { "CompanyId", "RequisitionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderFulfillments_CompanyId_Reference_SalesOrderLineId",
                table: "SalesOrderFulfillments",
                columns: new[] { "CompanyId", "Reference", "SalesOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderFulfillments_CompanyId_SalesOrderLineId",
                table: "SalesOrderFulfillments",
                columns: new[] { "CompanyId", "SalesOrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderFulfillments_CompanyId_SalesOrderRevisionId",
                table: "SalesOrderFulfillments",
                columns: new[] { "CompanyId", "SalesOrderRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_CompanyId_ProductId_ProductVariantId",
                table: "SalesOrderLines",
                columns: new[] { "CompanyId", "ProductId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_CompanyId_SalesOrderRevisionId",
                table: "SalesOrderLines",
                columns: new[] { "CompanyId", "SalesOrderRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderRevisions_CompanyId_CustomerId",
                table: "SalesOrderRevisions",
                columns: new[] { "CompanyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderRevisions_CompanyId_SalesOrderId_RevisionNumber",
                table: "SalesOrderRevisions",
                columns: new[] { "CompanyId", "SalesOrderId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CompanyId_CustomerId_CustomerPurchaseOrderRefer~",
                table: "SalesOrders",
                columns: new[] { "CompanyId", "CustomerId", "CustomerPurchaseOrderReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CompanyId_OrderNumber",
                table: "SalesOrders",
                columns: new[] { "CompanyId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionIdempotencyRecords_CompanyId_Key",
                table: "TransactionIdempotencyRecords",
                columns: new[] { "CompanyId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessTransactionEvents");

            migrationBuilder.DropTable(
                name: "GoodsReceiptLines");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLineRevisions");

            migrationBuilder.DropTable(
                name: "SalesOrderFulfillments");

            migrationBuilder.DropTable(
                name: "TransactionIdempotencyRecords");

            migrationBuilder.DropTable(
                name: "GoodsReceipts");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "SalesOrderLines");

            migrationBuilder.DropTable(
                name: "PurchaseOrderRevisions");

            migrationBuilder.DropTable(
                name: "PurchaseRequisitionLines");

            migrationBuilder.DropTable(
                name: "SalesOrderRevisions");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "PurchaseRequisitions");

            migrationBuilder.DropTable(
                name: "SalesOrders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProductVariants_CompanyId_ProductId_Id",
                table: "ProductVariants");
        }
    }
}
