using MarketLink.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLink.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260928203000_EmailNotificationDelivery")]
    public partial class EmailNotificationDelivery : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailError",
                table: "notifications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailSent",
                table: "notifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailSentAt",
                table: "notifications",
                type: "datetime2",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailError",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "EmailSent",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "EmailSentAt",
                table: "notifications");
        }
    }
}
