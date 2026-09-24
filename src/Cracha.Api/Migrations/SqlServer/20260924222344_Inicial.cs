using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracha.Api.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Departamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Cor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departamentos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Historico",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FuncionarioId = table.Column<int>(type: "int", nullable: false),
                    NomeFuncionario = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Departamento = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TipoAcao = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Quando = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FotoJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Alteracoes = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Historico", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Funcionarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Cargo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Endereco = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Ramal = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    EmailProfissional = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DepartamentoId = table.Column<int>(type: "int", nullable: false),
                    Salario = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DataAdmissao = table.Column<DateOnly>(type: "date", nullable: false),
                    Situacao = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DataDesligamento = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Funcionarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Funcionarios_Departamentos_DepartamentoId",
                        column: x => x.DepartamentoId,
                        principalTable: "Departamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Departamentos_Nome",
                table: "Departamentos",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Funcionarios_DepartamentoId",
                table: "Funcionarios",
                column: "DepartamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Funcionarios_EmailProfissional",
                table: "Funcionarios",
                column: "EmailProfissional",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Funcionarios_Nome",
                table: "Funcionarios",
                column: "Nome");

            migrationBuilder.CreateIndex(
                name: "IX_Historico_FuncionarioId",
                table: "Historico",
                column: "FuncionarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Historico_Quando",
                table: "Historico",
                column: "Quando");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Funcionarios");

            migrationBuilder.DropTable(
                name: "Historico");

            migrationBuilder.DropTable(
                name: "Departamentos");
        }
    }
}
