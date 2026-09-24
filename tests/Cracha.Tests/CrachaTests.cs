using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cracha.Api.Modelos;

namespace Cracha.Tests;

internal static class Consultas
{
    public static async Task<int> DepartamentoAsync(this HttpClient http, string nome) =>
        (await (await http.GetAsync("/api/departamentos")).LerAsync()).EnumerateArray()
            .Single(d => d.Texto("nome") == nome).GetProperty("id").GetInt32();

    public static async Task<JsonElement> FuncionarioAsync(this HttpClient http, string nome) =>
        (await (await http.GetAsync($"/api/funcionarios?busca={Uri.EscapeDataString(nome)}")).LerAsync()).EnumerateArray()
            .Single(f => f.Texto("nome") == nome);

    public static async Task<int> IdAsync(this HttpClient http, string nome) =>
        (await http.FuncionarioAsync(nome)).GetProperty("id").GetInt32();
}

public class FuncionariosTests(CrachaFactory fabrica) : IClassFixture<CrachaFactory>
{
    private readonly HttpClient _http = fabrica.Como(CrachaFactory.Rh);

    private async Task<object> NovoAsync(string email, string nome = "Teste da Silva", string departamento = "Tecnologia",
        decimal salario = 5000, int? gestorId = null) => new
    {
        nome,
        cargo = "Analista de Testes",
        endereco = "Rua do Teste, 1",
        ramal = "9001",
        emailProfissional = email,
        departamentoId = await _http.DepartamentoAsync(departamento),
        gestorId,
        salario,
        dataAdmissao = "2026-09-01",
    };

    private async Task<JsonElement> CriarAsync(string email, string nome = "Teste da Silva", int? gestorId = null)
    {
        var resposta = await _http.PostAsJsonAsync("/api/funcionarios", await NovoAsync(email, nome, gestorId: gestorId));
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return await resposta.LerAsync();
    }

    private async Task<JsonElement[]> HistoricoAsync(int id) =>
        (await (await _http.GetAsync($"/api/funcionarios/{id}/historico")).LerAsync()).EnumerateArray().ToArray();

    [Fact]
    public async Task Lista_por_padrao_traz_todos_ordenados_por_nome()
    {
        var nomes = (await (await _http.GetAsync("/api/funcionarios")).LerAsync()).Nomes();

        Assert.True(nomes.Length >= 21);
        Assert.Equal(nomes.Order(StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), false)), nomes);
    }

    [Fact]
    public async Task Filtra_por_situacao_departamento_gestor_e_busca()
    {
        var desligados = await (await _http.GetAsync("/api/funcionarios?situacao=Desligado")).LerAsync();
        Assert.Contains("Vinícius Araújo", desligados.Nomes());
        Assert.All(desligados.EnumerateArray(), f => Assert.Equal("Desligado", f.Texto("situacao")));

        var financeiro = await (await _http.GetAsync($"/api/funcionarios?departamentoId={await _http.DepartamentoAsync("Financeiro")}&situacao=Ativo")).LerAsync();
        Assert.Equal(["João Pedro Almeida", "Larissa Moreira"], financeiro.Nomes());

        var equipeNatalia = await (await _http.GetAsync($"/api/funcionarios?gestorId={await _http.IdAsync("Natália Rocha")}")).LerAsync();
        Assert.Equal(["Otávio Pires", "Paula Mendes"], equipeNatalia.Nomes());

        Assert.Equal(["Eduarda Lima"], (await (await _http.GetAsync("/api/funcionarios?busca=DEVOPS")).LerAsync()).Nomes());
        Assert.Equal(["Natália Rocha"], (await (await _http.GetAsync("/api/funcionarios?busca=2401")).LerAsync()).Nomes());
    }

    [Fact]
    public async Task Ordena_por_salario_decrescente()
    {
        var lista = await (await _http.GetAsync("/api/funcionarios?situacao=Ativo&ordem=Salario&desc=true")).LerAsync();
        var salarios = lista.EnumerateArray().Select(f => f.GetProperty("salario").GetDecimal()).ToArray();

        Assert.Equal(salarios.OrderDescending(), salarios);
        Assert.Equal("Helena Prado", lista[0].Texto("nome"));
    }

    [Fact]
    public async Task Mostra_gestor_equipe_e_quem_esta_ausente()
    {
        var bruno = await _http.FuncionarioAsync("Bruno Carvalho");
        Assert.Equal("Helena Prado", bruno.Texto("gestor"));
        // Outros testes da classe podem admitir gente na equipe dele; a base tem 5 ativos.
        Assert.True(bruno.GetProperty("subordinados").GetInt32() >= 5);

        var camila = await _http.FuncionarioAsync("Camila Ribeiro");
        Assert.Equal("Ferias", camila.GetProperty("ausenteAgora").Texto("tipo"));
    }

    [Fact]
    public async Task Criar_devolve_201_e_registra_admissao_com_autor()
    {
        var criado = await CriarAsync("criar@teste.dev", gestorId: await _http.IdAsync("Bruno Carvalho"));
        var id = criado.GetProperty("id").GetInt32();

        Assert.Equal("Tecnologia", criado.Texto("departamento"));
        Assert.Equal("Bruno Carvalho", criado.Texto("gestor"));

        var inclusao = Assert.Single(await HistoricoAsync(id));
        Assert.Equal("Inclusao", inclusao.Texto("tipoAcao"));
        Assert.Equal("Gabriela Nunes (RH)", inclusao.Texto("autor"));
        Assert.Equal(5000m, inclusao.GetProperty("foto").GetProperty("salario").GetDecimal());
    }

    [Fact]
    public async Task Email_fica_minusculo_e_nao_pode_repetir()
    {
        var criado = await CriarAsync("Repetido@Teste.dev");
        Assert.Equal("repetido@teste.dev", criado.Texto("emailProfissional"));

        var repetido = await _http.PostAsJsonAsync("/api/funcionarios", await NovoAsync("repetido@teste.dev", "Outra Pessoa"));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
    }

    [Theory]
    [InlineData("ramal", "12")]
    [InlineData("salario", "0")]
    [InlineData("emailProfissional", "sem-arroba")]
    [InlineData("departamentoId", "999")]
    [InlineData("gestorId", "999")]
    [InlineData("dataAdmissao", "2027-12-01")]
    public async Task Dados_invalidos_devolvem_400(string campo, string valor)
    {
        var corpo = JsonSerializer.SerializeToNode(await NovoAsync($"invalido-{campo}@teste.dev"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        corpo[campo] = campo is "salario" or "departamentoId" or "gestorId" ? JsonValue.Create(decimal.Parse(valor)) : JsonValue.Create(valor);

        var resposta = await _http.PostAsJsonAsync("/api/funcionarios", corpo);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Atualizar_registra_so_os_campos_que_mudaram()
    {
        var id = (await CriarAsync("atualizar@teste.dev")).GetProperty("id").GetInt32();
        var alterado = await NovoAsync("atualizar@teste.dev", departamento: "Recursos Humanos", salario: 6250.5m,
            gestorId: await _http.IdAsync("Gabriela Nunes"));

        var resposta = await _http.PutAsJsonAsync($"/api/funcionarios/{id}", alterado);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Recursos Humanos", (await resposta.LerAsync()).Texto("departamento"));

        var atualizacao = (await HistoricoAsync(id))[0];
        Assert.Equal("Atualizacao", atualizacao.Texto("tipoAcao"));
        var campos = atualizacao.GetProperty("alteracoes").EnumerateArray()
            .Select(a => (a.Texto("campo"), a.GetProperty("antes").GetString(), a.Texto("depois"))).ToArray();
        Assert.Equal(
            [("Departamento", "Tecnologia", "Recursos Humanos"), ("Gestor", null, "Gabriela Nunes"), ("Salário", "R$ 5.000,00", "R$ 6.250,50")],
            campos);

        // Salvar de novo sem mudar nada não gera outro registro.
        await _http.PutAsJsonAsync($"/api/funcionarios/{id}", alterado);
        Assert.Equal(2, (await HistoricoAsync(id)).Length);
    }

    [Fact]
    public async Task Organograma_nao_aceita_ciclo_nem_autogestao()
    {
        var helena = await _http.IdAsync("Helena Prado");
        var bruno = await _http.IdAsync("Bruno Carvalho");

        // Helena lidera Bruno; colocar Bruno como gestor da Helena fecharia um círculo.
        var ciclo = await _http.PutAsJsonAsync($"/api/funcionarios/{helena}", await NovoAsync("helena.prado@cracha.dev", "Helena Prado", "Diretoria", 32000, bruno));
        Assert.Equal(HttpStatusCode.BadRequest, ciclo.StatusCode);

        var propria = await _http.PutAsJsonAsync($"/api/funcionarios/{helena}", await NovoAsync("helena.prado@cracha.dev", "Helena Prado", "Diretoria", 32000, helena));
        Assert.Equal(HttpStatusCode.BadRequest, propria.StatusCode);

        var organograma = await (await _http.GetAsync("/api/organograma")).LerAsync();
        var topo = organograma.EnumerateArray().Where(n => n.GetProperty("gestorId").ValueKind == JsonValueKind.Null).Select(n => n.Texto("nome"));
        Assert.Contains("Helena Prado", topo);
    }

    [Fact]
    public async Task Nao_desliga_nem_remove_quem_lidera_equipe_ativa()
    {
        var natalia = await _http.IdAsync("Natália Rocha");

        var desligar = await _http.PostAsJsonAsync($"/api/funcionarios/{natalia}/desligar", new { });
        Assert.Equal(HttpStatusCode.Conflict, desligar.StatusCode);
        Assert.Contains("Transfira", (await desligar.LerAsync()).Texto("title"));

        Assert.Equal(HttpStatusCode.Conflict, (await _http.DeleteAsync($"/api/funcionarios/{natalia}")).StatusCode);
    }

    [Fact]
    public async Task Desligar_e_reativar()
    {
        var id = (await CriarAsync("desligar@teste.dev")).GetProperty("id").GetInt32();

        var antesDaAdmissao = await _http.PostAsJsonAsync($"/api/funcionarios/{id}/desligar", new { data = "2026-08-01" });
        Assert.Equal(HttpStatusCode.BadRequest, antesDaAdmissao.StatusCode);

        var desligado = await (await _http.PostAsJsonAsync($"/api/funcionarios/{id}/desligar", new { data = "2026-09-20" })).LerAsync();
        Assert.Equal("Desligado", desligado.Texto("situacao"));
        Assert.Equal("2026-09-20", desligado.Texto("dataDesligamento"));

        var deNovo = await _http.PostAsJsonAsync($"/api/funcionarios/{id}/desligar", new { });
        Assert.Equal(HttpStatusCode.Conflict, deNovo.StatusCode);

        var reativado = await (await _http.PostAsync($"/api/funcionarios/{id}/reativar", null)).LerAsync();
        Assert.Equal("Ativo", reativado.Texto("situacao"));
        Assert.Equal(JsonValueKind.Null, reativado.GetProperty("dataDesligamento").ValueKind);

        var tipos = (await HistoricoAsync(id)).Select(r => r.Texto("tipoAcao")).ToArray();
        Assert.Equal(["Reativacao", "Desligamento", "Inclusao"], tipos);
    }

    [Fact]
    public async Task Remover_apaga_do_cadastro_mas_mantem_o_historico()
    {
        var id = (await CriarAsync("remover@teste.dev")).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"/api/funcionarios/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/funcionarios/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.DeleteAsync($"/api/funcionarios/{id}")).StatusCode);

        var historico = await HistoricoAsync(id);
        Assert.Equal("Remocao", historico[0].Texto("tipoAcao"));
        Assert.Equal("remover@teste.dev", historico[0].GetProperty("foto").Texto("emailProfissional"));
    }

    [Fact]
    public async Task Inexistente_devolve_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/funcionarios/99999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PutAsJsonAsync("/api/funcionarios/99999", await NovoAsync("x@teste.dev"))).StatusCode);
    }

    [Fact]
    public async Task Exemplos_tem_transferencia_com_troca_de_gestor()
    {
        var historico = await HistoricoAsync(await _http.IdAsync("Rafael Gomes"));

        Assert.Equal(["Atualizacao", "Atualizacao", "Inclusao"], historico.Select(r => r.Texto("tipoAcao")));
        var campos = historico[1].GetProperty("alteracoes").EnumerateArray().ToDictionary(a => a.Texto("campo"), a => a.Texto("depois"));
        Assert.Equal("Tecnologia", campos["Departamento"]);
        Assert.Equal("Bruno Carvalho", campos["Gestor"]);
        Assert.Equal("Gabriela Nunes (RH)", historico[1].Texto("autor"));
    }
}

public class DepartamentosTests(CrachaFactory fabrica) : IClassFixture<CrachaFactory>
{
    private readonly HttpClient _http = fabrica.Como(CrachaFactory.Rh);

    [Fact]
    public async Task Lista_com_ativos_e_folha()
    {
        var lista = await (await _http.GetAsync("/api/departamentos")).LerAsync();
        var financeiro = lista.EnumerateArray().Single(d => d.Texto("nome") == "Financeiro");

        Assert.Equal(7, lista.GetArrayLength());
        Assert.Equal(2, financeiro.GetProperty("ativos").GetInt32());
        Assert.Equal(21700m, financeiro.GetProperty("folha").GetDecimal());
    }

    [Fact]
    public async Task Folha_nao_aparece_para_o_colaborador()
    {
        var lista = await (await fabrica.Como(CrachaFactory.Colaborador).GetAsync("/api/departamentos")).LerAsync();
        Assert.All(lista.EnumerateArray(), d => Assert.Equal(JsonValueKind.Null, d.GetProperty("folha").ValueKind));
    }

    [Fact]
    public async Task Criar_renomear_e_remover()
    {
        var criado = await (await _http.PostAsJsonAsync("/api/departamentos", new { nome = "Jurídico", cor = "#334155" })).LerAsync();
        var id = criado.GetProperty("id").GetInt32();

        var duplicado = await _http.PostAsJsonAsync("/api/departamentos", new { nome = "Jurídico", cor = "#334155" });
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);

        var renomeado = await (await _http.PutAsJsonAsync($"/api/departamentos/{id}", new { nome = "Jurídico e Compliance", cor = "#1E293B" })).LerAsync();
        Assert.Equal("#1e293b", renomeado.Texto("cor"));

        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"/api/departamentos/{id}")).StatusCode);
    }

    [Fact]
    public async Task Nao_remove_departamento_com_funcionarios()
    {
        var resposta = await _http.DeleteAsync($"/api/departamentos/{await _http.DepartamentoAsync("Tecnologia")}");
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    [Fact]
    public async Task Cor_invalida_devolve_400()
    {
        var resposta = await _http.PostAsJsonAsync("/api/departamentos", new { nome = "Qualquer", cor = "azul" });
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }
}

public class PainelTests(CrachaFactory fabrica) : IClassFixture<CrachaFactory>
{
    [Fact]
    public async Task Painel_do_RH_ve_a_empresa()
    {
        var p = await (await fabrica.Como(CrachaFactory.Rh).GetAsync("/api/painel")).LerAsync();

        Assert.Equal("Empresa", p.Texto("escopo"));
        Assert.Equal(18, p.GetProperty("ativos").GetInt32());
        Assert.Equal(3, p.GetProperty("desligados").GetInt32());
        Assert.Equal(187400m, p.GetProperty("folhaMensal").GetDecimal());
        Assert.Equal(2, p.GetProperty("pedidosPendentes").GetInt32());
        Assert.Equal(12, p.GetProperty("admissoesPorMes").GetArrayLength());
        Assert.Equal("Set/26", p.GetProperty("admissoesPorMes")[11].Texto("mes"));
        Assert.Equal("Tecnologia", p.GetProperty("porDepartamento")[0].Texto("nome"));
        Assert.Equal(["Otávio Pires", "Camila Ribeiro"], p.GetProperty("ausentesHoje").EnumerateArray().Select(a => a.Texto("funcionario")));

        var aniversarios = p.GetProperty("aniversariosDoMes").EnumerateArray()
            .Select(a => (a.Texto("nome"), a.GetProperty("anos").GetInt32())).ToArray();
        Assert.Equal([("Henrique Costa", 2), ("Ana Beatriz Souza", 3), ("Helena Prado", 9), ("Natália Rocha", 5), ("Eduarda Lima", 1)], aniversarios);
        Assert.Equal(6, p.GetProperty("ultimasAlteracoes").GetArrayLength());
    }

    [Fact]
    public async Task Painel_do_gestor_ve_so_a_equipe()
    {
        var p = await (await fabrica.Como(CrachaFactory.Gestor).GetAsync("/api/painel")).LerAsync();

        Assert.Equal("Equipe de Bruno Carvalho", p.Texto("escopo"));
        Assert.Equal(6, p.GetProperty("ativos").GetInt32());
        Assert.Equal(1, p.GetProperty("desligados").GetInt32());
        Assert.Equal(2, p.GetProperty("pedidosPendentes").GetInt32());
        Assert.Equal(["Camila Ribeiro"], p.GetProperty("ausentesHoje").EnumerateArray().Select(a => a.Texto("funcionario")));
        Assert.All(p.GetProperty("ultimasAlteracoes").EnumerateArray(),
            r => Assert.Contains(r.Texto("nomeFuncionario"), new[] { "Bruno Carvalho", "Ana Beatriz Souza", "Camila Ribeiro", "Diego Martins", "Eduarda Lima", "Felipe Andrade", "Rafael Gomes" }));
    }

    [Fact]
    public async Task Colaborador_nao_acessa_painel_nem_historico_geral()
    {
        var ana = fabrica.Como(CrachaFactory.Colaborador);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/painel")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/historico")).StatusCode);
    }

    [Fact]
    public async Task Historico_filtra_por_tipo_e_departamento()
    {
        var http = fabrica.Como(CrachaFactory.Rh);
        var desligamentos = await (await http.GetAsync("/api/historico?tipo=Desligamento")).LerAsync();
        Assert.Equal(new[] { "Vinícius Araújo", "Felipe Andrade", "Marcelo Teixeira" }.Order(), desligamentos.EnumerateArray().Select(r => r.Texto("nomeFuncionario")).Order());

        var marketing = await (await http.GetAsync("/api/historico?departamento=Marketing")).LerAsync();
        Assert.All(marketing.EnumerateArray(), r => Assert.Equal("Marketing", r.Texto("departamento")));

        var datas = marketing.EnumerateArray().Select(r => r.GetProperty("quando").GetDateTimeOffset()).ToArray();
        Assert.Equal(datas.OrderDescending(), datas);
    }

    [Fact]
    public async Task Sistema_informa_onde_os_dados_ficam()
    {
        var s = await (await fabrica.CreateClient().GetAsync("/api/sistema")).LerAsync();
        var azure = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CRACHA_TABLES"));
        Assert.Equal(azure ? "Azure Table (Azurite)" : "Banco de dados", s.Texto("historico"));
        Assert.Equal(azure ? "Azure Blob (Azurite)" : "Pasta local", s.Texto("fotos"));
    }
}

public class ComparacaoTests
{
    private static FotoFuncionario Foto(decimal salario = 1000, string cargo = "Analista", DateOnly? desligamento = null, string? gestor = null) =>
        new(1, "Fulano", cargo, "", "1234", "f@x.dev", "TI", salario, new DateOnly(2024, 1, 10),
            desligamento is null ? Situacao.Ativo : Situacao.Desligado, desligamento, gestor);

    [Fact]
    public void Inclusao_lista_os_campos_preenchidos()
    {
        var campos = FotoFuncionario.Comparar(null, Foto(gestor: "Beltrana")).Select(c => c.Campo).ToArray();
        Assert.Equal(["Nome", "Cargo", "Departamento", "Gestor", "Salário", "Ramal", "E-mail", "Admissão", "Situação"], campos);
    }

    [Fact]
    public void Formata_valores_em_portugues()
    {
        var campo = Assert.Single(FotoFuncionario.Comparar(Foto(1000), Foto(1234.5m)));
        Assert.Equal(new CampoAlterado("Salário", "R$ 1.000,00", "R$ 1.234,50"), campo);
    }

    [Fact]
    public void Desligamento_mostra_situacao_e_data()
    {
        var campos = FotoFuncionario.Comparar(Foto(), Foto(desligamento: new DateOnly(2026, 9, 1)));
        Assert.Equal(
            [new CampoAlterado("Situação", "Ativo", "Desligado"), new CampoAlterado("Desligamento", null, "01/09/2026")],
            campos);
    }
}
