using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallbackApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameReceivedAtToCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "received_at",
                table: "events",
                newName: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "events",
                newName: "received_at");
        }
    }
}
