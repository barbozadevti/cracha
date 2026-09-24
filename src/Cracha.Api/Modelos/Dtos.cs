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

    /// <summary>Gestor imediato; vazio para quem está no topo do organograma.</summary>
    public int? GestorId { get; set; }

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

/// <summary>Folha só aparece para quem pode ver salários (RH e administrador).</summary>
public sealed record DepartamentoSaida(int Id, string Nome, string Cor, int Ativos, decimal? Folha);

/// <summary>
/// Funcionário como a API devolve. Salário e endereço vêm vazios quando quem consulta não é do RH,
/// não é o gestor da pessoa nem a própria pessoa (<see cref="Detalhes"/> = false).
/// </summary>
public sealed record FuncionarioSaida(
    int Id, string Nome, string Cargo, string? Endereco, string Ramal, string EmailProfissional,
    int DepartamentoId, string Departamento, string CorDepartamento,
    int? GestorId, string? Gestor, int Subordinados,
    decimal? Salario, DateOnly DataAdmissao, Situacao Situacao, DateOnly? DataDesligamento, int MesesDeCasa,
    string? FotoUrl, bool Detalhes, AusenciaResumo? AusenteAgora)
{
    public static FuncionarioSaida De(Funcionario f, DateOnly hoje, bool detalhes, int subordinados = 0, AusenciaResumo? ausente = null)
    {
        var fim = f.DataDesligamento ?? hoje;
        var meses = Math.Max(0, (fim.Year - f.DataAdmissao.Year) * 12 + fim.Month - f.DataAdmissao.Month - (fim.Day < f.DataAdmissao.Day ? 1 : 0));
        return new(f.Id, f.Nome, f.Cargo, detalhes ? f.Endereco : null, f.Ramal, f.EmailProfissional,
            f.DepartamentoId, f.Departamento?.Nome ?? "", f.Departamento?.Cor ?? "#64748b",
            f.GestorId, f.Gestor?.Nome, subordinados,
            detalhes ? f.Salario : null, f.DataAdmissao, f.Situacao, f.DataDesligamento, meses,
            f.FotoVersao is null ? null : $"/api/funcionarios/{f.Id}/foto?v={f.FotoVersao}", detalhes, ausente);
    }
}

public sealed record AusenciaResumo(TipoAusencia Tipo, DateOnly Fim);

public sealed record HistoricoSaida(
    string Id, int FuncionarioId, string NomeFuncionario, string Departamento,
    TipoAcao TipoAcao, DateTimeOffset Quando, string Autor, IReadOnlyList<CampoAlterado> Alteracoes, JsonElement Foto)
{
    public static HistoricoSaida De(RegistroHistorico r) =>
        new(r.Id, r.FuncionarioId, r.NomeFuncionario, r.Departamento, r.TipoAcao, r.Quando, r.Autor, r.Alteracoes,
            JsonSerializer.Deserialize<JsonElement>(r.FotoJson));
}

/// <summary>Foto do funcionário gravada em cada registro do histórico, com os valores já legíveis.</summary>
public sealed record FotoFuncionario(
    int Id, string Nome, string Cargo, string Endereco, string Ramal, string EmailProfissional,
    string Departamento, decimal Salario, DateOnly DataAdmissao, Situacao Situacao, DateOnly? DataDesligamento,
    string? Gestor = null)
{
    public static FotoFuncionario De(Funcionario f, string departamento, string? gestor) =>
        new(f.Id, f.Nome, f.Cargo, f.Endereco, f.Ramal, f.EmailProfissional, departamento,
            f.Salario, f.DataAdmissao, f.Situacao, f.DataDesligamento, gestor);

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Lista os campos que mudaram de <paramref name="antes"/> para <paramref name="depois"/>.</summary>
    public static List<CampoAlterado> Comparar(FotoFuncionario? antes, FotoFuncionario? depois)
    {
        var campos = new (string Nome, Func<FotoFuncionario, string?> Valor)[]
        {
            ("Nome", f => f.Nome),
            ("Cargo", f => f.Cargo),
            ("Departamento", f => f.Departamento),
            ("Gestor", f => f.Gestor),
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
    string Escopo, int Ativos, int Desligados, decimal FolhaMensal, decimal SalarioMedio, double MesesMedioDeCasa,
    int AdmissoesNoAno, double Rotatividade12Meses, int PedidosPendentes,
    IReadOnlyList<DepartamentoSaida> PorDepartamento,
    IReadOnlyList<AdmissoesDoMes> AdmissoesPorMes,
    IReadOnlyList<AniversarioDeEmpresa> AniversariosDoMes,
    IReadOnlyList<AusenciaSaida> AusentesHoje,
    IReadOnlyList<HistoricoSaida> UltimasAlteracoes);

public sealed record AdmissoesDoMes(string Mes, int Admissoes, int Desligamentos);

public sealed record AniversarioDeEmpresa(int Id, string Nome, string Departamento, string Cor, DateOnly Data, int Anos);

public sealed record InfoSistema(string Banco, string Historico, string Fotos, string Versao, string Ambiente);

public sealed record NoOrganograma(int Id, string Nome, string Cargo, string Departamento, string Cor, int? GestorId, string? FotoUrl);

// ---------- Acesso ----------

public sealed class EntradaLogin
{
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Informe a senha.")]
    public string Senha { get; set; } = "";
}

public sealed class TrocaSenha
{
    [Required(ErrorMessage = "Informe a senha atual.")]
    public string Atual { get; set; } = "";

    [Required(ErrorMessage = "Informe a nova senha."), Senha]
    public string Nova { get; set; } = "";
}

/// <summary>Senha forte: 8 ou mais caracteres, com letra e número.</summary>
public sealed class SenhaAttribute() : ValidationAttribute("A senha precisa de 8 ou mais caracteres, com letras e números.")
{
    public override bool IsValid(object? value) =>
        value is not string s || (s.Length >= 8 && s.Any(char.IsLetter) && s.Any(char.IsDigit));
}

public sealed class UsuarioEntrada
{
    [Required(ErrorMessage = "Informe o nome."), StringLength(100, MinimumLength = 3, ErrorMessage = "O nome deve ter entre 3 e 100 caracteres.")]
    public string Nome { get; set; } = "";

    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido."), StringLength(120)]
    public string Email { get; set; } = "";

    public Perfil Perfil { get; set; } = Perfil.Colaborador;

    public int? FuncionarioId { get; set; }

    public bool Ativo { get; set; } = true;

    /// <summary>Obrigatória ao criar; ao editar, só se for trocar.</summary>
    [Senha]
    public string? Senha { get; set; }
}

public sealed record UsuarioSaida(int Id, string Nome, string Email, Perfil Perfil, int? FuncionarioId, string? Funcionario,
    bool Ativo, DateTimeOffset CriadoEm, DateTimeOffset? UltimoAcesso)
{
    public static UsuarioSaida De(Usuario u) =>
        new(u.Id, u.Nome, u.Email, u.Perfil, u.FuncionarioId, u.Funcionario?.Nome, u.Ativo, u.CriadoEm, u.UltimoAcesso);
}

/// <summary>Quem está logado e o que pode fazer (a interface esconde o que não se aplica).</summary>
public sealed record Sessao(int Id, string Nome, string Email, Perfil Perfil, int? FuncionarioId, string? FotoUrl, Permissoes Permissoes);

public sealed record Permissoes(bool GerirCadastro, bool Administrar, bool Acompanhar, bool AprovarAusencias, bool VerSalarios);

public sealed record ContaDemonstracao(string Nome, string Email, Perfil Perfil, string Descricao);

// ---------- Ausências ----------

public sealed class AusenciaEntrada : IValidatableObject
{
    /// <summary>Vazio: a própria pessoa logada.</summary>
    public int? FuncionarioId { get; set; }

    public TipoAusencia Tipo { get; set; } = TipoAusencia.Ferias;

    [Required(ErrorMessage = "Informe o início.")]
    public DateOnly? Inicio { get; set; }

    [Required(ErrorMessage = "Informe o fim.")]
    public DateOnly? Fim { get; set; }

    [StringLength(300, ErrorMessage = "A observação deve ter até 300 caracteres.")]
    public string? Observacao { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext contexto)
    {
        if (Inicio is null || Fim is null)
            yield break;
        if (Fim < Inicio)
            yield return new ValidationResult("O fim não pode ser antes do início.", [nameof(Fim)]);
        else if (Tipo == TipoAusencia.Ferias && Fim.Value.DayNumber - Inicio.Value.DayNumber + 1 > 30)
            yield return new ValidationResult("Férias podem ter no máximo 30 dias por período.", [nameof(Fim)]);
        else if (Tipo == TipoAusencia.Ferias && Fim.Value.DayNumber - Inicio.Value.DayNumber + 1 < 5)
            yield return new ValidationResult("Cada período de férias precisa ter pelo menos 5 dias.", [nameof(Fim)]);
    }
}

public sealed class DecisaoAusencia
{
    public bool Aprovar { get; set; }

    [StringLength(300)]
    public string? Motivo { get; set; }
}

public sealed record AusenciaSaida(
    int Id, int FuncionarioId, string Funcionario, string Departamento, string Cor, string? FotoUrl,
    TipoAusencia Tipo, DateOnly Inicio, DateOnly Fim, int Dias, StatusAusencia Status, string? Observacao,
    DateTimeOffset SolicitadaEm, string SolicitadaPor, DateTimeOffset? DecididaEm, string? DecididaPor, string? MotivoRecusa,
    bool PodeDecidir, bool PodeCancelar)
{
    public static AusenciaSaida De(Ausencia a, bool podeDecidir, bool podeCancelar)
    {
        var f = a.Funcionario!;
        return new(a.Id, a.FuncionarioId, f.Nome, f.Departamento?.Nome ?? "", f.Departamento?.Cor ?? "#64748b",
            f.FotoVersao is null ? null : $"/api/funcionarios/{f.Id}/foto?v={f.FotoVersao}",
            a.Tipo, a.Inicio, a.Fim, a.Dias, a.Status, a.Observacao, a.SolicitadaEm, a.SolicitadaPor,
            a.DecididaEm, a.DecididaPor, a.MotivoRecusa, podeDecidir, podeCancelar);
    }
}
