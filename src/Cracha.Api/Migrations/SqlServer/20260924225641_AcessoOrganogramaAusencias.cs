using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracha.Api.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AcessoOrganogramaAusencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Autor",
                table: "Historico",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Sistema");

            migrationBuilder.AddColumn<string>(
                name: "FotoVersao",
                table: "Funcionarios",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GestorId",
                table: "Funcionarios",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Ausencias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FuncionarioId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    Fim = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SolicitadaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SolicitadaPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DecididaEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecididaPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MotivoRecusa = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ausencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ausencias_Funcionarios_FuncionarioId",
                        column: x => x.FuncionarioId,
                        principalTable: "Funcionarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SenhaHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Perfil = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    FuncionarioId = table.Column<int>(type: "int", nullable: true),
                    Ativo = table.Column<bool>(type: "bit", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UltimoAcesso = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Carimbo = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Usuarios_Funcionarios_FuncionarioId",
                        column: x => x.FuncionarioId,
                        principalTable: "Funcionarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Funcionarios_GestorId",
                table: "Funcionarios",
                column: "GestorId");

            migrationBuilder.CreateIndex(
                name: "IX_Ausencias_FuncionarioId_Inicio",
                table: "Ausencias",
                columns: new[] { "FuncionarioId", "Inicio" });

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Email",
                table: "Usuarios",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_FuncionarioId",
                table: "Usuarios",
                column: "FuncionarioId");

            migrationBuilder.AddForeignKey(
                name: "FK_Funcionarios_Funcionarios_GestorId",
                table: "Funcionarios",
                column: "GestorId",
                principalTable: "Funcionarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Funcionarios_Funcionarios_GestorId",
                table: "Funcionarios");

            migrationBuilder.DropTable(
                name: "Ausencias");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_Funcionarios_GestorId",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "Autor",
                table: "Historico");

            migrationBuilder.DropColumn(
                name: "FotoVersao",
                table: "Funcionarios");

            migrationBuilder.DropColumn(
                name: "GestorId",
                table: "Funcionarios");
        }
    }
}
