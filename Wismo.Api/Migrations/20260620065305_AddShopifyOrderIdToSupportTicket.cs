using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wismo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddShopifyOrderIdToSupportTicket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShopifyOrderId",
                table: "SupportTickets",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShopifyOrderId",
                table: "SupportTickets");
        }
    }
}
