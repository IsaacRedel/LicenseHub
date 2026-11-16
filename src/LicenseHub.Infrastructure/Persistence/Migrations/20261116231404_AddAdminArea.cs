using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LicenseHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_validation_logs_CreatedAt",
                table: "validation_logs");

            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                table: "licenses",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "RevokedByAdminId",
                table: "licenses",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "admin_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PasswordHash = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    FailedLoginCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEndsAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_users", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_validation_logs_CreatedAt_Outcome",
                table: "validation_logs",
                columns: new[] { "CreatedAt", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_licenses_CreatedAt_Id",
                table: "licenses",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_licenses_ProductId_Status",
                table: "licenses",
                columns: new[] { "ProductId", "Status" });

            // Moved after IX_licenses_ProductId_Status: MariaDB refuses to drop the only index backing
            // FK_licenses_products_ProductId (error 1553), and the composite index takes over that role.
            migrationBuilder.DropIndex(
                name: "IX_licenses_ProductId",
                table: "licenses");

            migrationBuilder.CreateIndex(
                name: "IX_licenses_RevokedByAdminId",
                table: "licenses",
                column: "RevokedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_admin_users_Email",
                table: "admin_users",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_licenses_admin_users_RevokedByAdminId",
                table: "licenses",
                column: "RevokedByAdminId",
                principalTable: "admin_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_licenses_admin_users_RevokedByAdminId",
                table: "licenses");

            migrationBuilder.DropTable(
                name: "admin_users");

            migrationBuilder.DropIndex(
                name: "IX_validation_logs_CreatedAt_Outcome",
                table: "validation_logs");

            migrationBuilder.DropIndex(
                name: "IX_licenses_CreatedAt_Id",
                table: "licenses");

            // Recreated before dropping the composite index, which backs the product foreign key until then.
            migrationBuilder.CreateIndex(
                name: "IX_licenses_ProductId",
                table: "licenses",
                column: "ProductId");

            migrationBuilder.DropIndex(
                name: "IX_licenses_ProductId_Status",
                table: "licenses");

            migrationBuilder.DropIndex(
                name: "IX_licenses_RevokedByAdminId",
                table: "licenses");

            migrationBuilder.DropColumn(
                name: "RevocationReason",
                table: "licenses");

            migrationBuilder.DropColumn(
                name: "RevokedByAdminId",
                table: "licenses");

            migrationBuilder.CreateIndex(
                name: "IX_validation_logs_CreatedAt",
                table: "validation_logs",
                column: "CreatedAt");
        }
    }
}
