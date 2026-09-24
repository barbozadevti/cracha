using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cracha.Tests;

public class AcessoTests(CrachaFactory fabrica) : IClassFixture<CrachaFactory>
{
    [Fact]
    public async Task Sem_login_a_api_responde_401_mas_a_tela_e_o_health_abrem()
    {
        var anonimo = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/funcionarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/conta/eu")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync("/health/vivo")).StatusCode);
    }

    [Fact]
    public async Task Senha_errada_e_email_inexistente_tem_a_mesma_resposta()
    {
        var anonimo = fabrica.CreateClient();
        var senhaErrada = await anonimo.PostAsJsonAsync("/api/conta/entrar", new { email = CrachaFactory.Rh, senha = "errada123" });
        var inexistente = await anonimo.PostAsJsonAsync("/api/conta/entrar", new { email = "ninguem@cracha.dev", senha = "errada123" });

        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);
        Assert.Equal((await senhaErrada.LerAsync()).Texto("title"), (await inexistente.LerAsync()).Texto("title"));
    }

    [Fact]
    public async Task Login_devolve_perfil_e_permissoes_e_sair_encerra_a_sessao()
    {
        var cliente = fabrica.CreateClient();
        var resposta = await cliente.PostAsJsonAsync("/api/conta/entrar", new { email = CrachaFactory.Gestor, senha = CrachaFactory.Senha });
        var sessao = await resposta.LerAsync();

        Assert.Equal("Gestor", sessao.Texto("perfil"));
        Assert.True(sessao.GetProperty("permissoes").GetProperty("aprovarAusencias").GetBoolean());
        Assert.False(sessao.GetProperty("permissoes").GetProperty("gerirCadastro").GetBoolean());
        var cookie = Assert.Single(resposta.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/conta/eu")).StatusCode);
        await cliente.PostAsync("/api/conta/sair", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/conta/eu")).StatusCode);
    }

    [Fact]
    public async Task Colaborador_ve_o_diretorio_sem_salario_e_endereco_dos_outros()
    {
        var ana = fabrica.Como(CrachaFactory.Colaborador);
        var lista = (await (await ana.GetAsync("/api/funcionarios")).LerAsync()).EnumerateArray().ToList();

        var propria = lista.Single(f => f.Texto("nome") == "Ana Beatriz Souza");
        var outra = lista.Single(f => f.Texto("nome") == "Bruno Carvalho");
        Assert.Equal(10500m, propria.GetProperty("salario").GetDecimal());
        Assert.True(propria.GetProperty("detalhes").GetBoolean());
        Assert.Equal(JsonValueKind.Null, outra.GetProperty("salario").ValueKind);
        Assert.Equal(JsonValueKind.Null, outra.GetProperty("endereco").ValueKind);
        Assert.False(outra.GetProperty("detalhes").GetBoolean());
    }

    [Fact]
    public async Task Colaborador_nao_ordena_por_salario_nem_ve_historico_alheio()
    {
        var ana = fabrica.Como(CrachaFactory.Colaborador);

        // Pedir ordem por salário cai na ordem por nome: a posição revelaria a faixa salarial.
        var nomes = (await (await ana.GetAsync("/api/funcionarios?ordem=Salario&desc=true")).LerAsync()).Nomes();
        Assert.Equal(nomes.Order(StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), false)), nomes);

        var bruno = await ana.IdAsync("Bruno Carvalho");
        var anaId = await ana.IdAsync("Ana Beatriz Souza");
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync($"/api/funcionarios/{bruno}/historico")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync($"/api/funcionarios/{anaId}/historico")).StatusCode);
    }

    [Fact]
    public async Task Colaborador_e_gestor_nao_alteram_o_cadastro()
    {
        foreach (var email in new[] { CrachaFactory.Colaborador, CrachaFactory.Gestor })
        {
            var http = fabrica.Como(email);
            var id = await http.IdAsync("Diego Martins");
            Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync($"/api/funcionarios/{id}/desligar", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await http.DeleteAsync($"/api/funcionarios/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync("/api/departamentos", new { nome = "X", cor = "#000000" })).StatusCode);
        }
    }

    [Fact]
    public async Task Gestor_ve_salario_da_equipe_inteira_e_so_dela()
    {
        var helena = fabrica.Como(CrachaFactory.Diretora);
        var bruno = fabrica.Como(CrachaFactory.Gestor);

        // A diretora lidera todos, direta ou indiretamente.
        var todos = (await (await helena.GetAsync("/api/funcionarios?situacao=Ativo")).LerAsync()).EnumerateArray();
        Assert.All(todos, f => Assert.True(f.GetProperty("detalhes").GetBoolean()));

        var doBruno = (await (await bruno.GetAsync("/api/funcionarios?situacao=Ativo")).LerAsync()).EnumerateArray()
            .Where(f => f.GetProperty("detalhes").GetBoolean()).Select(f => f.Texto("nome")).Order();
        Assert.Equal(new[] { "Ana Beatriz Souza", "Bruno Carvalho", "Camila Ribeiro", "Diego Martins", "Eduarda Lima", "Rafael Gomes" }, doBruno);
    }

    [Fact]
    public async Task Somente_o_administrador_gerencia_acessos()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await fabrica.Como(CrachaFactory.Rh).GetAsync("/api/usuarios")).StatusCode);

        var admin = fabrica.Como(CrachaFactory.Admin);
        var usuarios = await (await admin.GetAsync("/api/usuarios")).LerAsync();
        var emails = usuarios.EnumerateArray().Select(u => u.Texto("email")).ToList();
        Assert.Subset(emails.ToHashSet(), new HashSet<string> { CrachaFactory.Admin, CrachaFactory.Rh, CrachaFactory.Diretora, CrachaFactory.Gestor, CrachaFactory.Colaborador });
        Assert.DoesNotContain("senha", usuarios.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Novo_acesso_precisa_de_funcionario_e_senha_forte()
    {
        var admin = fabrica.Como(CrachaFactory.Admin);
        var diego = await admin.IdAsync("Diego Martins");

        var semFuncionario = await admin.PostAsJsonAsync("/api/usuarios", new { nome = "Diego", email = "diego.martins@cracha.dev", perfil = "Colaborador", senha = "Senha1234" });
        Assert.Equal(HttpStatusCode.BadRequest, semFuncionario.StatusCode);

        var fraca = await admin.PostAsJsonAsync("/api/usuarios", new { nome = "Diego", email = "diego.martins@cracha.dev", perfil = "Colaborador", funcionarioId = diego, senha = "12345678" });
        Assert.Equal(HttpStatusCode.BadRequest, fraca.StatusCode);

        var criado = await admin.PostAsJsonAsync("/api/usuarios", new { nome = "Diego Martins", email = "diego.martins@cracha.dev", perfil = "Colaborador", funcionarioId = diego, senha = "Diego2026" });
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);

        var entrou = await fabrica.CreateClient().PostAsJsonAsync("/api/conta/entrar", new { email = "diego.martins@cracha.dev", senha = "Diego2026" });
        Assert.Equal(HttpStatusCode.OK, entrou.StatusCode);
    }

    [Fact]
    public async Task Mudar_o_perfil_derruba_a_sessao_aberta()
    {
        var admin = fabrica.Como(CrachaFactory.Admin);
        var isabela = await admin.IdAsync("Isabela Freitas");
        var criado = await (await admin.PostAsJsonAsync("/api/usuarios",
            new { nome = "Isabela Freitas", email = "isabela.freitas@cracha.dev", perfil = "RH", funcionarioId = isabela, senha = "Isabela2026" })).LerAsync();

        var sessao = fabrica.CreateClient();
        await sessao.PostAsJsonAsync("/api/conta/entrar", new { email = "isabela.freitas@cracha.dev", senha = "Isabela2026" });
        Assert.Equal(HttpStatusCode.OK, (await sessao.GetAsync("/api/painel")).StatusCode);

        var id = criado.GetProperty("id").GetInt32();
        await admin.PutAsJsonAsync($"/api/usuarios/{id}",
            new { nome = "Isabela Freitas", email = "isabela.freitas@cracha.dev", perfil = "Colaborador", funcionarioId = isabela, ativo = true });

        Assert.Equal(HttpStatusCode.Unauthorized, (await sessao.GetAsync("/api/painel")).StatusCode);
    }

    [Fact]
    public async Task Admin_nao_pode_se_rebaixar()
    {
        var admin = fabrica.Como(CrachaFactory.Admin);
        var eu = await (await admin.GetAsync("/api/conta/eu")).LerAsync();
        var resposta = await admin.PutAsJsonAsync($"/api/usuarios/{eu.GetProperty("id").GetInt32()}",
            new { nome = "Administrador", email = CrachaFactory.Admin, perfil = "RH", ativo = true });
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    [Fact]
    public async Task Troca_de_senha_exige_a_atual_e_senha_forte()
    {
        var cliente = fabrica.CreateClient();
        await cliente.PostAsJsonAsync("/api/conta/entrar", new { email = CrachaFactory.Diretora, senha = CrachaFactory.Senha });

        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync("/api/conta/senha", new { atual = "errada", nova = "NovaSenha1" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync("/api/conta/senha", new { atual = CrachaFactory.Senha, nova = "curta" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.PostAsJsonAsync("/api/conta/senha", new { atual = CrachaFactory.Senha, nova = "NovaSenha1" })).StatusCode);

        // A sessão atual continua valendo com o cookie renovado.
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/conta/eu")).StatusCode);
        await cliente.PostAsJsonAsync("/api/conta/senha", new { atual = "NovaSenha1", nova = CrachaFactory.Senha });
    }
}

/// <summary>Fábrica com limite baixo de tentativas, para testar o bloqueio de força bruta.</summary>
public sealed class FabricaComLimite : CrachaFactory
{
    protected override int TentativasPorMinuto => 3;
}

public class LimiteDeLoginTests(FabricaComLimite fabrica) : IClassFixture<FabricaComLimite>
{
    [Fact]
    public async Task Muitas_tentativas_seguidas_sao_bloqueadas_com_429()
    {
        var anonimo = fabrica.CreateClient();
        var codigos = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            codigos.Add((await anonimo.PostAsJsonAsync("/api/conta/entrar", new { email = "x@cracha.dev", senha = "chute123" })).StatusCode);

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], codigos);
    }
}
