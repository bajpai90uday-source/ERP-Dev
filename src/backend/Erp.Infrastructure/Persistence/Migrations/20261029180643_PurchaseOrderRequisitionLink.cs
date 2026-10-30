using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseOrderRequisitionLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseRequisitionId",
                table: "PurchaseOrderRevisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderRevisions_CompanyId_PurchaseRequisitionId",
                table: "PurchaseOrderRevisions",
                columns: new[] { "CompanyId", "PurchaseRequisitionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderRevisions_PurchaseRequisitions_CompanyId_Purch~",
                table: "PurchaseOrderRevisions",
                columns: new[] { "CompanyId", "PurchaseRequisitionId" },
                principalTable: "PurchaseRequisitions",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderRevisions_PurchaseRequisitions_CompanyId_Purch~",
                table: "PurchaseOrderRevisions");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderRevisions_CompanyId_PurchaseRequisitionId",
                table: "PurchaseOrderRevisions");

            migrationBuilder.DropColumn(
                name: "PurchaseRequisitionId",
                table: "PurchaseOrderRevisions");
        }
    }
}
