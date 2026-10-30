using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesPurchaseIdempotencyActorScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransactionIdempotencyRecords_CompanyId_Key",
                table: "TransactionIdempotencyRecords");

            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                table: "TransactionIdempotencyRecords",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_TransactionIdempotencyRecords_ActorUserId",
                table: "TransactionIdempotencyRecords",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionIdempotencyRecords_CompanyId_ActorUserId_Key",
                table: "TransactionIdempotencyRecords",
                columns: new[] { "CompanyId", "ActorUserId", "Key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TransactionIdempotencyRecords_UserAccounts_ActorUserId",
                table: "TransactionIdempotencyRecords",
                column: "ActorUserId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransactionIdempotencyRecords_UserAccounts_ActorUserId",
                table: "TransactionIdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_TransactionIdempotencyRecords_ActorUserId",
                table: "TransactionIdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_TransactionIdempotencyRecords_CompanyId_ActorUserId_Key",
                table: "TransactionIdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "TransactionIdempotencyRecords");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionIdempotencyRecords_CompanyId_Key",
                table: "TransactionIdempotencyRecords",
                columns: new[] { "CompanyId", "Key" },
                unique: true);
        }
    }
}
