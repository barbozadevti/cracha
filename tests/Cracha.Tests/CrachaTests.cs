using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cracha.Api.Modelos;

namespace Cracha.Tests;

public class FuncionariosTests(CrachaFactory fabrica) : IClassFixture<CrachaFactory>
{
    private readonly HttpClient _http = fabrica.CreateClient();

    private static object Novo(string email, string nome = "Teste da Silva", int departamentoId = 1, decimal salario = 5000) => new
    {
        nome,
        cargo = "Analista de Testes",
        endereco = "Rua do Teste, 1",
        ramal = "9001",
        emailProfissional = email,
        departamentoId,
        salario,
        dataAdmissao = "2026-09-01",
    };

    private async Task<JsonElement> CriarAsync(string email, string nome = "Teste da Silva")
    {
        var resposta = await _http.PostAsJsonAsync("/api/funcionarios", Novo(email, nome));
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return await resposta.LerAsync();
    }

    private async Task<JsonElement[]> HistoricoAsync(int id) =>
        (await (await _http.GetAsync($"/api/funcionarios/{id}/historico")).LerAsync()).EnumerateArray().ToArray();

    [Fact]
    public async Task Lista_por_padrao_traz_todos_ordenados_por_nome()
    {
        var lista = await (await _http.GetAsync("/api/funcionarios")).LerAsync();
        var nomes = lista.Nomes();

        Assert.True(nomes.Length >= 20);
        Assert.Equal(nomes.Order(StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), false)), nomes);
    }

    [Fact]
    public async Task Filtra_por_situacao_departamento_e_busca()
    {
        var desligados = await (await _http.GetAsync("/api/funcionarios?situacao=Desligado")).LerAsync();
        Assert.Contains("Vinícius Araújo", desligados.Nomes());
        Assert.All(desligados.EnumerateArray(), f => Assert.Equal("Desligado", f.Texto("situacao")));

        var financeiro = await (await _http.GetAsync("/api/funcionarios?departamentoId=3&situacao=Ativo")).LerAsync();
        Assert.Equal(["João Pedro Almeida", "Larissa Moreira"], financeiro.Nomes());

        var busca = await (await _http.GetAsync("/api/funcionarios?busca=DEVOPS")).LerAsync();
        Assert.Equal(["Eduarda Lima"], busca.Nomes());

        var ramal = await (await _http.GetAsync("/api/funcionarios?busca=2401")).LerAsync();
        Assert.Equal(["Natália Rocha"], ramal.Nomes());
    }

    [Fact]
    public async Task Ordena_por_salario_decrescente()
    {
        var lista = await (await _http.GetAsync("/api/funcionarios?situacao=Ativo&ordem=Salario&desc=true")).LerAsync();
        var salarios = lista.EnumerateArray().Select(f => f.GetProperty("salario").GetDecimal()).ToArray();

        Assert.Equal(salarios.OrderDescending(), salarios);
        Assert.Equal("Bruno Carvalho", lista[0].Texto("nome"));
    }

    [Fact]
    public async Task Criar_devolve_201_com_departamento_e_registra_admissao()
    {
        var criado = await CriarAsync("criar@teste.dev");
        var id = criado.GetProperty("id").GetInt32();

        Assert.Equal("Tecnologia", criado.Texto("departamento"));
        Assert.Equal("Ativo", criado.Texto("situacao"));

        var historico = await HistoricoAsync(id);
        var inclusao = Assert.Single(historico);
        Assert.Equal("Inclusao", inclusao.Texto("tipoAcao"));
        Assert.Equal("Tecnologia", inclusao.Texto("departamento"));
        Assert.Equal(5000m, inclusao.GetProperty("foto").GetProperty("salario").GetDecimal());
    }

    [Fact]
    public async Task Email_fica_minusculo_e_nao_pode_repetir()
    {
        var criado = await CriarAsync("Repetido@Teste.dev");
        Assert.Equal("repetido@teste.dev", criado.Texto("emailProfissional"));

        var repetido = await _http.PostAsJsonAsync("/api/funcionarios", Novo("repetido@teste.dev", "Outra Pessoa"));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
    }

    [Theory]
    [InlineData("ramal", "12")]
    [InlineData("salario", "0")]
    [InlineData("emailProfissional", "sem-arroba")]
    [InlineData("departamentoId", "999")]
    [InlineData("dataAdmissao", "2027-12-01")]
    public async Task Dados_invalidos_devolvem_400(string campo, string valor)
    {
        var corpo = JsonSerializer.SerializeToNode(Novo($"invalido-{campo}@teste.dev"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        corpo[campo] = campo is "salario" or "departamentoId" ? JsonValue.Create(decimal.Parse(valor)) : JsonValue.Create(valor);

        var resposta = await _http.PostAsJsonAsync("/api/funcionarios", corpo);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Atualizar_registra_so_os_campos_que_mudaram()
    {
        var id = (await CriarAsync("atualizar@teste.dev")).GetProperty("id").GetInt32();
        var alterado = Novo("atualizar@teste.dev", departamentoId: 2, salario: 6250.5m);

        var resposta = await _http.PutAsJsonAsync($"/api/funcionarios/{id}", alterado);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Recursos Humanos", (await resposta.LerAsync()).Texto("departamento"));

        var atualizacao = (await HistoricoAsync(id))[0];
        Assert.Equal("Atualizacao", atualizacao.Texto("tipoAcao"));
        Assert.Equal("Recursos Humanos", atualizacao.Texto("departamento"));
        var campos = atualizacao.GetProperty("alteracoes").EnumerateArray()
            .Select(a => (a.Texto("campo"), a.Texto("antes"), a.Texto("depois"))).ToArray();
        Assert.Equal(
            [("Departamento", "Tecnologia", "Recursos Humanos"), ("Salário", "R$ 5.000,00", "R$ 6.250,50")],
            campos);

        // Salvar de novo sem mudar nada não gera outro registro.
        await _http.PutAsJsonAsync($"/api/funcionarios/{id}", alterado);
        Assert.Equal(2, (await HistoricoAsync(id)).Length);
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
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PutAsJsonAsync("/api/funcionarios/99999", Novo("x@teste.dev"))).StatusCode);
    }

    [Fact]
    public async Task Exemplos_tem_linha_do_tempo_com_transferencia()
    {
        var rafael = (await (await _http.GetAsync("/api/funcionarios?busca=Rafael")).LerAsync())[0];
        var historico = await HistoricoAsync(rafael.GetProperty("id").GetInt32());

        Assert.Equal(["Atualizacao", "Atualizacao", "Inclusao"], historico.Select(r => r.Texto("tipoAcao")));
        var transferencia = historico[1].GetProperty("alteracoes").EnumerateArray().First(a => a.Texto("campo") == "Departamento");
        Assert.Equal("Comercial", transferencia.Texto("antes"));
        Assert.Equal("Tecnologia", transferencia.Texto("depois"));
    }
}

public class DepartamentosTests(CrachaFactory fabrica) : IClassFixture<CrachaFactory>
{
    private readonly HttpClient _http = fabrica.CreateClient();

    [Fact]
    public async Task Lista_com_ativos_e_folha()
    {
        var lista = await (await _http.GetAsync("/api/departamentos")).LerAsync();
        var financeiro = lista.EnumerateArray().Single(d => d.Texto("nome") == "Financeiro");

        Assert.Equal(6, lista.GetArrayLength());
        Assert.Equal(2, financeiro.GetProperty("ativos").GetInt32());
        Assert.Equal(21700m, financeiro.GetProperty("folha").GetDecimal());
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
        var resposta = await _http.DeleteAsync("/api/departamentos/1");
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
    private readonly HttpClient _http = fabrica.CreateClient();

    [Fact]
    public async Task Painel_com_os_dados_de_exemplo()
    {
        var p = await (await _http.GetAsync("/api/painel")).LerAsync();

        Assert.Equal(17, p.GetProperty("ativos").GetInt32());
        Assert.Equal(3, p.GetProperty("desligados").GetInt32());
        Assert.Equal(155400m, p.GetProperty("folhaMensal").GetDecimal());
        Assert.Equal(12, p.GetProperty("admissoesPorMes").GetArrayLength());
        Assert.Equal("Set/26", p.GetProperty("admissoesPorMes")[11].Texto("mes"));
        Assert.Equal("Tecnologia", p.GetProperty("porDepartamento")[0].Texto("nome"));

        var aniversarios = p.GetProperty("aniversariosDoMes").EnumerateArray()
            .Select(a => (a.Texto("nome"), a.GetProperty("anos").GetInt32())).ToArray();
        Assert.Equal([("Henrique Costa", 2), ("Ana Beatriz Souza", 3), ("Natália Rocha", 5), ("Eduarda Lima", 1)], aniversarios);
        Assert.Equal(6, p.GetProperty("ultimasAlteracoes").GetArrayLength());
    }

    [Fact]
    public async Task Historico_filtra_por_tipo_e_departamento()
    {
        var desligamentos = await (await _http.GetAsync("/api/historico?tipo=Desligamento")).LerAsync();
        Assert.Equal(new[] { "Vinícius Araújo", "Felipe Andrade", "Marcelo Teixeira" }.Order(), desligamentos.EnumerateArray().Select(r => r.Texto("nomeFuncionario")).Order());

        var marketing = await (await _http.GetAsync("/api/historico?departamento=Marketing")).LerAsync();
        Assert.All(marketing.EnumerateArray(), r => Assert.Equal("Marketing", r.Texto("departamento")));

        var datas = marketing.EnumerateArray().Select(r => r.GetProperty("quando").GetDateTimeOffset()).ToArray();
        Assert.Equal(datas.OrderDescending(), datas);
    }

    [Fact]
    public async Task Sistema_informa_onde_os_dados_ficam()
    {
        var s = await (await _http.GetAsync("/api/sistema")).LerAsync();
        Assert.False(string.IsNullOrEmpty(s.Texto("banco")));
        var esperado = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CRACHA_TABLES")) ? "Banco de dados" : "Azure Table (Azurite)";
        Assert.Equal(esperado, s.Texto("historico"));
    }
}

public class ComparacaoTests
{
    private static FotoFuncionario Foto(decimal salario = 1000, string cargo = "Analista", DateOnly? desligamento = null) =>
        new(1, "Fulano", cargo, "", "1234", "f@x.dev", "TI", salario, new DateOnly(2024, 1, 10),
            desligamento is null ? Situacao.Ativo : Situacao.Desligado, desligamento);

    [Fact]
    public void Inclusao_lista_os_campos_preenchidos()
    {
        var campos = FotoFuncionario.Comparar(null, Foto()).Select(c => c.Campo).ToArray();
        Assert.Equal(["Nome", "Cargo", "Departamento", "Salário", "Ramal", "E-mail", "Admissão", "Situação"], campos);
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
