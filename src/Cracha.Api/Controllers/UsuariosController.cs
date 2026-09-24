using Cracha.Api.Dados;
using Cracha.Api.Modelos;
using Cracha.Api.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Controllers;

/// <summary>Gestão de acessos (somente administrador).</summary>
[ApiController]
[Route("api/usuarios")]
[Produces("application/json")]
[Authorize(Policy = Politicas.Administrar)]
public class UsuariosController(CrachaContext contexto, IPasswordHasher<Usuario> hasher, TimeProvider relogio, UsuarioAtual atual) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<UsuarioSaida>> Listar() =>
        (await contexto.Usuarios.AsNoTracking().Include(u => u.Funcionario).OrderBy(u => u.Perfil).ThenBy(u => u.Nome).ToListAsync())
            .Select(UsuarioSaida.De);

    /// <summary>Cria um acesso. Gestor e colaborador precisam estar ligados a um funcionário.</summary>
    [HttpPost]
    [ProducesResponseType<UsuarioSaida>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UsuarioSaida>> Criar(UsuarioEntrada entrada)
    {
        if (string.IsNullOrEmpty(entrada.Senha))
            return Invalido("senha", "Defina a senha inicial.");
        if (await Validar(entrada, null) is { } erro)
            return erro;

        var usuario = new Usuario { CriadoEm = relogio.GetUtcNow() };
        Aplicar(entrada, usuario);
        usuario.SenhaHash = hasher.HashPassword(usuario, entrada.Senha);
        contexto.Usuarios.Add(usuario);
        await contexto.SaveChangesAsync();
        await contexto.Entry(usuario).Reference(u => u.Funcionario).LoadAsync();
        return Created($"/api/usuarios/{usuario.Id}", UsuarioSaida.De(usuario));
    }

    /// <summary>Altera perfil, vínculo, situação ou senha. Mudanças derrubam as sessões abertas do usuário.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<UsuarioSaida>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UsuarioSaida>> Atualizar(int id, UsuarioEntrada entrada)
    {
        var usuario = await contexto.Usuarios.Include(u => u.Funcionario).FirstOrDefaultAsync(u => u.Id == id);
        if (usuario is null)
            return NotFound();
        if (id == atual.Id && (!entrada.Ativo || entrada.Perfil != Perfil.Administrador))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Você não pode rebaixar nem desativar o próprio acesso.");
        if (await Validar(entrada, id) is { } erro)
            return erro;

        var mudouAcesso = usuario.Perfil != entrada.Perfil || usuario.Ativo != entrada.Ativo
            || usuario.FuncionarioId != entrada.FuncionarioId || !string.IsNullOrEmpty(entrada.Senha);
        Aplicar(entrada, usuario);
        if (!string.IsNullOrEmpty(entrada.Senha))
            usuario.SenhaHash = hasher.HashPassword(usuario, entrada.Senha);
        if (mudouAcesso)
            usuario.Carimbo = Guid.NewGuid().ToString("N");
        await contexto.SaveChangesAsync();
        await contexto.Entry(usuario).Reference(u => u.Funcionario).LoadAsync();
        return UsuarioSaida.De(usuario);
    }

    private static void Aplicar(UsuarioEntrada e, Usuario u)
    {
        u.Nome = e.Nome.Trim();
        u.Email = e.Email.Trim().ToLowerInvariant();
        u.Perfil = e.Perfil;
        u.FuncionarioId = e.FuncionarioId;
        u.Ativo = e.Ativo;
    }

    private async Task<ActionResult?> Validar(UsuarioEntrada entrada, int? id)
    {
        if (entrada.Perfil is (Perfil.Gestor or Perfil.Colaborador) && entrada.FuncionarioId is null)
            return Invalido("funcionarioId", "Gestor e colaborador precisam estar ligados a um funcionário.");
        if (entrada.FuncionarioId is int f && !await contexto.Funcionarios.AnyAsync(x => x.Id == f))
            return Invalido("funcionarioId", "Funcionário não encontrado.");
        if (entrada.FuncionarioId is int g && await contexto.Usuarios.AnyAsync(u => u.FuncionarioId == g && u.Id != id))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Este funcionário já tem um acesso.");

        var email = entrada.Email.Trim().ToLowerInvariant();
        if (await contexto.Usuarios.AnyAsync(u => u.Email == email && u.Id != id))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Já existe um acesso com este e-mail.");
        return null;
    }

    private ActionResult Invalido(string campo, string mensagem) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [campo] = [mensagem] }));
}
