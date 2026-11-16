using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LicenseHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameActivationInstanceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ServerIdentifier",
                table: "activations",
                newName: "InstanceId");

            migrationBuilder.RenameIndex(
                name: "IX_activations_LicenseId_ServerIdentifier",
                table: "activations",
                newName: "IX_activations_LicenseId_InstanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "InstanceId",
                table: "activations",
                newName: "ServerIdentifier");

            migrationBuilder.RenameIndex(
                name: "IX_activations_LicenseId_InstanceId",
                table: "activations",
                newName: "IX_activations_LicenseId_ServerIdentifier");
        }
    }
}
