using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "WorkOrders",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "AssignedTeam",
                table: "WorkOrderOperations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MachineId",
                table: "WorkOrderOperations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlannedTargetQuantity",
                table: "WorkOrderOperations",
                type: "numeric(20,6)",
                precision: 20,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductionLine",
                table: "WorkOrderOperations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "WorkOrders" SET "Priority" = 3;
                UPDATE "WorkOrderOperations" AS operation
                SET "PlannedTargetQuantity" = work_order."PlannedQuantity"
                FROM "WorkOrders" AS work_order
                WHERE operation."CompanyId" = work_order."CompanyId"
                    AND operation."WorkOrderId" = work_order."Id";
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "PlannedTargetQuantity",
                table: "WorkOrderOperations",
                type: "numeric(20,6)",
                precision: 20,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,6)",
                oldPrecision: 20,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "JobCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobCardNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    ProductionLine = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MachineId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedTeam = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobCards", x => x.Id);
                    table.UniqueConstraint("AK_JobCards_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_JobCards_ConcurrencyVersion", " \"ConcurrencyVersion\" >= 0 ");
                    table.CheckConstraint("CK_JobCards_PlannedQuantity", " CAST(\"PlannedQuantity\" AS NUMERIC) > 0 ");
                    table.CheckConstraint("CK_JobCards_Status", " \"Status\" IN ('ready', 'in_progress', 'on_hold', 'partially_completed', 'completed', 'cancelled') ");
                    table.ForeignKey(
                        name: "FK_JobCards_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobCards_Machines_CompanyId_MachineId",
                        columns: x => new { x.CompanyId, x.MachineId },
                        principalTable: "Machines",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobCards_WorkOrderOperations_CompanyId_WorkOrderOperationId",
                        columns: x => new { x.CompanyId, x.WorkOrderOperationId },
                        principalTable: "WorkOrderOperations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobCards_WorkOrders_CompanyId_WorkOrderId",
                        columns: x => new { x.CompanyId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionConfirmations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectsConfirmationId = table.Column<Guid>(type: "uuid", nullable: true),
                    GoodQuantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    RejectedQuantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    ReworkQuantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    ScrapQuantity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    Shift = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ProductionLine = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MachineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Team = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedBySubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CorrectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionConfirmations", x => x.Id);
                    table.UniqueConstraint("AK_ProductionConfirmations_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_ProductionConfirmations_Quantities", " CAST(\"GoodQuantity\" AS NUMERIC) >= 0 AND CAST(\"RejectedQuantity\" AS NUMERIC) >= 0 AND CAST(\"ReworkQuantity\" AS NUMERIC) >= 0 AND CAST(\"ScrapQuantity\" AS NUMERIC) >= 0 AND (\"GoodQuantity\" + \"RejectedQuantity\" + \"ReworkQuantity\" + \"ScrapQuantity\") > 0 ");
                    table.CheckConstraint("CK_ProductionConfirmations_Status", " \"Status\" IN ('pending_approval', 'approved', 'rejected', 'superseded') ");
                    table.CheckConstraint("CK_ProductionConfirmations_Timestamps", " \"CompletedAt\" >= \"StartedAt\" ");
                    table.ForeignKey(
                        name: "FK_ProductionConfirmations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionConfirmations_JobCards_CompanyId_JobCardId",
                        columns: x => new { x.CompanyId, x.JobCardId },
                        principalTable: "JobCards",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionConfirmations_Machines_CompanyId_MachineId",
                        columns: x => new { x.CompanyId, x.MachineId },
                        principalTable: "Machines",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionConfirmations_ProductionConfirmations_CompanyId_C~",
                        columns: x => new { x.CompanyId, x.CorrectsConfirmationId },
                        principalTable: "ProductionConfirmations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionConfirmations_WorkOrders_CompanyId_WorkOrderId",
                        columns: x => new { x.CompanyId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkOrders_Priority",
                table: "WorkOrders",
                sql: " \"Priority\" BETWEEN 1 AND 5 ");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderOperations_CompanyId_MachineId",
                table: "WorkOrderOperations",
                columns: new[] { "CompanyId", "MachineId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkOrderOperations_PlannedTargetQuantity",
                table: "WorkOrderOperations",
                sql: " CAST(\"PlannedTargetQuantity\" AS NUMERIC) > 0 ");

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_CompanyId_JobCardNumber",
                table: "JobCards",
                columns: new[] { "CompanyId", "JobCardNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_CompanyId_MachineId",
                table: "JobCards",
                columns: new[] { "CompanyId", "MachineId" });

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_CompanyId_WorkOrderId",
                table: "JobCards",
                columns: new[] { "CompanyId", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_CompanyId_WorkOrderOperationId",
                table: "JobCards",
                columns: new[] { "CompanyId", "WorkOrderOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionConfirmations_CompanyId_CorrectsConfirmationId",
                table: "ProductionConfirmations",
                columns: new[] { "CompanyId", "CorrectsConfirmationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionConfirmations_CompanyId_JobCardId_CreatedAt",
                table: "ProductionConfirmations",
                columns: new[] { "CompanyId", "JobCardId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionConfirmations_CompanyId_MachineId",
                table: "ProductionConfirmations",
                columns: new[] { "CompanyId", "MachineId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionConfirmations_CompanyId_WorkOrderId",
                table: "ProductionConfirmations",
                columns: new[] { "CompanyId", "WorkOrderId" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrderOperations_Machines_CompanyId_MachineId",
                table: "WorkOrderOperations",
                columns: new[] { "CompanyId", "MachineId" },
                principalTable: "Machines",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrderOperations_Machines_CompanyId_MachineId",
                table: "WorkOrderOperations");

            migrationBuilder.DropTable(
                name: "ProductionConfirmations");

            migrationBuilder.DropTable(
                name: "JobCards");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkOrders_Priority",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrderOperations_CompanyId_MachineId",
                table: "WorkOrderOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkOrderOperations_PlannedTargetQuantity",
                table: "WorkOrderOperations");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "AssignedTeam",
                table: "WorkOrderOperations");

            migrationBuilder.DropColumn(
                name: "MachineId",
                table: "WorkOrderOperations");

            migrationBuilder.DropColumn(
                name: "PlannedTargetQuantity",
                table: "WorkOrderOperations");

            migrationBuilder.DropColumn(
                name: "ProductionLine",
                table: "WorkOrderOperations");
        }
    }
}
