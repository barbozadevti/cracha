namespace Cracha.Api.Modelos;

public class Departamento
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Cor { get; set; } = "#0d9488";
    public List<Funcionario> Funcionarios { get; set; } = [];
}

public enum Situacao { Ativo, Desligado }

public class Funcionario
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Cargo { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string Ramal { get; set; } = "";
    public string EmailProfissional { get; set; } = "";
    public int DepartamentoId { get; set; }
    public Departamento? Departamento { get; set; }

    /// <summary>Gestor imediato (organograma). Vazio para quem está no topo.</summary>
    public int? GestorId { get; set; }
    public Funcionario? Gestor { get; set; }
    public List<Funcionario> Subordinados { get; set; } = [];

    public decimal Salario { get; set; }
    public DateOnly DataAdmissao { get; set; }
    public Situacao Situacao { get; set; } = Situacao.Ativo;
    public DateOnly? DataDesligamento { get; set; }

    /// <summary>Muda a cada nova foto; entra na URL para o navegador não usar a foto antiga do cache.</summary>
    public string? FotoVersao { get; set; }
}

/// <summary>Tipos de alteração registrados no histórico (o "FuncionarioLog" do desafio).</summary>
public enum TipoAcao { Inclusao, Atualizacao, Desligamento, Reativacao, Remocao }

/// <summary>Um campo que mudou numa alteração, já formatado para leitura.</summary>
public sealed record CampoAlterado(string Campo, string? Antes, string? Depois);

/// <summary>
/// Registro imutável de uma alteração: quem era o funcionário naquele momento (foto em JSON),
/// o que mudou em relação à versão anterior e quem fez a alteração.
/// </summary>
public sealed class RegistroHistorico
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int FuncionarioId { get; set; }
    public string NomeFuncionario { get; set; } = "";
    public string Departamento { get; set; } = "";
    public TipoAcao TipoAcao { get; set; }
    public DateTimeOffset Quando { get; set; }
    public string Autor { get; set; } = "Sistema";
    public string FotoJson { get; set; } = "{}";
    public List<CampoAlterado> Alteracoes { get; set; } = [];
}

/// <summary>Perfis de acesso, do mais amplo ao mais restrito.</summary>
public enum Perfil { Administrador, RH, Gestor, Colaborador }

public class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public Perfil Perfil { get; set; } = Perfil.Colaborador;

    /// <summary>Funcionário ligado ao acesso (o "eu" do colaborador e a raiz da equipe do gestor).</summary>
    public int? FuncionarioId { get; set; }
    public Funcionario? Funcionario { get; set; }

    public bool Ativo { get; set; } = true;
    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset? UltimoAcesso { get; set; }

    /// <summary>Troca quando perfil, senha ou situação mudam: derruba as sessões abertas.</summary>
    public string Carimbo { get; set; } = Guid.NewGuid().ToString("N");
}

public enum TipoAusencia { Ferias, Licenca, Atestado, Folga }

public enum StatusAusencia { Pendente, Aprovada, Recusada, Cancelada }

/// <summary>Férias, licenças, atestados e folgas, com fluxo de aprovação.</summary>
public class Ausencia
{
    public int Id { get; set; }
    public int FuncionarioId { get; set; }
    public Funcionario? Funcionario { get; set; }
    public TipoAusencia Tipo { get; set; }
    public DateOnly Inicio { get; set; }
    public DateOnly Fim { get; set; }
    public StatusAusencia Status { get; set; } = StatusAusencia.Pendente;
    public string? Observacao { get; set; }
    public DateTimeOffset SolicitadaEm { get; set; }
    public string SolicitadaPor { get; set; } = "";
    public DateTimeOffset? DecididaEm { get; set; }
    public string? DecididaPor { get; set; }
    public string? MotivoRecusa { get; set; }

    public int Dias => Fim.DayNumber - Inicio.DayNumber + 1;
}
