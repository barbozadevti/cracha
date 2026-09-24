using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Cracha.Api.Dados;
using Cracha.Api.Modelos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Seguranca;

public sealed class OpcoesAcesso
{
    /// <summary>Mostra as contas de demonstração na tela de login (portfólio). Desligue em produção.</summary>
    public bool Demonstracao { get; set; } = true;

    /// <summary>Senha das contas de demonstração.</summary>
    public string SenhaDemonstracao { get; set; } = "Cracha@2026";

    /// <summary>Senha inicial do admin quando não há exemplos. Vazia: gera uma aleatória e mostra no log.</summary>
    public string? SenhaAdministrador { get; set; }

    /// <summary>Tentativas de login por minuto por IP.</summary>
    public int TentativasPorMinuto { get; set; } = 10;
}

public static class ConfiguracaoAcesso
{
    public const string PoliticaLogin = "login";

    public static IServiceCollection AdicionarAcesso(this IServiceCollection services, OpcoesAcesso opcoes)
    {
        services.AddSingleton(opcoes);
        services.AddHttpContextAccessor();
        services.AddScoped<UsuarioAtual>();
        services.AddScoped<Visibilidade>();
        services.AddSingleton<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "cracha.sessao";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict; // barra CSRF: o cookie não vai em requisições de outros sites
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromHours(8);
                o.SlidingExpiration = true;
                // API: responde 401/403 em vez de redirecionar para uma página de login.
                o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
                o.Events.OnValidatePrincipal = ValidarSessaoAsync;
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Politicas.GerirCadastro, p => p.RequireRole(nameof(Perfil.Administrador), nameof(Perfil.RH)))
            .AddPolicy(Politicas.Administrar, p => p.RequireRole(nameof(Perfil.Administrador)))
            .AddPolicy(Politicas.Acompanhar, p => p.RequireRole(nameof(Perfil.Administrador), nameof(Perfil.RH), nameof(Perfil.Gestor)));

        // Poucas tentativas de login por minuto por IP: freia ataques de força bruta.
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(PoliticaLogin, contexto => RateLimitPartition.GetFixedWindowLimiter(
                contexto.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = opcoes.TentativasPorMinuto, Window = TimeSpan.FromMinutes(1) }));
        });
        return services;
    }

    public static ClaimsPrincipal Principal(Usuario u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Nome),
            new(ClaimTypes.Email, u.Email),
            new(ClaimTypes.Role, u.Perfil.ToString()),
            new(Reivindicacoes.Carimbo, u.Carimbo),
        };
        if (u.FuncionarioId is int f)
            claims.Add(new Claim(Reivindicacoes.Funcionario, f.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>A cada requisição confere se o acesso segue ativo e sem mudanças de perfil ou senha.</summary>
    private static async Task ValidarSessaoAsync(CookieValidatePrincipalContext contexto)
    {
        var id = int.TryParse(contexto.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var n) ? n : 0;
        var carimbo = contexto.Principal?.FindFirstValue(Reivindicacoes.Carimbo);
        var banco = contexto.HttpContext.RequestServices.GetRequiredService<CrachaContext>();
        var valido = await banco.Usuarios.AsNoTracking().AnyAsync(u => u.Id == id && u.Ativo && u.Carimbo == carimbo);
        if (!valido)
        {
            contexto.RejectPrincipal();
            await contexto.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    /// <summary>
    /// Cria os acessos iniciais quando ainda não há nenhum: o administrador e, com os dados de exemplo,
    /// uma conta por perfil ligada a funcionários reais do cadastro.
    /// </summary>
    public static async Task PrepararAcessosAsync(this IServiceProvider services, bool comExemplos)
    {
        await using var escopo = services.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<CrachaContext>();
        if (await contexto.Usuarios.AnyAsync())
            return;

        var opcoes = escopo.ServiceProvider.GetRequiredService<OpcoesAcesso>();
        var hasher = escopo.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>();
        var agora = escopo.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var log = escopo.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Cracha.Acesso");

        Usuario Novo(string nome, string email, Perfil perfil, string senha, int? funcionarioId = null)
        {
            var u = new Usuario { Nome = nome, Email = email, Perfil = perfil, FuncionarioId = funcionarioId, CriadoEm = agora };
            u.SenhaHash = hasher.HashPassword(u, senha);
            return u;
        }

        if (!comExemplos)
        {
            var senha = string.IsNullOrWhiteSpace(opcoes.SenhaAdministrador)
                ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(12))
                : opcoes.SenhaAdministrador;
            contexto.Usuarios.Add(Novo("Administrador", "admin@cracha.dev", Perfil.Administrador, senha));
            await contexto.SaveChangesAsync();
            if (string.IsNullOrWhiteSpace(opcoes.SenhaAdministrador))
                log.LogWarning("Acesso inicial criado: admin@cracha.dev / {Senha}. Troque a senha no primeiro acesso.", senha);
            return;
        }

        var porEmail = await contexto.Funcionarios.AsNoTracking().ToDictionaryAsync(f => f.EmailProfissional, f => f.Id);
        var senhaDemo = opcoes.SenhaDemonstracao;
        contexto.Usuarios.Add(Novo("Administrador", "admin@cracha.dev", Perfil.Administrador, senhaDemo));
        foreach (var (nome, email, perfil) in ContasDemonstracao.Where(c => c.Perfil != Perfil.Administrador))
            if (porEmail.TryGetValue(email, out var funcionarioId))
                contexto.Usuarios.Add(Novo(nome, email, perfil, senhaDemo, funcionarioId));
        await contexto.SaveChangesAsync();
    }

    public static readonly (string Nome, string Email, Perfil Perfil)[] ContasDemonstracao =
    [
        ("Administrador", "admin@cracha.dev", Perfil.Administrador),
        ("Gabriela Nunes", "gabriela.nunes@cracha.dev", Perfil.RH),
        ("Helena Prado", "helena.prado@cracha.dev", Perfil.Gestor),
        ("Bruno Carvalho", "bruno.carvalho@cracha.dev", Perfil.Gestor),
        ("Ana Beatriz Souza", "ana.souza@cracha.dev", Perfil.Colaborador),
    ];
}
