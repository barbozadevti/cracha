using Cracha.Api.Modelos;

namespace Cracha.Api.Dados;

/// <summary>
/// Empresa de exemplo com datas relativas a hoje: diretoria e equipes, admissões ao longo dos anos,
/// promoções, uma transferência, desligamentos e ausências, cada alteração com o seu registro no histórico.
/// </summary>
public static class Exemplos
{
    private sealed record Evento(int DiasAposAdmissao, decimal? Salario = null, string? Cargo = null,
        string? Departamento = null, string? Gestor = null, bool Desliga = false);

    private sealed record Pessoa(string Nome, string Cargo, string Departamento, string? Gestor, decimal Salario,
        int DiasDeCasa, string Ramal, string Endereco, params Evento[] Eventos);

    private const string Rh = "Gabriela Nunes (RH)";

    public static async Task CriarAsync(CrachaContext contexto, IHistorico historico, DateTimeOffset agora)
    {
        var hoje = DateOnly.FromDateTime(agora.Date);

        var departamentos = new Dictionary<string, Departamento>
        {
            ["Diretoria"] = new() { Nome = "Diretoria", Cor = "#475569" },
            ["Tecnologia"] = new() { Nome = "Tecnologia", Cor = "#0d9488" },
            ["Recursos Humanos"] = new() { Nome = "Recursos Humanos", Cor = "#db2777" },
            ["Financeiro"] = new() { Nome = "Financeiro", Cor = "#2563eb" },
            ["Comercial"] = new() { Nome = "Comercial", Cor = "#ea580c" },
            ["Operações"] = new() { Nome = "Operações", Cor = "#7c3aed" },
            ["Marketing"] = new() { Nome = "Marketing", Cor = "#ca8a04" },
        };
        contexto.Departamentos.AddRange(departamentos.Values);

        // Quem faz aniversário de empresa este mês: admitido no mesmo mês, anos atrás.
        int AnosAtras(int anos, int dia) =>
            hoje.DayNumber - new DateOnly(hoje.Year - anos, hoje.Month, Math.Min(dia, DateTime.DaysInMonth(hoje.Year - anos, hoje.Month))).DayNumber;

        const string Helena = "Helena Prado", Bruno = "Bruno Carvalho", Gabriela = "Gabriela Nunes",
            Joao = "João Pedro Almeida", Natalia = "Natália Rocha", Sabrina = "Sabrina Duarte";

        // O gestor sempre vem antes da equipe.
        Pessoa[] pessoas =
        [
            new(Helena, "Diretora-Geral", "Diretoria", null, 28000, 3300, "2001", "Rua Pamplona, 1200 - São Paulo/SP",
                new Evento(1800, 32000)),
            new(Bruno, "Tech Lead", "Tecnologia", Helena, 15500, 2400, "2102", "Av. Paulista, 1500 - São Paulo/SP",
                new Evento(900, 17200)),
            new("Ana Beatriz Souza", "Desenvolvedora Pleno", "Tecnologia", Bruno, 7800, AnosAtras(3, 8), "2101", "Rua das Acácias, 120 - São Paulo/SP",
                new Evento(365, 8900), new Evento(730, 10500, "Desenvolvedora Sênior")),
            new("Camila Ribeiro", "Analista de Dados", "Tecnologia", Bruno, 6900, 420, "2103", "Rua Harmonia, 45 - São Paulo/SP",
                new Evento(360, 7600)),
            new("Diego Martins", "Desenvolvedor Júnior", "Tecnologia", Bruno, 4200, 75, "2104", "Rua Voluntários da Pátria, 300 - São Paulo/SP"),
            new("Eduarda Lima", "Engenheira de DevOps", "Tecnologia", Bruno, 11200, AnosAtras(1, 20), "2105", "Rua Augusta, 900 - São Paulo/SP"),
            new("Felipe Andrade", "Desenvolvedor Pleno", "Tecnologia", Bruno, 7600, 610, "2106", "Rua Cardeal Arcoverde, 77 - São Paulo/SP",
                new Evento(540, Desliga: true)),
            new(Gabriela, "Gerente de RH", "Recursos Humanos", Helena, 12800, 3100, "2201", "Rua Bela Cintra, 210 - São Paulo/SP",
                new Evento(1500, 14000)),
            new("Henrique Costa", "Analista de Departamento Pessoal", "Recursos Humanos", Gabriela, 5200, AnosAtras(2, 3), "2202", "Rua Tabapuã, 88 - São Paulo/SP"),
            new("Isabela Freitas", "Recrutadora", "Recursos Humanos", Gabriela, 4800, 30, "2203", "Rua Oscar Freire, 400 - São Paulo/SP"),
            new(Joao, "Controller", "Financeiro", Helena, 13500, 1900, "2301", "Alameda Santos, 1000 - São Paulo/SP",
                new Evento(700, 14800)),
            new("Larissa Moreira", "Analista Financeira", "Financeiro", Joao, 6300, 800, "2302", "Rua Haddock Lobo, 595 - São Paulo/SP",
                new Evento(365, 6900), new Evento(600, 6900, "Analista de Controladoria")),
            new("Marcelo Teixeira", "Assistente Financeiro", "Financeiro", Joao, 3600, 260, "2303", "Rua Teodoro Sampaio, 1020 - São Paulo/SP",
                new Evento(200, Desliga: true)),
            new(Natalia, "Gerente Comercial", "Comercial", Helena, 14200, AnosAtras(5, 15), "2401", "Rua Funchal, 263 - São Paulo/SP"),
            new("Otávio Pires", "Executivo de Contas", "Comercial", Natalia, 6800, 540, "2402", "Av. Faria Lima, 2200 - São Paulo/SP",
                new Evento(365, 7400)),
            new("Paula Mendes", "Executiva de Contas", "Comercial", Natalia, 6800, 150, "2403", "Rua Gomes de Carvalho, 1500 - São Paulo/SP"),
            new("Rafael Gomes", "Pré-vendas", "Comercial", Natalia, 7200, 980, "2404", "Rua Joaquim Floriano, 466 - São Paulo/SP",
                new Evento(400, 7200, "Analista de Soluções", "Tecnologia", Bruno), new Evento(700, 8100)),
            new(Sabrina, "Coordenadora de Operações", "Operações", Helena, 9900, 1400, "2501", "Rua Vergueiro, 3185 - São Paulo/SP",
                new Evento(1000, 10800)),
            new("Thiago Barros", "Analista de Logística", "Operações", Sabrina, 5600, 12, "2502", "Rua Domingos de Morais, 2000 - São Paulo/SP"),
            new("Úrsula Campos", "Analista de Marketing", "Marketing", Helena, 6100, 350, "2601", "Rua Girassol, 555 - São Paulo/SP"),
            new("Vinícius Araújo", "Designer", "Marketing", Helena, 5900, 1200, "2602", "Rua Fradique Coutinho, 1100 - São Paulo/SP",
                new Evento(1100, Desliga: true)),
        ];

        // Monta a linha do tempo de cada pessoa e guarda o estado final no banco.
        var porNome = new Dictionary<string, Funcionario>();
        var linhas = new List<(Funcionario Funcionario, List<(TipoAcao Tipo, DateOnly Data, Funcionario Estado)> Versoes)>();
        foreach (var p in pessoas)
        {
            var admissao = hoje.AddDays(-p.DiasDeCasa);
            var estado = new Funcionario
            {
                Nome = p.Nome,
                Cargo = p.Cargo,
                Endereco = p.Endereco,
                Ramal = p.Ramal,
                EmailProfissional = Email(p.Nome),
                Departamento = departamentos[p.Departamento],
                Gestor = p.Gestor is null ? null : porNome[p.Gestor],
                Salario = p.Salario,
                DataAdmissao = admissao,
            };
            porNome[p.Nome] = estado;
            var versoes = new List<(TipoAcao, DateOnly, Funcionario)> { (TipoAcao.Inclusao, admissao, Copia(estado)) };

            foreach (var e in p.Eventos.Where(e => e.DiasAposAdmissao < p.DiasDeCasa))
            {
                var data = admissao.AddDays(e.DiasAposAdmissao);
                if (e.Desliga)
                {
                    estado.Situacao = Situacao.Desligado;
                    estado.DataDesligamento = data;
                    versoes.Add((TipoAcao.Desligamento, data, Copia(estado)));
                    continue;
                }
                estado.Salario = e.Salario ?? estado.Salario;
                estado.Cargo = e.Cargo ?? estado.Cargo;
                estado.Departamento = e.Departamento is null ? estado.Departamento : departamentos[e.Departamento];
                estado.Gestor = e.Gestor is null ? estado.Gestor : porNome[e.Gestor];
                versoes.Add((TipoAcao.Atualizacao, data, Copia(estado)));
            }

            linhas.Add((estado, versoes));
        }

        contexto.Funcionarios.AddRange(linhas.Select(l => l.Funcionario));
        await contexto.SaveChangesAsync();

        // Antes da chegada da Gabriela ao RH, o cadastro vinha da planilha antiga.
        var inicioRh = porNome[Gabriela].DataAdmissao;
        foreach (var (funcionario, versoes) in linhas)
        {
            FotoFuncionario? anterior = null;
            foreach (var (tipo, data, estado) in versoes)
            {
                estado.Id = funcionario.Id;
                var foto = FotoFuncionario.De(estado, estado.Departamento!.Nome, estado.Gestor?.Nome);
                var quando = new DateTimeOffset(data.ToDateTime(new TimeOnly(9, 30)), agora.Offset);
                var autor = data <= inicioRh ? "Importação da planilha" : Rh;
                await historico.RegistrarAsync(Auditoria.Registro(tipo, anterior, foto, quando, autor));
                anterior = foto;
            }
        }

        contexto.Ausencias.AddRange(Ausencias(porNome, hoje, agora));
        await contexto.SaveChangesAsync();
    }

    private static IEnumerable<Ausencia> Ausencias(Dictionary<string, Funcionario> p, DateOnly hoje, DateTimeOffset agora)
    {
        Ausencia Nova(string nome, TipoAusencia tipo, int de, int ate, StatusAusencia status, string? decisor,
            string? observacao = null, string? motivoRecusa = null) => new()
        {
            FuncionarioId = p[nome].Id,
            Tipo = tipo,
            Inicio = hoje.AddDays(de),
            Fim = hoje.AddDays(ate),
            Status = status,
            Observacao = observacao,
            SolicitadaEm = agora.AddDays(Math.Min(de, 0) - 20),
            SolicitadaPor = tipo is TipoAusencia.Atestado or TipoAusencia.Licenca ? Rh : $"{nome} (Colaborador)",
            DecididaEm = status == StatusAusencia.Pendente ? null : agora.AddDays(Math.Min(de, 0) - 15),
            DecididaPor = decisor,
            MotivoRecusa = motivoRecusa,
        };

        return
        [
            Nova("Camila Ribeiro", TipoAusencia.Ferias, -3, 11, StatusAusencia.Aprovada, "Bruno Carvalho (Gestor)", "Viagem em família"),
            Nova("Otávio Pires", TipoAusencia.Atestado, 0, 1, StatusAusencia.Aprovada, Rh),
            Nova("Larissa Moreira", TipoAusencia.Ferias, -60, -46, StatusAusencia.Aprovada, "João Pedro Almeida (Gestor)"),
            Nova("Henrique Costa", TipoAusencia.Ferias, 45, 59, StatusAusencia.Aprovada, "Gabriela Nunes (Gestor)"),
            Nova("Ana Beatriz Souza", TipoAusencia.Ferias, 20, 34, StatusAusencia.Pendente, null, "Primeiro período de 2026"),
            Nova("Diego Martins", TipoAusencia.Folga, 7, 7, StatusAusencia.Pendente, null, "Banco de horas"),
            Nova("Paula Mendes", TipoAusencia.Ferias, 10, 14, StatusAusencia.Recusada, "Natália Rocha (Gestor)",
                motivoRecusa: "Fechamento do trimestre; vamos remarcar para novembro."),
            Nova("Thiago Barros", TipoAusencia.Licenca, 30, 34, StatusAusencia.Aprovada, Rh, "Licença-casamento"),
        ];
    }

    private static Funcionario Copia(Funcionario f) => new()
    {
        Nome = f.Nome, Cargo = f.Cargo, Endereco = f.Endereco, Ramal = f.Ramal, EmailProfissional = f.EmailProfissional,
        Departamento = f.Departamento, Gestor = f.Gestor, Salario = f.Salario, DataAdmissao = f.DataAdmissao,
        Situacao = f.Situacao, DataDesligamento = f.DataDesligamento,
    };

    public static string Email(string nome)
    {
        var partes = nome.Split(' ');
        var texto = $"{partes[0]}.{partes[^1]}".ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var limpo = new string(texto.Where(c => char.IsAsciiLetterOrDigit(c) || c == '.').ToArray());
        return $"{limpo}@cracha.dev";
    }
}
