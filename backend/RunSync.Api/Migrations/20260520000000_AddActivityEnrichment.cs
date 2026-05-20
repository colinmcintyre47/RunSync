using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunSync.Api.Migrations
{
    public partial class AddActivityEnrichment : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── StravaActivities: new enrichment columns ────────────────────
            migrationBuilder.AddColumn<int>(
                name: "SufferScore",
                table: "StravaActivities",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "AverageCadence",
                table: "StravaActivities",
                type: "float",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "WorkoutType",
                table: "StravaActivities",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<float>(
                name: "MaxHeartrate",
                table: "StravaActivities",
                type: "float",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<string>(
                name: "SportType",
                table: "StravaActivities",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ElapsedTimeSeconds",
                table: "StravaActivities",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PrCount",
                table: "StravaActivities",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SummaryPolyline",
                table: "StravaActivities",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            // ── StravaTokens: athlete profile columns ───────────────────────
            migrationBuilder.AddColumn<string>(
                name: "AthleteFirstName",
                table: "StravaTokens",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AthleteLastName",
                table: "StravaTokens",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AthleteProfileUrl",
                table: "StravaTokens",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AthleteCity",
                table: "StravaTokens",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AthleteState",
                table: "StravaTokens",
                type: "longtext",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SufferScore",        table: "StravaActivities");
            migrationBuilder.DropColumn(name: "AverageCadence",     table: "StravaActivities");
            migrationBuilder.DropColumn(name: "WorkoutType",        table: "StravaActivities");
            migrationBuilder.DropColumn(name: "MaxHeartrate",       table: "StravaActivities");
            migrationBuilder.DropColumn(name: "SportType",          table: "StravaActivities");
            migrationBuilder.DropColumn(name: "ElapsedTimeSeconds", table: "StravaActivities");
            migrationBuilder.DropColumn(name: "PrCount",            table: "StravaActivities");
            migrationBuilder.DropColumn(name: "SummaryPolyline",    table: "StravaActivities");

            migrationBuilder.DropColumn(name: "AthleteFirstName",  table: "StravaTokens");
            migrationBuilder.DropColumn(name: "AthleteLastName",   table: "StravaTokens");
            migrationBuilder.DropColumn(name: "AthleteProfileUrl", table: "StravaTokens");
            migrationBuilder.DropColumn(name: "AthleteCity",       table: "StravaTokens");
            migrationBuilder.DropColumn(name: "AthleteState",      table: "StravaTokens");
        }
    }
}
