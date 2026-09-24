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
    public decimal Salario { get; set; }
    public DateOnly DataAdmissao { get; set; }
    public Situacao Situacao { get; set; } = Situacao.Ativo;
    public DateOnly? DataDesligamento { get; set; }
}

/// <summary>Tipos de alteração registrados no histórico (o "FuncionarioLog" do desafio).</summary>
public enum TipoAcao { Inclusao, Atualizacao, Desligamento, Reativacao, Remocao }

/// <summary>Um campo que mudou numa alteração, já formatado para leitura.</summary>
public sealed record CampoAlterado(string Campo, string? Antes, string? Depois);

/// <summary>
/// Registro imutável de uma alteração: quem era o funcionário naquele momento (foto em JSON)
/// e o que mudou em relação à versão anterior.
/// </summary>
public sealed class RegistroHistorico
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int FuncionarioId { get; set; }
    public string NomeFuncionario { get; set; } = "";
    public string Departamento { get; set; } = "";
    public TipoAcao TipoAcao { get; set; }
    public DateTimeOffset Quando { get; set; }
    public string FotoJson { get; set; } = "{}";
    public List<CampoAlterado> Alteracoes { get; set; } = [];
}
