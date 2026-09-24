using Cracha.Api.Dados;
using Cracha.Api.Modelos;
using Cracha.Api.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Controllers;

public enum OrdemFuncionarios { Nome, Admissao, Salario, Departamento }

/// <summary>
/// Cadastro de funcionários. Todos os perfis consultam o diretório; salário e endereço só aparecem para
/// o RH, o gestor da pessoa e a própria pessoa. Alterações exigem o perfil RH ou Administrador e
/// geram um registro no histórico com o autor.
/// </summary>
[ApiController]
[Route("api/funcionarios")]
[Produces("application/json")]
public class FuncionariosController(
    CrachaContext contexto, IHistorico historico, TimeProvider relogio, UsuarioAtual usuario, Visibilidade visibilidade) : ControllerBase
{
    private DateOnly Hoje => DateOnly.FromDateTime(relogio.GetLocalNow().DateTime);

    /// <summary>Lista funcionários com busca, filtros e ordenação.</summary>
    /// <param name="busca">Trecho do nome, cargo, e-mail ou ramal.</param>
    /// <param name="departamentoId">Só deste departamento.</param>
    /// <param name="gestorId">Só a equipe direta deste gestor.</param>
    /// <param name="situacao">Ativo ou Desligado. Vazio traz todos.</param>
    /// <param name="ordem">Nome, Admissao, Salario ou Departamento.</param>
    /// <param name="desc">Ordem decrescente.</param>
    [HttpGet]
    public async Task<IEnumerable<FuncionarioSaida>> Listar(
        string? busca, int? departamentoId, int? gestorId, Situacao? situacao,
        OrdemFuncionarios ordem = OrdemFuncionarios.Nome, bool desc = false)
    {
        var consulta = contexto.Funcionarios.AsNoTracking().Include(f => f.Departamento).Include(f => f.Gestor).AsQueryable();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim().ToLower();
            consulta = consulta.Where(f => f.Nome.ToLower().Contains(termo) || f.Cargo.ToLower().Contains(termo)
                || f.EmailProfissional.ToLower().Contains(termo) || f.Ramal.Contains(termo));
        }
        if (departamentoId is int d)
            consulta = consulta.Where(f => f.DepartamentoId == d);
        if (gestorId is int g)
            consulta = consulta.Where(f => f.GestorId == g);
        if (situacao is Situacao s)
            consulta = consulta.Where(f => f.Situacao == s);

        var funcionarios = await consulta.ToListAsync();
        var equipe = await visibilidade.EquipeAsync();
        var subordinados = await ContarSubordinadosAsync();
        var ausentes = await AusentesHojeAsync();

        // Ordena em memória: o SQLite não ordena decimal, e a lista de uma empresa cabe folgada.
        // Quem não vê salários não pode ordenar por eles (a ordem revelaria a faixa salarial).
        if (ordem == OrdemFuncionarios.Salario && equipe is not null)
            (ordem, desc) = (OrdemFuncionarios.Nome, false);
        var lista = funcionarios.Select(f => FuncionarioSaida.De(f, Hoje, equipe?.Contains(f.Id) ?? true,
            subordinados.GetValueOrDefault(f.Id), ausentes.GetValueOrDefault(f.Id)));
        return ordem switch
        {
            OrdemFuncionarios.Admissao => desc ? lista.OrderByDescending(f => f.DataAdmissao) : lista.OrderBy(f => f.DataAdmissao),
            OrdemFuncionarios.Salario => desc ? lista.OrderByDescending(f => f.Salario) : lista.OrderBy(f => f.Salario),
            OrdemFuncionarios.Departamento => (desc ? lista.OrderByDescending(f => f.Departamento) : lista.OrderBy(f => f.Departamento)).ThenBy(f => f.Nome),
            _ => desc ? lista.OrderByDescending(f => f.Nome) : lista.OrderBy(f => f.Nome),
        };
    }

    /// <summary>Obtém um funcionário pelo id.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<FuncionarioSaida>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FuncionarioSaida>> ObterPorId(int id)
    {
        var funcionario = await Buscar(id);
        return funcionario is null ? NotFound() : await SaidaAsync(funcionario);
    }

    /// <summary>Cadastra um funcionário e registra a inclusão no histórico.</summary>
    [HttpPost]
    [Authorize(Policy = Politicas.GerirCadastro)]
    [ProducesResponseType<FuncionarioSaida>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FuncionarioSaida>> Criar(FuncionarioEntrada entrada)
    {
        if (await Validar(entrada, null) is { } erro)
            return erro;

        var funcionario = new Funcionario();
        Aplicar(entrada, funcionario);
        contexto.Funcionarios.Add(funcionario);
        await contexto.SaveChangesAsync();
        await RecarregarReferenciasAsync(funcionario);

        await Registrar(TipoAcao.Inclusao, null, funcionario);
        return CreatedAtAction(nameof(ObterPorId), new { id = funcionario.Id }, await SaidaAsync(funcionario));
    }

    /// <summary>Atualiza todos os dados de um funcionário e registra o que mudou.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Politicas.GerirCadastro)]
    [ProducesResponseType<FuncionarioSaida>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FuncionarioSaida>> Atualizar(int id, FuncionarioEntrada entrada)
    {
        var funcionario = await Buscar(id);
        if (funcionario is null)
            return NotFound();
        if (await Validar(entrada, id) is { } erro)
            return erro;

        var antes = Foto(funcionario);
        Aplicar(entrada, funcionario);
        await contexto.SaveChangesAsync();
        await RecarregarReferenciasAsync(funcionario);

        // Salvar sem mudar nada não polui o histórico.
        if (FotoFuncionario.Comparar(antes, Foto(funcionario)).Count > 0)
            await Registrar(TipoAcao.Atualizacao, antes, funcionario);
        return await SaidaAsync(funcionario);
    }

    /// <summary>Desliga o funcionário (ele continua no cadastro, com a data de saída).</summary>
    /// <remarks>Quem ainda lidera uma equipe ativa precisa ter a equipe transferida antes (409).</remarks>
    [HttpPost("{id:int}/desligar")]
    [Authorize(Policy = Politicas.GerirCadastro)]
    [ProducesResponseType<FuncionarioSaida>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FuncionarioSaida>> Desligar(int id, DesligamentoEntrada? entrada)
    {
        var funcionario = await Buscar(id);
        if (funcionario is null)
            return NotFound();
        if (funcionario.Situacao == Situacao.Desligado)
            return Problema(StatusCodes.Status409Conflict, "Este funcionário já está desligado.");
        if (await EquipeAtivaAsync(id) is > 0 and var equipe)
            return Problema(StatusCodes.Status409Conflict, $"{funcionario.Nome} lidera {equipe} pessoa(s) ativa(s). Transfira a equipe para outro gestor antes do desligamento.");

        var data = entrada?.Data ?? Hoje;
        if (data < funcionario.DataAdmissao)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["data"] = ["O desligamento não pode ser antes da admissão."],
            }));

        var antes = Foto(funcionario);
        funcionario.Situacao = Situacao.Desligado;
        funcionario.DataDesligamento = data;

        // Pedidos de ausência em aberto perdem o sentido.
        var pendentes = await contexto.Ausencias.Where(a => a.FuncionarioId == id && a.Status == StatusAusencia.Pendente).ToListAsync();
        pendentes.ForEach(a => { a.Status = StatusAusencia.Cancelada; a.DecididaPor = usuario.Assinatura; a.DecididaEm = relogio.GetUtcNow(); });
        await contexto.SaveChangesAsync();

        await Registrar(TipoAcao.Desligamento, antes, funcionario);
        return await SaidaAsync(funcionario);
    }

    /// <summary>Reativa um funcionário desligado.</summary>
    [HttpPost("{id:int}/reativar")]
    [Authorize(Policy = Politicas.GerirCadastro)]
    [ProducesResponseType<FuncionarioSaida>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FuncionarioSaida>> Reativar(int id)
    {
        var funcionario = await Buscar(id);
        if (funcionario is null)
            return NotFound();
        if (funcionario.Situacao == Situacao.Ativo)
            return Problema(StatusCodes.Status409Conflict, "Este funcionário já está ativo.");

        var antes = Foto(funcionario);
        funcionario.Situacao = Situacao.Ativo;
        funcionario.DataDesligamento = null;
        await contexto.SaveChangesAsync();

        await Registrar(TipoAcao.Reativacao, antes, funcionario);
        return await SaidaAsync(funcionario);
    }

    /// <summary>Remove o funcionário do cadastro. O histórico dele é mantido.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Politicas.GerirCadastro)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remover(int id)
    {
        var funcionario = await Buscar(id);
        if (funcionario is null)
            return NotFound();
        if (await EquipeAtivaAsync(id) is > 0 and var equipe)
            return Problema(StatusCodes.Status409Conflict, $"{funcionario.Nome} lidera {equipe} pessoa(s) ativa(s). Transfira a equipe antes de remover.");

        // Ex-subordinados já desligados ficam sem gestor; o histórico guarda quem era.
        await contexto.Funcionarios.Where(f => f.GestorId == id).ExecuteUpdateAsync(s => s.SetProperty(f => f.GestorId, (int?)null));
        contexto.Funcionarios.Remove(funcionario);
        await contexto.SaveChangesAsync();

        await Registrar(TipoAcao.Remocao, null, funcionario);
        return NoContent();
    }

    /// <summary>Linha do tempo de um funcionário, da mais nova para a mais antiga.</summary>
    /// <remarks>RH vê de todos; gestor, da equipe; colaborador, só a própria.</remarks>
    [HttpGet("{id:int}/historico")]
    [ProducesResponseType<IEnumerable<HistoricoSaida>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<HistoricoSaida>>> Historico(int id)
    {
        if (!await visibilidade.PodeVerAsync(id))
            return Problema(StatusCodes.Status403Forbidden, "Você não tem acesso ao histórico desta pessoa.");
        var registros = await historico.ListarAsync(new FiltroHistorico(FuncionarioId: id, Limite: 500));
        return Ok(registros.Select(HistoricoSaida.De));
    }

    private Task<Funcionario?> Buscar(int id) =>
        contexto.Funcionarios.Include(f => f.Departamento).Include(f => f.Gestor).FirstOrDefaultAsync(f => f.Id == id);

    private async Task<FuncionarioSaida> SaidaAsync(Funcionario f)
    {
        var subordinados = await contexto.Funcionarios.CountAsync(s => s.GestorId == f.Id && s.Situacao == Situacao.Ativo);
        return FuncionarioSaida.De(f, Hoje, await visibilidade.PodeVerAsync(f.Id), subordinados, (await AusentesHojeAsync(f.Id)).GetValueOrDefault(f.Id));
    }

    private async Task<Dictionary<int, int>> ContarSubordinadosAsync() =>
        await contexto.Funcionarios.AsNoTracking()
            .Where(f => f.GestorId != null && f.Situacao == Situacao.Ativo)
            .GroupBy(f => f.GestorId!.Value)
            .Select(g => new { Gestor = g.Key, Total = g.Count() })
            .ToDictionaryAsync(g => g.Gestor, g => g.Total);

    private async Task<Dictionary<int, AusenciaResumo>> AusentesHojeAsync(int? funcionarioId = null)
    {
        var hoje = Hoje;
        var consulta = contexto.Ausencias.AsNoTracking()
            .Where(a => a.Status == StatusAusencia.Aprovada && a.Inicio <= hoje && a.Fim >= hoje);
        if (funcionarioId is int id)
            consulta = consulta.Where(a => a.FuncionarioId == id);
        var lista = await consulta.ToListAsync();
        return lista.GroupBy(a => a.FuncionarioId).ToDictionary(g => g.Key, g => new AusenciaResumo(g.First().Tipo, g.Max(a => a.Fim)));
    }

    private Task<int> EquipeAtivaAsync(int id) =>
        contexto.Funcionarios.CountAsync(f => f.GestorId == id && f.Situacao == Situacao.Ativo);

    private async Task RecarregarReferenciasAsync(Funcionario f)
    {
        f.Departamento = await contexto.Departamentos.FindAsync(f.DepartamentoId);
        f.Gestor = f.GestorId is int g ? await contexto.Funcionarios.FindAsync(g) : null;
    }

    private static FotoFuncionario Foto(Funcionario f) => FotoFuncionario.De(f, f.Departamento?.Nome ?? "", f.Gestor?.Nome);

    private Task Registrar(TipoAcao tipo, FotoFuncionario? antes, Funcionario funcionario) =>
        historico.RegistrarAsync(Auditoria.Registro(tipo, antes, Foto(funcionario), relogio.GetLocalNow(), usuario.Assinatura));

    private static void Aplicar(FuncionarioEntrada e, Funcionario f)
    {
        f.Nome = e.Nome.Trim();
        f.Cargo = e.Cargo.Trim();
        f.Endereco = e.Endereco?.Trim() ?? "";
        f.Ramal = e.Ramal;
        f.EmailProfissional = e.EmailProfissional.Trim().ToLowerInvariant();
        f.DepartamentoId = e.DepartamentoId;
        f.GestorId = e.GestorId;
        f.Salario = Math.Round(e.Salario, 2);
        f.DataAdmissao = e.DataAdmissao!.Value;
    }

    private async Task<ActionResult?> Validar(FuncionarioEntrada entrada, int? id)
    {
        if (!await contexto.Departamentos.AnyAsync(d => d.Id == entrada.DepartamentoId))
            return Invalido("departamentoId", "Departamento não encontrado.");

        if (entrada.GestorId is int gestorId)
        {
            if (gestorId == id)
                return Invalido("gestorId", "A pessoa não pode ser gestora de si mesma.");
            var gestor = await contexto.Funcionarios.AsNoTracking().FirstOrDefaultAsync(f => f.Id == gestorId);
            if (gestor is null || gestor.Situacao != Situacao.Ativo)
                return Invalido("gestorId", "Escolha um gestor ativo.");

            // Sobe a cadeia a partir do novo gestor: se chegar na própria pessoa, seria um ciclo.
            if (id is int eu)
            {
                var cadeia = await contexto.Funcionarios.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.GestorId);
                for (int? atual = gestorId, passos = 0; atual is int a && passos < cadeia.Count; atual = cadeia.GetValueOrDefault(a), passos++)
                    if (a == eu)
                        return Invalido("gestorId", "Esse gestor faz parte da equipe desta pessoa (o organograma ficaria em círculo).");
            }
        }

        var email = entrada.EmailProfissional.Trim().ToLowerInvariant();
        if (await contexto.Funcionarios.AnyAsync(f => f.EmailProfissional == email && f.Id != id))
            return Problema(StatusCodes.Status409Conflict, "Já existe um funcionário com este e-mail.");
        return null;
    }

    private ActionResult Invalido(string campo, string mensagem) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [campo] = [mensagem] }));

    private ObjectResult Problema(int status, string mensagem) => Problem(statusCode: status, title: mensagem);
}
