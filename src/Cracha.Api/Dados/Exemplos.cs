using Cracha.Api.Modelos;

namespace Cracha.Api.Dados;

/// <summary>
/// Empresa de exemplo com datas relativas a hoje: admissões ao longo dos anos, promoções,
/// transferências e desligamentos, cada um com o seu registro no histórico.
/// </summary>
public static class Exemplos
{
    private sealed record Evento(int DiasAposAdmissao, decimal? Salario = null, string? Cargo = null, string? Departamento = null, bool Desliga = false);

    private sealed record Pessoa(string Nome, string Cargo, string Departamento, decimal Salario, int DiasDeCasa, string Ramal, string Endereco, params Evento[] Eventos);

    public static async Task CriarAsync(CrachaContext contexto, IHistorico historico, DateTimeOffset agora)
    {
        var hoje = DateOnly.FromDateTime(agora.Date);

        var departamentos = new Dictionary<string, Departamento>
        {
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

        Pessoa[] pessoas =
        [
            new("Ana Beatriz Souza", "Desenvolvedora Pleno", "Tecnologia", 7800, AnosAtras(3, 8), "2101", "Rua das Acácias, 120 - São Paulo/SP",
                new Evento(365, 8900), new Evento(730, 10500, "Desenvolvedora Sênior")),
            new("Bruno Carvalho", "Tech Lead", "Tecnologia", 15500, 2400, "2102", "Av. Paulista, 1500 - São Paulo/SP",
                new Evento(900, 17200)),
            new("Camila Ribeiro", "Analista de Dados", "Tecnologia", 6900, 420, "2103", "Rua Harmonia, 45 - São Paulo/SP",
                new Evento(360, 7600)),
            new("Diego Martins", "Desenvolvedor Júnior", "Tecnologia", 4200, 75, "2104", "Rua Voluntários da Pátria, 300 - São Paulo/SP"),
            new("Eduarda Lima", "Engenheira de DevOps", "Tecnologia", 11200, AnosAtras(1, 20), "2105", "Rua Augusta, 900 - São Paulo/SP"),
            new("Felipe Andrade", "Desenvolvedor Pleno", "Tecnologia", 7600, 610, "2106", "Rua Cardeal Arcoverde, 77 - São Paulo/SP",
                new Evento(540, Desliga: true)),
            new("Gabriela Nunes", "Gerente de RH", "Recursos Humanos", 12800, 3100, "2201", "Rua Bela Cintra, 210 - São Paulo/SP",
                new Evento(1500, 14000)),
            new("Henrique Costa", "Analista de Departamento Pessoal", "Recursos Humanos", 5200, AnosAtras(2, 3), "2202", "Rua Tabapuã, 88 - São Paulo/SP"),
            new("Isabela Freitas", "Recrutadora", "Recursos Humanos", 4800, 30, "2203", "Rua Oscar Freire, 400 - São Paulo/SP"),
            new("João Pedro Almeida", "Controller", "Financeiro", 13500, 1900, "2301", "Alameda Santos, 1000 - São Paulo/SP",
                new Evento(700, 14800)),
            new("Larissa Moreira", "Analista Financeira", "Financeiro", 6300, 800, "2302", "Rua Haddock Lobo, 595 - São Paulo/SP",
                new Evento(365, 6900), new Evento(600, 6900, "Analista de Controladoria")),
            new("Marcelo Teixeira", "Assistente Financeiro", "Financeiro", 3600, 260, "2303", "Rua Teodoro Sampaio, 1020 - São Paulo/SP",
                new Evento(200, Desliga: true)),
            new("Natália Rocha", "Gerente Comercial", "Comercial", 14200, AnosAtras(5, 15), "2401", "Rua Funchal, 263 - São Paulo/SP"),
            new("Otávio Pires", "Executivo de Contas", "Comercial", 6800, 540, "2402", "Av. Faria Lima, 2200 - São Paulo/SP",
                new Evento(365, 7400)),
            new("Paula Mendes", "Executiva de Contas", "Comercial", 6800, 150, "2403", "Rua Gomes de Carvalho, 1500 - São Paulo/SP"),
            new("Rafael Gomes", "Pré-vendas", "Comercial", 7200, 980, "2404", "Rua Joaquim Floriano, 466 - São Paulo/SP",
                new Evento(400, 7200, Departamento: "Tecnologia", Cargo: "Analista de Soluções"), new Evento(700, 8100)),
            new("Sabrina Duarte", "Coordenadora de Operações", "Operações", 9900, 1400, "2501", "Rua Vergueiro, 3185 - São Paulo/SP",
                new Evento(1000, 10800)),
            new("Thiago Barros", "Analista de Logística", "Operações", 5600, 12, "2502", "Rua Domingos de Morais, 2000 - São Paulo/SP"),
            new("Úrsula Campos", "Analista de Marketing", "Marketing", 6100, 350, "2601", "Rua Girassol, 555 - São Paulo/SP"),
            new("Vinícius Araújo", "Designer", "Marketing", 5900, 1200, "2602", "Rua Fradique Coutinho, 1100 - São Paulo/SP",
                new Evento(1100, Desliga: true)),
        ];

        // Monta a linha do tempo de cada pessoa e guarda o estado final no banco.
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
                Salario = p.Salario,
                DataAdmissao = admissao,
            };
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
                versoes.Add((TipoAcao.Atualizacao, data, Copia(estado)));
            }

            linhas.Add((estado, versoes));
        }

        contexto.Funcionarios.AddRange(linhas.Select(l => l.Funcionario));
        await contexto.SaveChangesAsync();

        foreach (var (funcionario, versoes) in linhas)
        {
            FotoFuncionario? anterior = null;
            foreach (var (tipo, data, estado) in versoes)
            {
                estado.Id = funcionario.Id;
                var foto = FotoFuncionario.De(estado, estado.Departamento!.Nome);
                var quando = new DateTimeOffset(data.ToDateTime(new TimeOnly(9, 30)), agora.Offset);
                await historico.RegistrarAsync(Auditoria.Registro(tipo, anterior, foto, quando));
                anterior = foto;
            }
        }
    }

    private static Funcionario Copia(Funcionario f) => new()
    {
        Nome = f.Nome, Cargo = f.Cargo, Endereco = f.Endereco, Ramal = f.Ramal, EmailProfissional = f.EmailProfissional,
        Departamento = f.Departamento, Salario = f.Salario, DataAdmissao = f.DataAdmissao,
        Situacao = f.Situacao, DataDesligamento = f.DataDesligamento,
    };

    private static string Email(string nome)
    {
        var partes = nome.Split(' ');
        var texto = $"{partes[0]}.{partes[^1]}".ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var limpo = new string(texto.Where(c => char.IsAsciiLetterOrDigit(c) || c == '.').ToArray());
        return $"{limpo}@cracha.dev";
    }
}
