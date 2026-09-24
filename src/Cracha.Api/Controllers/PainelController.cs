using System.Globalization;
using System.Reflection;
using Cracha.Api.Dados;
using Cracha.Api.Modelos;
using Cracha.Api.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Controllers;

/// <summary>Indicadores de RH, histórico geral, organograma e informações do ambiente.</summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class PainelController(
    CrachaContext contexto, IHistorico historico, IArmazenamentoFotos fotos, TimeProvider relogio,
    UsuarioAtual usuario, Visibilidade visibilidade, IWebHostEnvironment ambiente) : ControllerBase
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Quadro de pessoal, folha, admissões e desligamentos, ausências e aniversários de empresa.</summary>
    /// <remarks>RH e administrador veem a empresa; o gestor vê só a própria equipe (direta e indireta).</remarks>
    [HttpGet("painel")]
    [Authorize(Policy = Politicas.Acompanhar)]
    public async Task<Painel> Painel()
    {
        var hoje = DateOnly.FromDateTime(relogio.GetLocalNow().DateTime);
        var equipe = await visibilidade.EquipeAsync();
        var funcionarios = (await contexto.Funcionarios.AsNoTracking().Include(f => f.Departamento).ToListAsync())
            .Where(f => equipe?.Contains(f.Id) ?? true).ToList();
        var ativos = funcionarios.Where(f => f.Situacao == Situacao.Ativo).ToList();
        var departamentos = await contexto.Departamentos.AsNoTracking().OrderBy(d => d.Nome).ToListAsync();

        var porDepartamento = departamentos
            .Select(d =>
            {
                var doDepto = ativos.Where(f => f.DepartamentoId == d.Id).ToList();
                return new DepartamentoSaida(d.Id, d.Nome, d.Cor, doDepto.Count, doDepto.Sum(f => f.Salario));
            })
            .Where(d => equipe is null || d.Ativos > 0)
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

        // Rotatividade: desligamentos em 12 meses sobre o quadro médio (início e fim do período).
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

        var ids = funcionarios.Select(f => f.Id).ToHashSet();
        var ausencias = await contexto.Ausencias.AsNoTracking()
            .Include(a => a.Funcionario).ThenInclude(f => f!.Departamento)
            .Where(a => a.Status == StatusAusencia.Aprovada && a.Inicio <= hoje && a.Fim >= hoje)
            .ToListAsync();
        var ausentes = ausencias.Where(a => ids.Contains(a.FuncionarioId)).OrderBy(a => a.Fim)
            .Select(a => AusenciaSaida.De(a, false, false)).ToList();
        var pendentes = (await contexto.Ausencias.AsNoTracking().Where(a => a.Status == StatusAusencia.Pendente).Select(a => a.FuncionarioId).ToListAsync())
            .Count(id => ids.Contains(id) && id != usuario.FuncionarioId);

        var ultimas = (await historico.ListarAsync(new FiltroHistorico(Limite: equipe is null ? 6 : 300)))
            .Where(r => equipe?.Contains(r.FuncionarioId) ?? true).Take(6);

        var escopo = equipe is null ? "Empresa" : $"Equipe de {usuario.Nome}";
        return new Painel(
            escopo,
            ativos.Count,
            funcionarios.Count - ativos.Count,
            ativos.Sum(f => f.Salario),
            ativos.Count > 0 ? Math.Round(ativos.Average(f => f.Salario), 2) : 0,
            ativos.Count > 0 ? Math.Round(ativos.Average(f => FuncionarioSaida.De(f, hoje, true).MesesDeCasa), 1) : 0,
            funcionarios.Count(f => f.DataAdmissao.Year == hoje.Year),
            rotatividade,
            pendentes,
            porDepartamento,
            porMes,
            aniversarios,
            ausentes,
            ultimas.Select(HistoricoSaida.De).ToList());
    }

    /// <summary>Histórico geral de alterações (o log gravado na Azure Table).</summary>
    /// <param name="departamento">Nome do departamento (a PartitionKey na Azure Table).</param>
    /// <param name="tipo">Inclusao, Atualizacao, Desligamento, Reativacao ou Remocao.</param>
    /// <param name="limite">Máximo de registros (1 a 500).</param>
    [HttpGet("historico")]
    [Authorize(Policy = Politicas.Acompanhar)]
    public async Task<IEnumerable<HistoricoSaida>> Historico(string? departamento, TipoAcao? tipo, int limite = 100)
    {
        limite = Math.Clamp(limite, 1, 500);
        var equipe = await visibilidade.EquipeAsync();
        var registros = await historico.ListarAsync(new FiltroHistorico(null, departamento, tipo, equipe is null ? limite : 5000));
        return registros.Where(r => equipe?.Contains(r.FuncionarioId) ?? true).Take(limite).Select(HistoricoSaida.De);
    }

    /// <summary>Organograma: funcionários ativos com o gestor imediato de cada um.</summary>
    [HttpGet("organograma")]
    public async Task<IEnumerable<NoOrganograma>> Organograma()
    {
        var ativos = await contexto.Funcionarios.AsNoTracking().Include(f => f.Departamento)
            .Where(f => f.Situacao == Situacao.Ativo).OrderBy(f => f.Nome).ToListAsync();
        return ativos.Select(f => new NoOrganograma(f.Id, f.Nome, f.Cargo, f.Departamento!.Nome, f.Departamento.Cor, f.GestorId,
            f.FotoVersao is null ? null : $"/api/funcionarios/{f.Id}/foto?v={f.FotoVersao}"));
    }

    /// <summary>Onde os dados, o histórico e as fotos estão sendo gravados.</summary>
    [HttpGet("sistema")]
    [AllowAnonymous]
    public InfoSistema Sistema() => new(
        contexto.Database.IsSqlServer() ? "SQL Server" : "SQLite",
        historico.Descricao,
        fotos.Descricao,
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0",
        ambiente.EnvironmentName);
}
