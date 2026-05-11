using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accountant.MySql.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "evaluation_runs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RunAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Notes = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_runs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "source_documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FileHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileName = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_documents", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "extractions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SourceDocumentId = table.Column<int>(type: "int", nullable: false),
                    Vendor = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Model = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PromptVersion = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Pipeline = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OcrUsed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    CostEstimateUsd = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StopReason = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsSuccess = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ValidationNeedsReview = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ValidationFailCount = table.Column<int>(type: "int", nullable: false),
                    ValidationWarnCount = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResultJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_extractions_source_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "source_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ground_truths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SourceDocumentId = table.Column<int>(type: "int", nullable: false),
                    ExtractionJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastEditedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastEditedBy = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ground_truths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ground_truths_source_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "source_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "evaluation_documents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EvaluationRunId = table.Column<int>(type: "int", nullable: false),
                    SourceDocumentId = table.Column<int>(type: "int", nullable: false),
                    ExtractionId = table.Column<long>(type: "bigint", nullable: true),
                    Vendor = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PromptVersion = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Model = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MatchCount = table.Column<int>(type: "int", nullable: false),
                    MismatchCount = table.Column<int>(type: "int", nullable: false),
                    CriticalMatchCount = table.Column<int>(type: "int", nullable: false),
                    CriticalMismatchCount = table.Column<int>(type: "int", nullable: false),
                    MoneyMatchCount = table.Column<int>(type: "int", nullable: false),
                    MoneyMismatchCount = table.Column<int>(type: "int", nullable: false),
                    MismatchesJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_documents_evaluation_runs_EvaluationRunId",
                        column: x => x.EvaluationRunId,
                        principalTable: "evaluation_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_evaluation_documents_extractions_ExtractionId",
                        column: x => x.ExtractionId,
                        principalTable: "extractions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_evaluation_documents_source_documents_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "source_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_documents_EvaluationRunId_Vendor",
                table: "evaluation_documents",
                columns: new[] { "EvaluationRunId", "Vendor" });

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_documents_ExtractionId",
                table: "evaluation_documents",
                column: "ExtractionId");

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_documents_PromptVersion",
                table: "evaluation_documents",
                column: "PromptVersion");

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_documents_SourceDocumentId",
                table: "evaluation_documents",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_runs_RunAtUtc",
                table: "evaluation_runs",
                column: "RunAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_extractions_Model",
                table: "extractions",
                column: "Model");

            migrationBuilder.CreateIndex(
                name: "IX_extractions_PromptVersion",
                table: "extractions",
                column: "PromptVersion");

            migrationBuilder.CreateIndex(
                name: "IX_extractions_SourceDocumentId_Vendor_StartedAtUtc",
                table: "extractions",
                columns: new[] { "SourceDocumentId", "Vendor", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ground_truths_SourceDocumentId",
                table: "ground_truths",
                column: "SourceDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_source_documents_FileHash",
                table: "source_documents",
                column: "FileHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_source_documents_FileName",
                table: "source_documents",
                column: "FileName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evaluation_documents");

            migrationBuilder.DropTable(
                name: "ground_truths");

            migrationBuilder.DropTable(
                name: "evaluation_runs");

            migrationBuilder.DropTable(
                name: "extractions");

            migrationBuilder.DropTable(
                name: "source_documents");
        }
    }
}
