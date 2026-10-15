using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaFlow.Data;

#nullable disable

namespace PharmaFlow.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261015180000_AddProfilePhotoUrl")]
public partial class AddProfilePhotoUrl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "profile_photo_url",
            schema: "public",
            table: "profiles",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "profile_photo_url",
            schema: "public",
            table: "profiles");
    }
}
