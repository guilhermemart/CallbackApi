using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallbackApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventAuditTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE events SET updated_at = received_at WHERE updated_at IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                table: "events",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "events");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "events");
        }
    }
}
