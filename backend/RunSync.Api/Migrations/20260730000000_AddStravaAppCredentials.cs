// Migrations/20260730000000_AddStravaAppCredentials.cs
// Adds the per-user Strava API application credentials table.
//
// Each user registers their own free Strava app (which has an athlete capacity of 1 —
// themselves), so RunSync is not bounded by a single shared application's athlete limit.
//
// ClientSecretEncrypted stores AES-256-GCM ciphertext produced by SecretProtector, never a
// plaintext secret. The unique index on UserId enforces the one-to-one relationship.
//
// Applied automatically at startup by Program.cs → db.Database.MigrateAsync().

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunSync.Api.Migrations
{
    [Migration("20260730000000_AddStravaAppCredentials")]
    public partial class AddStravaAppCredentials : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StravaAppCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId                = table.Column<int>(type: "int", nullable: false),
                    ClientId              = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ClientSecretEncrypted = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    CreatedAt             = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt             = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StravaAppCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StravaAppCredentials_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Unique — one Strava application per user (one-to-one with Users)
            migrationBuilder.CreateIndex(
                name: "IX_StravaAppCredentials_UserId",
                table: "StravaAppCredentials",
                column: "UserId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StravaAppCredentials");
        }
    }
}
