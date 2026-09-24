using Cracha.Api.Dados;
using Cracha.Api.Modelos;
using Cracha.Api.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Controllers;

/// <summary>
/// Férias, folgas, licenças e atestados. O colaborador pede férias e folgas para si; o gestor aprova os
/// pedidos da equipe (nunca os próprios); o RH aprova qualquer pedido e lança licenças e atestados.
/// </summary>
[ApiController]
[Route("api/ausencias")]
[Produces("application/json")]
public class AusenciasController(
    CrachaContext contexto, TimeProvider relogio, UsuarioAtual usuario, Visibilidade visibilidade) : ControllerBase
{
    private DateOnly Hoje => DateOnly.FromDateTime(relogio.GetLocalNow().DateTime);

    /// <summary>Lista as ausências que o usuário pode ver.</summary>
    /// <param name="status">Pendente, Aprovada, Recusada ou Cancelada.</param>
    /// <param name="funcionarioId">Só desta pessoa.</param>
    /// <param name="de">Que terminam a partir desta data.</param>
    /// <param name="ate">Que começam até esta data.</param>
    /// <param name="paraDecidir">Só os pedidos que o usuário pode aprovar.</param>
    [HttpGet]
    public async Task<IEnumerable<AusenciaSaida>> Listar(StatusAusencia? status, int? funcionarioId, DateOnly? de, DateOnly? ate, bool paraDecidir = false)
    {
        var consulta = contexto.Ausencias.AsNoTracking().Include(a => a.Funcionario).ThenInclude(f => f!.Departamento).AsQueryable();
        if (status is StatusAusencia s)
            consulta = consulta.Where(a => a.Status == s);
        if (funcionarioId is int f)
            consulta = consulta.Where(a => a.FuncionarioId == f);
        if (de is DateOnly d)
            consulta = consulta.Where(a => a.Fim >= d);
        if (ate is DateOnly t)
            consulta = consulta.Where(a => a.Inicio <= t);

        var equipe = await visibilidade.EquipeAsync();
        var lista = (await consulta.ToListAsync())
            .Where(a => equipe?.Contains(a.FuncionarioId) ?? true)
            .Select(a => AusenciaSaida.De(a, PodeDecidir(a, equipe), PodeCancelar(a)));
        if (paraDecidir)
            lista = lista.Where(a => a.PodeDecidir);
        return lista.OrderBy(a => a.Status != StatusAusencia.Pendente).ThenBy(a => a.Inicio).ToList();
    }

    /// <summary>Registra um pedido. Licença e atestado só pelo RH, e já entram aprovados.</summary>
    [HttpPost]
    [ProducesResponseType<AusenciaSaida>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AusenciaSaida>> Solicitar(AusenciaEntrada entrada)
    {
        var funcionarioId = entrada.FuncionarioId ?? usuario.FuncionarioId;
        if (funcionarioId is not int id)
            return Invalido("funcionarioId", "Escolha a pessoa.");
        var paraSi = id == usuario.FuncionarioId;
        if (!paraSi && !usuario.VeTudo)
            return Problema(StatusCodes.Status403Forbidden, "Só o RH registra ausências de outras pessoas.");
        if (entrada.Tipo is (TipoAusencia.Licenca or TipoAusencia.Atestado) && !usuario.VeTudo)
            return Problema(StatusCodes.Status403Forbidden, "Licenças e atestados são lançados pelo RH.");

        var funcionario = await contexto.Funcionarios.Include(f => f.Departamento).FirstOrDefaultAsync(f => f.Id == id);
        if (funcionario is null || funcionario.Situacao != Situacao.Ativo)
            return Invalido("funcionarioId", "A pessoa precisa estar ativa.");

        var (inicio, fim) = (entrada.Inicio!.Value, entrada.Fim!.Value);
        if (!usuario.VeTudo && inicio < Hoje)
            return Invalido("inicio", "O pedido precisa começar a partir de hoje.");
        if (inicio < funcionario.DataAdmissao)
            return Invalido("inicio", "A ausência não pode começar antes da admissão.");

        var conflito = await contexto.Ausencias.AsNoTracking().FirstOrDefaultAsync(a => a.FuncionarioId == id
            && (a.Status == StatusAusencia.Pendente || a.Status == StatusAusencia.Aprovada)
            && a.Inicio <= fim && a.Fim >= inicio);
        if (conflito is not null)
            return Problema(StatusCodes.Status409Conflict,
                $"Já existe {Nome(conflito.Tipo).ToLowerInvariant()} ({conflito.Status.ToString().ToLowerInvariant()}) de {conflito.Inicio:dd/MM} a {conflito.Fim:dd/MM} nesse período.");

        // Lançamentos do RH (inclusive licença e atestado) já nascem aprovados.
        var aprovadaDireto = usuario.VeTudo;
        var ausencia = new Ausencia
        {
            FuncionarioId = id,
            Funcionario = funcionario,
            Tipo = entrada.Tipo,
            Inicio = inicio,
            Fim = fim,
            Observacao = string.IsNullOrWhiteSpace(entrada.Observacao) ? null : entrada.Observacao.Trim(),
            SolicitadaEm = relogio.GetUtcNow(),
            SolicitadaPor = usuario.Assinatura,
            Status = aprovadaDireto ? StatusAusencia.Aprovada : StatusAusencia.Pendente,
            DecididaEm = aprovadaDireto ? relogio.GetUtcNow() : null,
            DecididaPor = aprovadaDireto ? usuario.Assinatura : null,
        };
        contexto.Ausencias.Add(ausencia);
        await contexto.SaveChangesAsync();
        return Created($"/api/ausencias/{ausencia.Id}", await SaidaAsync(ausencia));
    }

    /// <summary>Aprova ou recusa um pedido pendente. Recusar exige motivo.</summary>
    [HttpPost("{id:int}/decisao")]
    [ProducesResponseType<AusenciaSaida>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AusenciaSaida>> Decidir(int id, DecisaoAusencia decisao)
    {
        var ausencia = await Buscar(id);
        if (ausencia is null)
            return NotFound();
        if (ausencia.Status != StatusAusencia.Pendente)
            return Problema(StatusCodes.Status409Conflict, "Este pedido já foi decidido.");
        if (!await PodeDecidirAsync(ausencia))
            return Problema(StatusCodes.Status403Forbidden, "Só o gestor da pessoa ou o RH podem decidir este pedido.");
        if (!decisao.Aprovar && string.IsNullOrWhiteSpace(decisao.Motivo))
            return Invalido("motivo", "Explique o motivo da recusa.");

        ausencia.Status = decisao.Aprovar ? StatusAusencia.Aprovada : StatusAusencia.Recusada;
        ausencia.MotivoRecusa = decisao.Aprovar ? null : decisao.Motivo!.Trim();
        ausencia.DecididaEm = relogio.GetUtcNow();
        ausencia.DecididaPor = usuario.Assinatura;
        await contexto.SaveChangesAsync();
        return await SaidaAsync(ausencia);
    }

    /// <summary>Cancela um pedido pendente ou uma ausência aprovada que ainda não começou.</summary>
    [HttpPost("{id:int}/cancelar")]
    [ProducesResponseType<AusenciaSaida>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AusenciaSaida>> Cancelar(int id)
    {
        var ausencia = await Buscar(id);
        if (ausencia is null)
            return NotFound();
        if (!PodeCancelar(ausencia))
            return ausencia.FuncionarioId == usuario.FuncionarioId || usuario.VeTudo
                ? Problema(StatusCodes.Status409Conflict, "Só dá para cancelar pedidos pendentes ou ausências que ainda não começaram.")
                : Problema(StatusCodes.Status403Forbidden, "Só a própria pessoa ou o RH podem cancelar.");

        ausencia.Status = StatusAusencia.Cancelada;
        ausencia.DecididaEm = relogio.GetUtcNow();
        ausencia.DecididaPor = usuario.Assinatura;
        await contexto.SaveChangesAsync();
        return await SaidaAsync(ausencia);
    }

    private Task<Ausencia?> Buscar(int id) =>
        contexto.Ausencias.Include(a => a.Funcionario).ThenInclude(f => f!.Departamento).FirstOrDefaultAsync(a => a.Id == id);

    private bool PodeDecidir(Ausencia a, HashSet<int>? equipe) =>
        a.Status == StatusAusencia.Pendente && a.FuncionarioId != usuario.FuncionarioId
        && (usuario.VeTudo || (usuario.Perfil == Perfil.Gestor && (equipe?.Contains(a.FuncionarioId) ?? true)));

    private async Task<bool> PodeDecidirAsync(Ausencia a) => PodeDecidir(a, await visibilidade.EquipeAsync());

    private bool PodeCancelar(Ausencia a) =>
        (a.FuncionarioId == usuario.FuncionarioId || usuario.VeTudo)
        && (a.Status == StatusAusencia.Pendente || (a.Status == StatusAusencia.Aprovada && a.Inicio > Hoje));

    private async Task<AusenciaSaida> SaidaAsync(Ausencia a) => AusenciaSaida.De(a, await PodeDecidirAsync(a), PodeCancelar(a));

    private static string Nome(TipoAusencia t) => t switch
    {
        TipoAusencia.Ferias => "Férias",
        TipoAusencia.Licenca => "Licença",
        TipoAusencia.Atestado => "Atestado",
        _ => "Folga",
    };

    private ActionResult Invalido(string campo, string mensagem) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [campo] = [mensagem] }));

    private ObjectResult Problema(int status, string mensagem) => Problem(statusCode: status, title: mensagem);
}
