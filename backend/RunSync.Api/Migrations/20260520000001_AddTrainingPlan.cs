using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunSync.Api.Migrations
{
    [Migration("20260520000001_AddTrainingPlan")]
    public partial class AddTrainingPlan : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserTrainingPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId              = table.Column<int>(type: "int", nullable: false),
                    GoalType            = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    RaceDate            = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FitnessLevel        = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CurrentWeeklyMiles  = table.Column<float>(type: "float", nullable: false),
                    CurrentLongRunMiles = table.Column<float>(type: "float", nullable: false),
                    GoalFinishMinutes   = table.Column<int>(type: "int", nullable: true),
                    RunDays             = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, defaultValue: "[]"),
                    LongRunDay          = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, defaultValue: "Sun"),
                    CreatedAt           = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt           = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTrainingPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTrainingPlans_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserTrainingPlans_UserId",
                table: "UserTrainingPlans",
                column: "UserId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserTrainingPlans");
        }
    }
}
