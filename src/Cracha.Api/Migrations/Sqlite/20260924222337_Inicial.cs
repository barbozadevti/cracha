using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cracha.Api.Migrations.Sqlite
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Cor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departamentos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Historico",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FuncionarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    NomeFuncionario = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Departamento = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TipoAcao = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    Quando = table.Column<long>(type: "INTEGER", nullable: false),
                    FotoJson = table.Column<string>(type: "TEXT", nullable: false),
                    Alteracoes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Historico", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Funcionarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Cargo = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Endereco = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Ramal = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    EmailProfissional = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DepartamentoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Salario = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    DataAdmissao = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Situacao = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DataDesligamento = table.Column<DateOnly>(type: "TEXT", nullable: true)
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
