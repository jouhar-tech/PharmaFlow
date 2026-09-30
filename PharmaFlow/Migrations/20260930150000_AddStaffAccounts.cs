using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaFlow.Migrations;

public partial class AddStaffAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "staff",
            schema: "public",
            columns: table => new
            {
                staff_id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                profile_id = table.Column<long>(type: "bigint", nullable: false),
                auth_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                full_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                profile_photo_url = table.Column<string>(type: "text", nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_logout_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_staff", x => x.staff_id);
                table.ForeignKey(
                    name: "FK_staff_profiles_profile_id",
                    column: x => x.profile_id,
                    principalSchema: "public",
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.CheckConstraint(
                    "CK_staff_login_identifier",
                    "email IS NOT NULL OR phone_number IS NOT NULL");
            });

        migrationBuilder.CreateIndex(
            name: "IX_staff_auth_user_id",
            schema: "public",
            table: "staff",
            column: "auth_user_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_staff_profile_id",
            schema: "public",
            table: "staff",
            column: "profile_id");

        migrationBuilder.CreateIndex(
            name: "IX_staff_profile_id_email",
            schema: "public",
            table: "staff",
            columns: new[] { "profile_id", "email" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_staff_profile_id_phone_number",
            schema: "public",
            table: "staff",
            columns: new[] { "profile_id", "phone_number" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "staff",
            schema: "public");
    }
}
