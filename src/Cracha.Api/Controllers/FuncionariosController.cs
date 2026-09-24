using Cracha.Api.Dados;
using Cracha.Api.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Controllers;

public enum OrdemFuncionarios { Nome, Admissao, Salario, Departamento }

/// <summary>Cadastro de funcionários. Toda alteração gera um registro no histórico.</summary>
[ApiController]
[Route("api/funcionarios")]
[Produces("application/json")]
public class FuncionariosController(CrachaContext contexto, IHistorico historico, TimeProvider relogio) : ControllerBase
{
    private DateOnly Hoje => DateOnly.FromDateTime(relogio.GetLocalNow().DateTime);

    /// <summary>Lista funcionários com busca, filtros e ordenação.</summary>
    /// <param name="busca">Trecho do nome, cargo, e-mail ou ramal.</param>
    /// <param name="departamentoId">Só deste departamento.</param>
    /// <param name="situacao">Ativo ou Desligado. Vazio traz todos.</param>
    /// <param name="ordem">Nome, Admissao, Salario ou Departamento.</param>
    /// <param name="desc">Ordem decrescente.</param>
    [HttpGet]
    public async Task<IEnumerable<FuncionarioSaida>> Listar(
        string? busca, int? departamentoId, Situacao? situacao,
        OrdemFuncionarios ordem = OrdemFuncionarios.Nome, bool desc = false)
    {
        var consulta = contexto.Funcionarios.AsNoTracking().Include(f => f.Departamento).AsQueryable();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim().ToLower();
            consulta = consulta.Where(f => f.Nome.ToLower().Contains(termo) || f.Cargo.ToLower().Contains(termo)
                || f.EmailProfissional.ToLower().Contains(termo) || f.Ramal.Contains(termo));
        }
        if (departamentoId is int d)
            consulta = consulta.Where(f => f.DepartamentoId == d);
        if (situacao is Situacao s)
            consulta = consulta.Where(f => f.Situacao == s);

        // Ordena em memória: o SQLite não ordena decimal, e a lista de uma empresa cabe folgada.
        var lista = (await consulta.ToListAsync()).Select(f => FuncionarioSaida.De(f, Hoje));
        lista = ordem switch
        {
            OrdemFuncionarios.Admissao => desc ? lista.OrderByDescending(f => f.DataAdmissao) : lista.OrderBy(f => f.DataAdmissao),
            OrdemFuncionarios.Salario => desc ? lista.OrderByDescending(f => f.Salario) : lista.OrderBy(f => f.Salario),
            OrdemFuncionarios.Departamento => (desc ? lista.OrderByDescending(f => f.Departamento) : lista.OrderBy(f => f.Departamento)).ThenBy(f => f.Nome),
            _ => desc ? lista.OrderByDescending(f => f.Nome) : lista.OrderBy(f => f.Nome),
        };
        return lista;
    }

    /// <summary>Obtém um funcionário pelo id.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<FuncionarioSaida>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FuncionarioSaida>> ObterPorId(int id)
    {
        var funcionario = await Buscar(id);
        return funcionario is null ? NotFound() : FuncionarioSaida.De(funcionario, Hoje);
    }

    /// <summary>Cadastra um funcionário e registra a inclusão no histórico.</summary>
    [HttpPost]
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
        funcionario.Departamento = await contexto.Departamentos.FindAsync(funcionario.DepartamentoId);

        await Registrar(TipoAcao.Inclusao, null, funcionario);
        return CreatedAtAction(nameof(ObterPorId), new { id = funcionario.Id }, FuncionarioSaida.De(funcionario, Hoje));
    }

    /// <summary>Atualiza todos os dados de um funcionário e registra o que mudou.</summary>
    [HttpPut("{id:int}")]
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
        funcionario.Departamento = await contexto.Departamentos.FindAsync(funcionario.DepartamentoId);

        // Salvar sem mudar nada não polui o histórico.
        if (FotoFuncionario.Comparar(antes, Foto(funcionario)).Count > 0)
            await Registrar(TipoAcao.Atualizacao, antes, funcionario);
        return FuncionarioSaida.De(funcionario, Hoje);
    }

    /// <summary>Desliga o funcionário (ele continua no cadastro, com a data de saída).</summary>
    [HttpPost("{id:int}/desligar")]
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

        var data = entrada?.Data ?? Hoje;
        if (data < funcionario.DataAdmissao)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["data"] = ["O desligamento não pode ser antes da admissão."],
            }));

        var antes = Foto(funcionario);
        funcionario.Situacao = Situacao.Desligado;
        funcionario.DataDesligamento = data;
        await contexto.SaveChangesAsync();

        await Registrar(TipoAcao.Desligamento, antes, funcionario);
        return FuncionarioSaida.De(funcionario, Hoje);
    }

    /// <summary>Reativa um funcionário desligado.</summary>
    [HttpPost("{id:int}/reativar")]
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
        return FuncionarioSaida.De(funcionario, Hoje);
    }

    /// <summary>Remove o funcionário do cadastro. O histórico dele é mantido.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remover(int id)
    {
        var funcionario = await Buscar(id);
        if (funcionario is null)
            return NotFound();

        contexto.Funcionarios.Remove(funcionario);
        await contexto.SaveChangesAsync();

        await Registrar(TipoAcao.Remocao, null, funcionario);
        return NoContent();
    }

    /// <summary>Linha do tempo de um funcionário, da mais nova para a mais antiga.</summary>
    [HttpGet("{id:int}/historico")]
    public async Task<IEnumerable<HistoricoSaida>> Historico(int id) =>
        (await historico.ListarAsync(new FiltroHistorico(FuncionarioId: id, Limite: 500))).Select(HistoricoSaida.De);

    private Task<Funcionario?> Buscar(int id) =>
        contexto.Funcionarios.Include(f => f.Departamento).FirstOrDefaultAsync(f => f.Id == id);

    private static FotoFuncionario Foto(Funcionario f) => FotoFuncionario.De(f, f.Departamento?.Nome ?? "");

    private Task Registrar(TipoAcao tipo, FotoFuncionario? antes, Funcionario funcionario) =>
        historico.RegistrarAsync(Auditoria.Registro(tipo, antes, Foto(funcionario), relogio.GetLocalNow()));

    private static void Aplicar(FuncionarioEntrada e, Funcionario f)
    {
        f.Nome = e.Nome.Trim();
        f.Cargo = e.Cargo.Trim();
        f.Endereco = e.Endereco?.Trim() ?? "";
        f.Ramal = e.Ramal;
        f.EmailProfissional = e.EmailProfissional.Trim().ToLowerInvariant();
        f.DepartamentoId = e.DepartamentoId;
        f.Salario = Math.Round(e.Salario, 2);
        f.DataAdmissao = e.DataAdmissao!.Value;
    }

    private async Task<ActionResult?> Validar(FuncionarioEntrada entrada, int? id)
    {
        if (!await contexto.Departamentos.AnyAsync(d => d.Id == entrada.DepartamentoId))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["departamentoId"] = ["Departamento não encontrado."],
            }));

        var email = entrada.EmailProfissional.Trim().ToLowerInvariant();
        if (await contexto.Funcionarios.AnyAsync(f => f.EmailProfissional == email && f.Id != id))
            return Problema(StatusCodes.Status409Conflict, "Já existe um funcionário com este e-mail.");
        return null;
    }

    private ObjectResult Problema(int status, string mensagem) => Problem(statusCode: status, title: mensagem);
}
