using Cracha.Api.Dados;
using Cracha.Api.Modelos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Controllers;

/// <summary>Departamentos da empresa, com quantidade de ativos e folha mensal.</summary>
[ApiController]
[Route("api/departamentos")]
[Produces("application/json")]
public class DepartamentosController(CrachaContext contexto) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<DepartamentoSaida>> Listar()
    {
        var departamentos = await contexto.Departamentos.AsNoTracking().Include(d => d.Funcionarios).OrderBy(d => d.Nome).ToListAsync();
        return departamentos.Select(Saida);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<DepartamentoSaida>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartamentoSaida>> ObterPorId(int id)
    {
        var departamento = await contexto.Departamentos.AsNoTracking().Include(d => d.Funcionarios).FirstOrDefaultAsync(d => d.Id == id);
        return departamento is null ? NotFound() : Saida(departamento);
    }

    [HttpPost]
    [ProducesResponseType<DepartamentoSaida>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartamentoSaida>> Criar(DepartamentoEntrada entrada)
    {
        var nome = entrada.Nome.Trim();
        if (await contexto.Departamentos.AnyAsync(d => d.Nome == nome))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Já existe um departamento com este nome.");

        var departamento = new Departamento { Nome = nome, Cor = entrada.Cor.ToLowerInvariant() };
        contexto.Departamentos.Add(departamento);
        await contexto.SaveChangesAsync();
        return CreatedAtAction(nameof(ObterPorId), new { id = departamento.Id }, Saida(departamento));
    }

    /// <summary>Renomeia ou troca a cor. Os registros antigos do histórico mantêm o nome da época.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<DepartamentoSaida>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartamentoSaida>> Atualizar(int id, DepartamentoEntrada entrada)
    {
        var departamento = await contexto.Departamentos.Include(d => d.Funcionarios).FirstOrDefaultAsync(d => d.Id == id);
        if (departamento is null)
            return NotFound();

        var nome = entrada.Nome.Trim();
        if (await contexto.Departamentos.AnyAsync(d => d.Nome == nome && d.Id != id))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Já existe um departamento com este nome.");

        departamento.Nome = nome;
        departamento.Cor = entrada.Cor.ToLowerInvariant();
        await contexto.SaveChangesAsync();
        return Saida(departamento);
    }

    /// <summary>Remove um departamento vazio. Com funcionários (mesmo desligados), responde 409.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remover(int id)
    {
        var departamento = await contexto.Departamentos.FindAsync(id);
        if (departamento is null)
            return NotFound();

        var pessoas = await contexto.Funcionarios.CountAsync(f => f.DepartamentoId == id);
        if (pessoas > 0)
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: $"O departamento tem {pessoas} funcionário(s). Transfira-os antes de remover.");

        contexto.Departamentos.Remove(departamento);
        await contexto.SaveChangesAsync();
        return NoContent();
    }

    private static DepartamentoSaida Saida(Departamento d)
    {
        var ativos = d.Funcionarios.Where(f => f.Situacao == Situacao.Ativo).ToList();
        return new(d.Id, d.Nome, d.Cor, ativos.Count, ativos.Sum(f => f.Salario));
    }
}
