using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpenceAI.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class _20261114142210_BackupSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AutoBackup",
            table: "Settings",
            type: "INTEGER",
            nullable: false,
            defaultValue: true); // existing installs get daily backups too

        migrationBuilder.AddColumn<string>(
            name: "BackupCopyFolder",
            table: "Settings",
            type: "TEXT",
            maxLength: 500,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AutoBackup",
            table: "Settings");

        migrationBuilder.DropColumn(
            name: "BackupCopyFolder",
            table: "Settings");
    }
}
