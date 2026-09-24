using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;

namespace Cracha.Api.Modelos;

/// <summary>Dados para criar ou atualizar um funcionário.</summary>
public sealed class FuncionarioEntrada : IValidatableObject
{
    [Required(ErrorMessage = "Informe o nome."), StringLength(100, MinimumLength = 3, ErrorMessage = "O nome deve ter entre 3 e 100 caracteres.")]
    public string Nome { get; set; } = "";

    [Required(ErrorMessage = "Informe o cargo."), StringLength(60, ErrorMessage = "O cargo deve ter até 60 caracteres.")]
    public string Cargo { get; set; } = "";

    [StringLength(200, ErrorMessage = "O endereço deve ter até 200 caracteres.")]
    public string? Endereco { get; set; }

    [Required(ErrorMessage = "Informe o ramal."), RegularExpression(@"^\d{4}$", ErrorMessage = "O ramal deve ter 4 dígitos.")]
    public string Ramal { get; set; } = "";

    [Required(ErrorMessage = "Informe o e-mail profissional."), EmailAddress(ErrorMessage = "E-mail inválido."), StringLength(120)]
    public string EmailProfissional { get; set; } = "";

    [Range(1, int.MaxValue, ErrorMessage = "Escolha o departamento.")]
    public int DepartamentoId { get; set; }

    [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O salário deve estar entre R$ 0,01 e R$ 1.000.000,00.")]
    public decimal Salario { get; set; }

    [Required(ErrorMessage = "Informe a data de admissão.")]
    public DateOnly? DataAdmissao { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext contexto)
    {
        var relogio = contexto.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        var hoje = DateOnly.FromDateTime(relogio.GetLocalNow().DateTime);
        if (DataAdmissao > hoje.AddDays(90))
            yield return new ValidationResult("A admissão não pode ser mais de 90 dias no futuro.", [nameof(DataAdmissao)]);
        if (DataAdmissao < new DateOnly(1950, 1, 1))
            yield return new ValidationResult("Data de admissão inválida.", [nameof(DataAdmissao)]);
    }
}

public sealed class DesligamentoEntrada
{
    /// <summary>Data do desligamento; se vazia, usa hoje.</summary>
    public DateOnly? Data { get; set; }
}

public sealed class DepartamentoEntrada
{
    [Required(ErrorMessage = "Informe o nome."), StringLength(40, MinimumLength = 2, ErrorMessage = "O nome deve ter entre 2 e 40 caracteres.")]
    public string Nome { get; set; } = "";

    [Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Cor inválida (use #rrggbb).")]
    public string Cor { get; set; } = "#0d9488";
}

public sealed record DepartamentoSaida(int Id, string Nome, string Cor, int Ativos, decimal Folha);

public sealed record FuncionarioSaida(
    int Id, string Nome, string Cargo, string Endereco, string Ramal, string EmailProfissional,
    int DepartamentoId, string Departamento, string CorDepartamento,
    decimal Salario, DateOnly DataAdmissao, Situacao Situacao, DateOnly? DataDesligamento, int MesesDeCasa)
{
    public static FuncionarioSaida De(Funcionario f, DateOnly hoje)
    {
        var fim = f.DataDesligamento ?? hoje;
        var meses = Math.Max(0, (fim.Year - f.DataAdmissao.Year) * 12 + fim.Month - f.DataAdmissao.Month - (fim.Day < f.DataAdmissao.Day ? 1 : 0));
        return new(f.Id, f.Nome, f.Cargo, f.Endereco, f.Ramal, f.EmailProfissional,
            f.DepartamentoId, f.Departamento?.Nome ?? "", f.Departamento?.Cor ?? "#64748b",
            f.Salario, f.DataAdmissao, f.Situacao, f.DataDesligamento, meses);
    }
}

public sealed record HistoricoSaida(
    string Id, int FuncionarioId, string NomeFuncionario, string Departamento,
    TipoAcao TipoAcao, DateTimeOffset Quando, IReadOnlyList<CampoAlterado> Alteracoes, JsonElement Foto)
{
    public static HistoricoSaida De(RegistroHistorico r) =>
        new(r.Id, r.FuncionarioId, r.NomeFuncionario, r.Departamento, r.TipoAcao, r.Quando, r.Alteracoes,
            JsonSerializer.Deserialize<JsonElement>(r.FotoJson));
}

/// <summary>Foto do funcionário gravada em cada registro do histórico, com os valores já legíveis.</summary>
public sealed record FotoFuncionario(
    int Id, string Nome, string Cargo, string Endereco, string Ramal, string EmailProfissional,
    string Departamento, decimal Salario, DateOnly DataAdmissao, Situacao Situacao, DateOnly? DataDesligamento)
{
    public static FotoFuncionario De(Funcionario f, string departamento) =>
        new(f.Id, f.Nome, f.Cargo, f.Endereco, f.Ramal, f.EmailProfissional, departamento,
            f.Salario, f.DataAdmissao, f.Situacao, f.DataDesligamento);

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Lista os campos que mudaram de <paramref name="antes"/> para <paramref name="depois"/>.</summary>
    public static List<CampoAlterado> Comparar(FotoFuncionario? antes, FotoFuncionario? depois)
    {
        var campos = new (string Nome, Func<FotoFuncionario, string?> Valor)[]
        {
            ("Nome", f => f.Nome),
            ("Cargo", f => f.Cargo),
            ("Departamento", f => f.Departamento),
            ("Salário", f => f.Salario.ToString("C", Br)),
            ("Ramal", f => f.Ramal),
            ("E-mail", f => f.EmailProfissional),
            ("Endereço", f => string.IsNullOrWhiteSpace(f.Endereco) ? null : f.Endereco),
            ("Admissão", f => f.DataAdmissao.ToString("dd/MM/yyyy", Br)),
            ("Situação", f => f.Situacao.ToString()),
            ("Desligamento", f => f.DataDesligamento?.ToString("dd/MM/yyyy", Br)),
        };

        var lista = new List<CampoAlterado>();
        foreach (var (nome, valor) in campos)
        {
            var a = antes is null ? null : valor(antes);
            var d = depois is null ? null : valor(depois);
            if (a != d)
                lista.Add(new CampoAlterado(nome, a, d));
        }
        return lista;
    }
}

public sealed record Painel(
    int Ativos, int Desligados, decimal FolhaMensal, decimal SalarioMedio, double MesesMedioDeCasa,
    int AdmissoesNoAno, double Rotatividade12Meses,
    IReadOnlyList<DepartamentoSaida> PorDepartamento,
    IReadOnlyList<AdmissoesDoMes> AdmissoesPorMes,
    IReadOnlyList<AniversarioDeEmpresa> AniversariosDoMes,
    IReadOnlyList<HistoricoSaida> UltimasAlteracoes);

public sealed record AdmissoesDoMes(string Mes, int Admissoes, int Desligamentos);

public sealed record AniversarioDeEmpresa(int Id, string Nome, string Departamento, string Cor, DateOnly Data, int Anos);

public sealed record InfoSistema(string Banco, string Historico, string Versao);
