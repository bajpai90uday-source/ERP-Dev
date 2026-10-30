using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManufacturingPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "GoodsReceipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryLedgerEntries", x => x.Id);
                    table.UniqueConstraint("AK_InventoryLedgerEntries_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_InventoryLedgerEntries_Quantity", " CAST(\"Quantity\" AS NUMERIC) <> 0 ");
                    table.CheckConstraint("CK_InventoryLedgerEntries_StockStatus", " \"StockStatus\" IN ('available', 'hold', 'quarantine') ");
                    table.CheckConstraint("CK_InventoryLedgerEntries_TransactionType", " \"TransactionType\" IN ('receipt', 'issue', 'adjustment') ");
                    table.ForeignKey(
                        name: "FK_InventoryLedgerEntries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLedgerEntries_Items_CompanyId_ItemId",
                        columns: x => new { x.CompanyId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLedgerEntries_Warehouses_CompanyId_WarehouseId",
                        columns: x => new { x.CompanyId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryStockBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OnHandQuantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryStockBalances", x => x.Id);
                    table.UniqueConstraint("AK_InventoryStockBalances_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_InventoryStockBalances_ConcurrencyVersion", " \"ConcurrencyVersion\" >= 0 ");
                    table.CheckConstraint("CK_InventoryStockBalances_OnHand", " CAST(\"OnHandQuantity\" AS NUMERIC) >= 0 ");
                    table.CheckConstraint("CK_InventoryStockBalances_Reserved", " CAST(\"ReservedQuantity\" AS NUMERIC) >= 0 AND \"ReservedQuantity\" <= \"OnHandQuantity\" ");
                    table.CheckConstraint("CK_InventoryStockBalances_StockStatus", " \"StockStatus\" IN ('available', 'hold', 'quarantine') ");
                    table.ForeignKey(
                        name: "FK_InventoryStockBalances_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryStockBalances_Items_CompanyId_ItemId",
                        columns: x => new { x.CompanyId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryStockBalances_Warehouses_CompanyId_WarehouseId",
                        columns: x => new { x.CompanyId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SalesOrderRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BomVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutputWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    BomVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    PlannedStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.UniqueConstraint("AK_WorkOrders_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkOrders_ConcurrencyVersion", " \"ConcurrencyVersion\" >= 0 ");
                    table.CheckConstraint("CK_WorkOrders_PlannedDates", " \"PlannedEndDate\" >= \"PlannedStartDate\" ");
                    table.CheckConstraint("CK_WorkOrders_PlannedQuantity", " CAST(\"PlannedQuantity\" AS NUMERIC) > 0 ");
                    table.CheckConstraint("CK_WorkOrders_Status", " \"Status\" IN ('draft', 'submitted', 'approved', 'rejected', 'released', 'on_hold', 'completed', 'cancelled') ");
                    table.ForeignKey(
                        name: "FK_WorkOrders_BomVersions_CompanyId_BomVersionId",
                        columns: x => new { x.CompanyId, x.BomVersionId },
                        principalTable: "BomVersions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_SalesOrderLines_CompanyId_SalesOrderLineId",
                        columns: x => new { x.CompanyId, x.SalesOrderLineId },
                        principalTable: "SalesOrderLines",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_SalesOrderRevisions_CompanyId_SalesOrderRevision~",
                        columns: x => new { x.CompanyId, x.SalesOrderRevisionId },
                        principalTable: "SalesOrderRevisions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Warehouses_CompanyId_OutputWarehouseId",
                        columns: x => new { x.CompanyId, x.OutputWarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderMaterialRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    BomVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BomComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitOfMeasureId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityPerFinishedUnit = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: false),
                    WastePercent = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    RequiredQuantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderMaterialRequirements", x => x.Id);
                    table.UniqueConstraint("AK_WorkOrderMaterialRequirements_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkOrderMaterialRequirements_QuantityPerUnit", " CAST(\"QuantityPerFinishedUnit\" AS NUMERIC) > 0 ");
                    table.CheckConstraint("CK_WorkOrderMaterialRequirements_RequiredQuantity", " CAST(\"RequiredQuantity\" AS NUMERIC) > 0 ");
                    table.CheckConstraint("CK_WorkOrderMaterialRequirements_Waste", " CAST(\"WastePercent\" AS NUMERIC) >= 0 AND CAST(\"WastePercent\" AS NUMERIC) <= 100 ");
                    table.ForeignKey(
                        name: "FK_WorkOrderMaterialRequirements_BomComponents_CompanyId_BomCo~",
                        columns: x => new { x.CompanyId, x.BomComponentId },
                        principalTable: "BomComponents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderMaterialRequirements_BomVersions_CompanyId_BomVers~",
                        columns: x => new { x.CompanyId, x.BomVersionId },
                        principalTable: "BomVersions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderMaterialRequirements_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderMaterialRequirements_Items_CompanyId_ItemId",
                        columns: x => new { x.CompanyId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderMaterialRequirements_UnitsOfMeasure_CompanyId_Unit~",
                        columns: x => new { x.CompanyId, x.UnitOfMeasureId },
                        principalTable: "UnitsOfMeasure",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderMaterialRequirements_WorkOrders_CompanyId_WorkOrde~",
                        columns: x => new { x.CompanyId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    OperationCodeSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OperationNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PlannedStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderOperations", x => x.Id);
                    table.UniqueConstraint("AK_WorkOrderOperations_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkOrderOperations_PlannedDates", " \"PlannedEndDate\" >= \"PlannedStartDate\" ");
                    table.CheckConstraint("CK_WorkOrderOperations_Sequence", " \"Sequence\" > 0 ");
                    table.CheckConstraint("CK_WorkOrderOperations_Status", " \"Status\" IN ('pending', 'completed') ");
                    table.ForeignKey(
                        name: "FK_WorkOrderOperations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderOperations_ProductionOperations_CompanyId_Producti~",
                        columns: x => new { x.CompanyId, x.ProductionOperationId },
                        principalTable: "ProductionOperations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderOperations_WorkOrders_CompanyId_WorkOrderId",
                        columns: x => new { x.CompanyId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderTransactionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderTransactionEvents", x => x.Id);
                    table.UniqueConstraint("AK_WorkOrderTransactionEvents_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_WorkOrderTransactionEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderTransactionEvents_WorkOrders_CompanyId_WorkOrderId",
                        columns: x => new { x.CompanyId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderMaterialRequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReservations", x => x.Id);
                    table.UniqueConstraint("AK_InventoryReservations_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_InventoryReservations_Quantity", " CAST(\"Quantity\" AS NUMERIC) > 0 ");
                    table.CheckConstraint("CK_InventoryReservations_Status", " \"Status\" IN ('active', 'released', 'consumed') ");
                    table.ForeignKey(
                        name: "FK_InventoryReservations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservations_Warehouses_CompanyId_WarehouseId",
                        columns: x => new { x.CompanyId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservations_WorkOrderMaterialRequirements_Company~",
                        columns: x => new { x.CompanyId, x.WorkOrderMaterialRequirementId },
                        principalTable: "WorkOrderMaterialRequirements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_CompanyId_WarehouseId",
                table: "GoodsReceipts",
                columns: new[] { "CompanyId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLedgerEntries_CompanyId_ItemId_WarehouseId_StockSt~",
                table: "InventoryLedgerEntries",
                columns: new[] { "CompanyId", "ItemId", "WarehouseId", "StockStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLedgerEntries_CompanyId_ReferenceType_ReferenceId_~",
                table: "InventoryLedgerEntries",
                columns: new[] { "CompanyId", "ReferenceType", "ReferenceId", "ReferenceLineId", "TransactionType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLedgerEntries_CompanyId_WarehouseId",
                table: "InventoryLedgerEntries",
                columns: new[] { "CompanyId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_CompanyId_WarehouseId_Status",
                table: "InventoryReservations",
                columns: new[] { "CompanyId", "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_CompanyId_WorkOrderMaterialRequiremen~",
                table: "InventoryReservations",
                columns: new[] { "CompanyId", "WorkOrderMaterialRequirementId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStockBalances_CompanyId_ItemId_WarehouseId_StockSt~",
                table: "InventoryStockBalances",
                columns: new[] { "CompanyId", "ItemId", "WarehouseId", "StockStatus" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStockBalances_CompanyId_WarehouseId",
                table: "InventoryStockBalances",
                columns: new[] { "CompanyId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderMaterialRequirements_CompanyId_BomComponentId",
                table: "WorkOrderMaterialRequirements",
                columns: new[] { "CompanyId", "BomComponentId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderMaterialRequirements_CompanyId_BomVersionId",
                table: "WorkOrderMaterialRequirements",
                columns: new[] { "CompanyId", "BomVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderMaterialRequirements_CompanyId_ItemId",
                table: "WorkOrderMaterialRequirements",
                columns: new[] { "CompanyId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderMaterialRequirements_CompanyId_UnitOfMeasureId",
                table: "WorkOrderMaterialRequirements",
                columns: new[] { "CompanyId", "UnitOfMeasureId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderMaterialRequirements_CompanyId_WorkOrderId_ItemId",
                table: "WorkOrderMaterialRequirements",
                columns: new[] { "CompanyId", "WorkOrderId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderOperations_CompanyId_ProductionOperationId",
                table: "WorkOrderOperations",
                columns: new[] { "CompanyId", "ProductionOperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderOperations_CompanyId_WorkOrderId_Sequence",
                table: "WorkOrderOperations",
                columns: new[] { "CompanyId", "WorkOrderId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CompanyId_BomVersionId",
                table: "WorkOrders",
                columns: new[] { "CompanyId", "BomVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CompanyId_OutputWarehouseId",
                table: "WorkOrders",
                columns: new[] { "CompanyId", "OutputWarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CompanyId_SalesOrderLineId_Status",
                table: "WorkOrders",
                columns: new[] { "CompanyId", "SalesOrderLineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CompanyId_SalesOrderRevisionId",
                table: "WorkOrders",
                columns: new[] { "CompanyId", "SalesOrderRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CompanyId_WorkOrderNumber",
                table: "WorkOrders",
                columns: new[] { "CompanyId", "WorkOrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderTransactionEvents_CompanyId_WorkOrderId_OccurredAt",
                table: "WorkOrderTransactionEvents",
                columns: new[] { "CompanyId", "WorkOrderId", "OccurredAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceipts_Warehouses_CompanyId_WarehouseId",
                table: "GoodsReceipts",
                columns: new[] { "CompanyId", "WarehouseId" },
                principalTable: "Warehouses",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceipts_Warehouses_CompanyId_WarehouseId",
                table: "GoodsReceipts");

            migrationBuilder.DropTable(
                name: "InventoryLedgerEntries");

            migrationBuilder.DropTable(
                name: "InventoryReservations");

            migrationBuilder.DropTable(
                name: "InventoryStockBalances");

            migrationBuilder.DropTable(
                name: "WorkOrderOperations");

            migrationBuilder.DropTable(
                name: "WorkOrderTransactionEvents");

            migrationBuilder.DropTable(
                name: "WorkOrderMaterialRequirements");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceipts_CompanyId_WarehouseId",
                table: "GoodsReceipts");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "GoodsReceipts");
        }
    }
}
