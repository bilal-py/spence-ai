using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpenceAI.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class _20261114202222_SavingsPotsAndPet : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "PetEnabled",
            table: "Settings",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<string>(
            name: "PetName",
            table: "Settings",
            type: "TEXT",
            maxLength: 30,
            nullable: false,
            defaultValue: "Sprig");

        migrationBuilder.AddColumn<DateOnly>(
            name: "PetNudgeDismissedOn",
            table: "Settings",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "SavingsPots",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                Color = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                Icon = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                TargetAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Deadline = table.Column<DateOnly>(type: "TEXT", nullable: true),
                LinkPattern = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                RoundUpTo = table.Column<int>(type: "INTEGER", nullable: true),
                RoundUpsClaimedThrough = table.Column<DateOnly>(type: "TEXT", nullable: true),
                CollectLeftover = table.Column<bool>(type: "INTEGER", nullable: false),
                LeftoverClaimedThrough = table.Column<DateOnly>(type: "TEXT", nullable: true),
                NoSpendBonus = table.Column<decimal>(type: "TEXT", nullable: true),
                NoSpendClaimedThrough = table.Column<DateOnly>(type: "TEXT", nullable: true),
                IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SavingsPots", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PotChallenges",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                PotId = table.Column<int>(type: "INTEGER", nullable: false),
                Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                Target = table.Column<int>(type: "INTEGER", nullable: true),
                StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Reward = table.Column<decimal>(type: "TEXT", nullable: false),
                ClaimedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PotChallenges", x => x.Id);
                table.ForeignKey(
                    name: "FK_PotChallenges_Categories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "Categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PotChallenges_SavingsPots_PotId",
                    column: x => x.PotId,
                    principalTable: "SavingsPots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PotContributions",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                PotId = table.Column<int>(type: "INTEGER", nullable: false),
                Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PotContributions", x => x.Id);
                table.ForeignKey(
                    name: "FK_PotContributions_SavingsPots_PotId",
                    column: x => x.PotId,
                    principalTable: "SavingsPots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PotChallenges_CategoryId",
            table: "PotChallenges",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_PotChallenges_PotId",
            table: "PotChallenges",
            column: "PotId");

        migrationBuilder.CreateIndex(
            name: "IX_PotContributions_PotId_Date",
            table: "PotContributions",
            columns: new[] { "PotId", "Date" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PotChallenges");

        migrationBuilder.DropTable(
            name: "PotContributions");

        migrationBuilder.DropTable(
            name: "SavingsPots");

        migrationBuilder.DropColumn(
            name: "PetEnabled",
            table: "Settings");

        migrationBuilder.DropColumn(
            name: "PetName",
            table: "Settings");

        migrationBuilder.DropColumn(
            name: "PetNudgeDismissedOn",
            table: "Settings");
    }
}
