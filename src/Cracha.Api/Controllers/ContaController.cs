using Cracha.Api.Dados;
using Cracha.Api.Modelos;
using Cracha.Api.Seguranca;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Controllers;

/// <summary>Login por cookie (HttpOnly, SameSite=Strict), sessão atual e troca de senha.</summary>
[ApiController]
[Route("api/conta")]
[Produces("application/json")]
public class ContaController(
    CrachaContext contexto, IPasswordHasher<Usuario> hasher, TimeProvider relogio, UsuarioAtual atual, OpcoesAcesso opcoes) : ControllerBase
{
    /// <summary>Entra no sistema. Limitado a 10 tentativas por minuto por IP.</summary>
    [HttpPost("entrar")]
    [AllowAnonymous]
    [EnableRateLimiting(ConfiguracaoAcesso.PoliticaLogin)]
    [ProducesResponseType<Sessao>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<Sessao>> Entrar(EntradaLogin entrada)
    {
        var email = entrada.Email.Trim().ToLowerInvariant();
        var usuario = await contexto.Usuarios.Include(u => u.Funcionario).FirstOrDefaultAsync(u => u.Email == email);

        // Mesma resposta para e-mail inexistente e senha errada: não revela quais e-mails existem.
        var resultado = usuario is null ? PasswordVerificationResult.Failed : hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, entrada.Senha);
        if (usuario is null || !usuario.Ativo || resultado == PasswordVerificationResult.Failed)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "E-mail ou senha inválidos.");

        if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
            usuario.SenhaHash = hasher.HashPassword(usuario, entrada.Senha);
        usuario.UltimoAcesso = relogio.GetUtcNow();
        await contexto.SaveChangesAsync();

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ConfiguracaoAcesso.Principal(usuario));
        return Sessao(usuario);
    }

    /// <summary>Sai do sistema.</summary>
    [HttpPost("sair")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Sair()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>Quem está logado e o que pode fazer. 401 se não houver sessão.</summary>
    [HttpGet("eu")]
    [ProducesResponseType<Sessao>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Sessao>> Eu()
    {
        var usuario = await contexto.Usuarios.AsNoTracking().Include(u => u.Funcionario).FirstOrDefaultAsync(u => u.Id == atual.Id);
        return usuario is null ? Unauthorized() : Sessao(usuario);
    }

    /// <summary>Troca a própria senha. As outras sessões abertas são encerradas.</summary>
    [HttpPost("senha")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TrocarSenha(TrocaSenha entrada)
    {
        var usuario = await contexto.Usuarios.FirstAsync(u => u.Id == atual.Id);
        if (hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, entrada.Atual) == PasswordVerificationResult.Failed)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["atual"] = ["Senha atual incorreta."] }));

        usuario.SenhaHash = hasher.HashPassword(usuario, entrada.Nova);
        usuario.Carimbo = Guid.NewGuid().ToString("N");
        await contexto.SaveChangesAsync();
        // Renova o cookie desta sessão com o carimbo novo.
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ConfiguracaoAcesso.Principal(usuario));
        return NoContent();
    }

    /// <summary>Contas de demonstração mostradas na tela de login (só quando habilitado).</summary>
    [HttpGet("demonstracao")]
    [AllowAnonymous]
    public async Task<IEnumerable<ContaDemonstracao>> Demonstracao()
    {
        if (!opcoes.Demonstracao)
            return [];
        var descricoes = new Dictionary<Perfil, string>
        {
            [Perfil.Administrador] = "Tudo, inclusive gerenciar acessos",
            [Perfil.RH] = "Cadastro completo, salários e aprovações",
            [Perfil.Gestor] = "Painel e aprovações da própria equipe",
            [Perfil.Colaborador] = "Diretório, os próprios dados e férias",
        };
        var existentes = await contexto.Usuarios.AsNoTracking().Where(u => u.Ativo).Select(u => u.Email).ToListAsync();
        return ConfiguracaoAcesso.ContasDemonstracao
            .Where(c => existentes.Contains(c.Email))
            .Select(c => new ContaDemonstracao(c.Nome, c.Email, c.Perfil, c.Email == "helena.prado@cracha.dev"
                ? "Diretora: a empresa inteira é a equipe dela" : descricoes[c.Perfil]));
    }

    private static Sessao Sessao(Usuario u)
    {
        var veTudo = u.Perfil is Perfil.Administrador or Perfil.RH;
        var foto = u.Funcionario?.FotoVersao is { } v ? $"/api/funcionarios/{u.FuncionarioId}/foto?v={v}" : null;
        return new Sessao(u.Id, u.Nome, u.Email, u.Perfil, u.FuncionarioId, foto, new Permissoes(
            GerirCadastro: veTudo,
            Administrar: u.Perfil == Perfil.Administrador,
            Acompanhar: veTudo || u.Perfil == Perfil.Gestor,
            AprovarAusencias: veTudo || u.Perfil == Perfil.Gestor,
            VerSalarios: veTudo));
    }
}
