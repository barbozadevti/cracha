using System.Security.Claims;
using Cracha.Api.Dados;
using Cracha.Api.Modelos;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Seguranca;

public static class Politicas
{
    /// <summary>Cadastrar, editar, desligar e remover funcionários e departamentos.</summary>
    public const string GerirCadastro = "GerirCadastro";

    /// <summary>Criar e administrar acessos.</summary>
    public const string Administrar = "Administrar";

    /// <summary>Painel e histórico geral (RH vê tudo; gestor vê a equipe).</summary>
    public const string Acompanhar = "Acompanhar";
}

public static class Reivindicacoes
{
    public const string Funcionario = "cracha:funcionario";
    public const string Carimbo = "cracha:carimbo";
}

/// <summary>Quem está usando o sistema nesta requisição.</summary>
public sealed class UsuarioAtual(IHttpContextAccessor http)
{
    private ClaimsPrincipal Principal => http.HttpContext?.User ?? new ClaimsPrincipal();

    public bool Autenticado => Principal.Identity?.IsAuthenticated == true;
    public int Id => int.Parse(Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
    public string Nome => Principal.FindFirstValue(ClaimTypes.Name) ?? "Sistema";
    public Perfil Perfil => Enum.TryParse<Perfil>(Principal.FindFirstValue(ClaimTypes.Role), out var p) ? p : Perfil.Colaborador;
    public int? FuncionarioId => int.TryParse(Principal.FindFirstValue(Reivindicacoes.Funcionario), out var id) ? id : null;

    /// <summary>Administrador e RH enxergam e alteram todo o cadastro.</summary>
    public bool VeTudo => Perfil is Perfil.Administrador or Perfil.RH;

    /// <summary>Como o autor aparece no histórico: "Gabriela Nunes (RH)".</summary>
    public string Assinatura => Autenticado ? $"{Nome} ({Perfil})" : "Sistema";
}

/// <summary>
/// Regras de visibilidade (LGPD): salário, endereço e histórico só para o RH, para o gestor da
/// pessoa (equipe direta e indireta) e para a própria pessoa.
/// </summary>
public sealed class Visibilidade(CrachaContext contexto, UsuarioAtual usuario)
{
    private HashSet<int>? _equipe;
    private bool _carregada;

    /// <summary>Ids que o usuário pode ver em detalhe; null = todos.</summary>
    public async Task<HashSet<int>?> EquipeAsync()
    {
        if (_carregada)
            return _equipe;
        _carregada = true;
        if (usuario.VeTudo)
            return _equipe = null;

        _equipe = [];
        if (usuario.FuncionarioId is not int eu)
            return _equipe;
        _equipe.Add(eu);
        if (usuario.Perfil != Perfil.Gestor)
            return _equipe;

        var gestores = await contexto.Funcionarios.AsNoTracking()
            .Where(f => f.GestorId != null)
            .Select(f => new { f.Id, GestorId = f.GestorId!.Value })
            .ToListAsync();
        var fila = new Queue<int>([eu]);
        while (fila.TryDequeue(out var atual))
            foreach (var sub in gestores.Where(g => g.GestorId == atual && _equipe.Add(g.Id)))
                fila.Enqueue(sub.Id);
        return _equipe;
    }

    public async Task<bool> PodeVerAsync(int funcionarioId) =>
        await EquipeAsync() is not { } equipe || equipe.Contains(funcionarioId);
}
