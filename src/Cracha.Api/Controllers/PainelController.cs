using System.Globalization;
using System.Reflection;
using Cracha.Api.Dados;
using Cracha.Api.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Controllers;

/// <summary>Indicadores de RH e o histórico geral de alterações.</summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class PainelController(CrachaContext contexto, IHistorico historico, TimeProvider relogio) : ControllerBase
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Quadro de pessoal, folha, admissões e desligamentos dos últimos 12 meses e aniversários de empresa.</summary>
    [HttpGet("painel")]
    public async Task<Painel> Painel()
    {
        var hoje = DateOnly.FromDateTime(relogio.GetLocalNow().DateTime);
        var funcionarios = await contexto.Funcionarios.AsNoTracking().Include(f => f.Departamento).ToListAsync();
        var departamentos = await contexto.Departamentos.AsNoTracking().OrderBy(d => d.Nome).ToListAsync();
        var ativos = funcionarios.Where(f => f.Situacao == Situacao.Ativo).ToList();

        var porDepartamento = departamentos
            .Select(d =>
            {
                var doDepto = ativos.Where(f => f.DepartamentoId == d.Id).ToList();
                return new DepartamentoSaida(d.Id, d.Nome, d.Cor, doDepto.Count, doDepto.Sum(f => f.Salario));
            })
            .OrderByDescending(d => d.Ativos).ThenBy(d => d.Nome)
            .ToList();

        // Últimos 12 meses, do mais antigo ao atual.
        var inicio = new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(-11);
        var meses = Enumerable.Range(0, 12).Select(i => inicio.AddMonths(i)).ToList();
        bool NoMes(DateOnly? data, DateOnly mes) => data is DateOnly d && d.Year == mes.Year && d.Month == mes.Month;
        var porMes = meses
            .Select(m => new AdmissoesDoMes(
                Br.TextInfo.ToTitleCase(m.ToString("MMM/yy", Br).Replace(".", "")),
                funcionarios.Count(f => NoMes(f.DataAdmissao, m)),
                funcionarios.Count(f => NoMes(f.DataDesligamento, m))))
            .ToList();

        // Rotatividade: desligamentos em 12 meses sobre o quadro médio (ativos hoje + desligados no período, / 2).
        var desligados12 = funcionarios.Count(f => f.DataDesligamento >= inicio);
        var quadroMedio = (ativos.Count + (ativos.Count + desligados12 - porMes.Sum(m => m.Admissoes))) / 2.0;
        var rotatividade = quadroMedio > 0 ? Math.Round(desligados12 / quadroMedio * 100, 1) : 0;

        var aniversarios = ativos
            .Where(f => f.DataAdmissao.Month == hoje.Month && f.DataAdmissao.Year < hoje.Year)
            .OrderBy(f => f.DataAdmissao.Day)
            .Select(f => new AniversarioDeEmpresa(f.Id, f.Nome, f.Departamento!.Nome, f.Departamento.Cor,
                new DateOnly(hoje.Year, hoje.Month, Math.Min(f.DataAdmissao.Day, DateTime.DaysInMonth(hoje.Year, hoje.Month))),
                hoje.Year - f.DataAdmissao.Year))
            .ToList();

        var ultimas = await historico.ListarAsync(new FiltroHistorico(Limite: 6));

        return new Painel(
            ativos.Count,
            funcionarios.Count - ativos.Count,
            ativos.Sum(f => f.Salario),
            ativos.Count > 0 ? Math.Round(ativos.Average(f => f.Salario), 2) : 0,
            ativos.Count > 0 ? Math.Round(ativos.Average(f => FuncionarioSaida.De(f, hoje).MesesDeCasa), 1) : 0,
            funcionarios.Count(f => f.DataAdmissao.Year == hoje.Year),
            rotatividade,
            porDepartamento,
            porMes,
            aniversarios,
            ultimas.Select(HistoricoSaida.De).ToList());
    }

    /// <summary>Histórico geral de alterações (o log gravado na Azure Table).</summary>
    /// <param name="departamento">Nome do departamento (a PartitionKey na Azure Table).</param>
    /// <param name="tipo">Inclusao, Atualizacao, Desligamento, Reativacao ou Remocao.</param>
    /// <param name="limite">Máximo de registros (1 a 500).</param>
    [HttpGet("historico")]
    public async Task<IEnumerable<HistoricoSaida>> Historico(string? departamento, TipoAcao? tipo, int limite = 100)
    {
        var registros = await historico.ListarAsync(new FiltroHistorico(null, departamento, tipo, Math.Clamp(limite, 1, 500)));
        return registros.Select(HistoricoSaida.De);
    }

    /// <summary>Onde os dados e o histórico estão sendo gravados.</summary>
    [HttpGet("sistema")]
    public InfoSistema Sistema() => new(
        contexto.Database.IsSqlServer() ? "SQL Server" : "SQLite",
        historico.Descricao,
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
}
