using System.Globalization;
using System.Text;
using Cracha.Api.Dados;
using Cracha.Api.Modelos;
using Cracha.Api.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Controllers;

/// <summary>
/// Relatórios em CSV prontos para o Excel em português: separador ";", UTF-8 com BOM e números com vírgula.
/// Respeitam as mesmas regras de visibilidade das telas.
/// </summary>
[ApiController]
[Route("api/exportar")]
public class ExportacaoController(
    CrachaContext contexto, IHistorico historico, TimeProvider relogio, Visibilidade visibilidade) : ControllerBase
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Quadro de pessoal. Salário e endereço só para quem pode vê-los.</summary>
    [HttpGet("funcionarios.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> Funcionarios(Situacao? situacao, int? departamentoId)
    {
        var consulta = contexto.Funcionarios.AsNoTracking().Include(f => f.Departamento).Include(f => f.Gestor).AsQueryable();
        if (situacao is Situacao s)
            consulta = consulta.Where(f => f.Situacao == s);
        if (departamentoId is int d)
            consulta = consulta.Where(f => f.DepartamentoId == d);
        var equipe = await visibilidade.EquipeAsync();
        var hoje = DateOnly.FromDateTime(relogio.GetLocalNow().DateTime);

        var linhas = (await consulta.ToListAsync()).OrderBy(f => f.Nome, StringComparer.Create(Br, true)).Select(f =>
        {
            var ve = equipe?.Contains(f.Id) ?? true;
            var saida = FuncionarioSaida.De(f, hoje, ve);
            return new[]
            {
                $"{f.Id:00000}", f.Nome, f.Cargo, f.Departamento?.Nome, f.Gestor?.Nome, f.EmailProfissional, f.Ramal,
                f.DataAdmissao.ToString("dd/MM/yyyy", Br), f.Situacao.ToString(), f.DataDesligamento?.ToString("dd/MM/yyyy", Br),
                saida.MesesDeCasa.ToString(Br), ve ? f.Salario.ToString("N2", Br) : "", ve ? f.Endereco : "",
            };
        });
        return Csv($"funcionarios-{hoje:yyyy-MM-dd}.csv",
            ["Matrícula", "Nome", "Cargo", "Departamento", "Gestor", "E-mail", "Ramal", "Admissão", "Situação", "Desligamento",
             "Meses de casa", "Salário (R$)", "Endereço"], linhas);
    }

    /// <summary>Histórico de alterações, um campo alterado por linha.</summary>
    [HttpGet("historico.csv")]
    [Authorize(Policy = Politicas.Acompanhar)]
    [Produces("text/csv")]
    public async Task<IActionResult> Historico(string? departamento, TipoAcao? tipo)
    {
        var equipe = await visibilidade.EquipeAsync();
        var registros = (await historico.ListarAsync(new FiltroHistorico(null, departamento, tipo, 5000)))
            .Where(r => equipe?.Contains(r.FuncionarioId) ?? true);

        var linhas = registros.SelectMany(r =>
        {
            var quando = r.Quando.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Br);
            var campos = r.Alteracoes.Count == 0 ? [new CampoAlterado("", null, null)] : r.Alteracoes;
            return campos.Select(c => new[]
                { quando, r.TipoAcao.ToString(), $"{r.FuncionarioId:00000}", r.NomeFuncionario, r.Departamento, r.Autor, c.Campo, c.Antes, c.Depois });
        });
        return Csv($"historico-{relogio.GetLocalNow():yyyy-MM-dd}.csv",
            ["Quando", "Ação", "Matrícula", "Funcionário", "Departamento", "Autor", "Campo", "Antes", "Depois"], linhas);
    }

    /// <summary>Férias e ausências no período.</summary>
    [HttpGet("ausencias.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> Ausencias(DateOnly? de, DateOnly? ate)
    {
        var equipe = await visibilidade.EquipeAsync();
        var consulta = contexto.Ausencias.AsNoTracking().Include(a => a.Funcionario).ThenInclude(f => f!.Departamento).AsQueryable();
        if (de is DateOnly d)
            consulta = consulta.Where(a => a.Fim >= d);
        if (ate is DateOnly t)
            consulta = consulta.Where(a => a.Inicio <= t);

        var linhas = (await consulta.ToListAsync())
            .Where(a => equipe?.Contains(a.FuncionarioId) ?? true)
            .OrderBy(a => a.Inicio)
            .Select(a => new[]
            {
                a.Funcionario!.Nome, a.Funcionario.Departamento?.Nome, a.Tipo.ToString(), a.Inicio.ToString("dd/MM/yyyy", Br),
                a.Fim.ToString("dd/MM/yyyy", Br), a.Dias.ToString(Br), a.Status.ToString(), a.SolicitadaPor, a.DecididaPor,
                a.MotivoRecusa ?? a.Observacao,
            });
        return Csv($"ausencias-{relogio.GetLocalNow():yyyy-MM-dd}.csv",
            ["Funcionário", "Departamento", "Tipo", "Início", "Fim", "Dias", "Situação", "Pedido por", "Decidido por", "Observação"], linhas);
    }

    private FileContentResult Csv(string nome, string[] cabecalho, IEnumerable<string?[]> linhas)
    {
        var texto = new StringBuilder();
        texto.AppendLine(string.Join(';', cabecalho.Select(Celula)));
        foreach (var linha in linhas)
            texto.AppendLine(string.Join(';', linha.Select(Celula)));
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(texto.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", nome);
    }

    /// <summary>
    /// Escapa aspas e ";" e neutraliza injeção de fórmula: um valor começando com = + - @ viraria
    /// fórmula ao abrir no Excel, então ganha um apóstrofo na frente.
    /// </summary>
    internal static string Celula(string? valor)
    {
        valor ??= "";
        if (valor.Length > 0 && "=+-@\t\r".Contains(valor[0]))
            valor = "'" + valor;
        return valor.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{valor.Replace("\"", "\"\"")}\"" : valor;
    }
}
