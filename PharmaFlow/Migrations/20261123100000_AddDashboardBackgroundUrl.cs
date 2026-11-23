using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaFlow.Data;

#nullable disable

namespace PharmaFlow.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261123100000_AddDashboardBackgroundUrl")]
public partial class AddDashboardBackgroundUrl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "dashboard_background_url",
            schema: "public",
            table: "profiles",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "dashboard_background_url",
            schema: "public",
            table: "profiles");
    }
}
