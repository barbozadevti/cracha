using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cracha.Tests;

public class AusenciasTests(CrachaFactory fabrica) : IClassFixture<CrachaFactory>
{
    // Relógio dos testes: 24/09/2026.
    private static object Ferias(string inicio, string fim, int? funcionarioId = null, string tipo = "Ferias") =>
        new { funcionarioId, tipo, inicio, fim, observacao = "teste" };

    [Fact]
    public async Task Colaborador_pede_ferias_e_o_gestor_aprova()
    {
        var eduarda = fabrica.CreateClient();
        var admin = fabrica.Como(CrachaFactory.Admin);
        var id = await admin.IdAsync("Eduarda Lima");
        await admin.PostAsJsonAsync("/api/usuarios", new { nome = "Eduarda Lima", email = "eduarda.lima@cracha.dev", perfil = "Colaborador", funcionarioId = id, senha = "Eduarda2026" });
        await eduarda.PostAsJsonAsync("/api/conta/entrar", new { email = "eduarda.lima@cracha.dev", senha = "Eduarda2026" });

        var pedido = await eduarda.PostAsJsonAsync("/api/ausencias", Ferias("2026-11-02", "2026-11-16"));
        Assert.Equal(HttpStatusCode.Created, pedido.StatusCode);
        var criado = await pedido.LerAsync();
        Assert.Equal("Pendente", criado.Texto("status"));
        Assert.Equal(15, criado.GetProperty("dias").GetInt32());
        Assert.False(criado.GetProperty("podeDecidir").GetBoolean());

        var bruno = fabrica.Como(CrachaFactory.Gestor);
        var paraDecidir = await (await bruno.GetAsync("/api/ausencias?paraDecidir=true")).LerAsync();
        Assert.Contains("Eduarda Lima", paraDecidir.EnumerateArray().Select(a => a.Texto("funcionario")));

        var decisao = await (await bruno.PostAsJsonAsync($"/api/ausencias/{criado.GetProperty("id").GetInt32()}/decisao", new { aprovar = true })).LerAsync();
        Assert.Equal("Aprovada", decisao.Texto("status"));
        Assert.Equal("Bruno Carvalho (Gestor)", decisao.Texto("decididaPor"));
    }

    [Fact]
    public async Task Periodo_sobreposto_e_bloqueado()
    {
        // Camila já está de férias até 05/10.
        var rh = fabrica.Como(CrachaFactory.Rh);
        var camila = await rh.IdAsync("Camila Ribeiro");

        var resposta = await rh.PostAsJsonAsync("/api/ausencias", Ferias("2026-10-01", "2026-10-10", camila));

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Contains("férias", (await resposta.LerAsync()).Texto("title"));
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-12")] // menos de 5 dias
    [InlineData("2026-10-10", "2026-11-20")] // mais de 30 dias
    [InlineData("2026-10-10", "2026-10-01")] // fim antes do início
    [InlineData("2026-09-01", "2026-09-10")] // no passado
    public async Task Regras_de_ferias_devolvem_400(string inicio, string fim)
    {
        var ana = fabrica.Como(CrachaFactory.Colaborador);
        var resposta = await ana.PostAsJsonAsync("/api/ausencias", Ferias(inicio, fim));
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Colaborador_nao_lanca_atestado_nem_pede_por_outra_pessoa()
    {
        var ana = fabrica.Como(CrachaFactory.Colaborador);
        var diego = await ana.IdAsync("Diego Martins");

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsJsonAsync("/api/ausencias", Ferias("2026-12-01", "2026-12-02", tipo: "Atestado"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsJsonAsync("/api/ausencias", Ferias("2026-12-01", "2026-12-10", diego))).StatusCode);
    }

    [Fact]
    public async Task RH_lanca_atestado_ja_aprovado_e_ate_retroativo()
    {
        var rh = fabrica.Como(CrachaFactory.Rh);
        var paula = await rh.IdAsync("Paula Mendes");

        var atestado = await (await rh.PostAsJsonAsync("/api/ausencias", Ferias("2026-09-22", "2026-09-23", paula, "Atestado"))).LerAsync();

        Assert.Equal("Aprovada", atestado.Texto("status"));
        Assert.Equal("Gabriela Nunes (RH)", atestado.Texto("decididaPor"));
    }

    [Fact]
    public async Task Gestor_nao_decide_fora_da_equipe_nem_o_proprio_pedido()
    {
        var rh = fabrica.Como(CrachaFactory.Rh);
        var bruno = fabrica.Como(CrachaFactory.Gestor);

        // Pedido do Bruno: quem decide é a Helena (gestora dele) ou o RH, nunca ele.
        var proprio = await (await bruno.PostAsJsonAsync("/api/ausencias", Ferias("2026-12-07", "2026-12-18"))).LerAsync();
        var proprioId = proprio.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Forbidden, (await bruno.PostAsJsonAsync($"/api/ausencias/{proprioId}/decisao", new { aprovar = true })).StatusCode);

        // A Ana pede e o Bruno aprova; já a Isabela (equipe da Gabriela) não é com ele.
        var helena = fabrica.Como(CrachaFactory.Diretora);
        var aprovadoPelaDiretora = await helena.PostAsJsonAsync($"/api/ausencias/{proprioId}/decisao", new { aprovar = true });
        Assert.Equal(HttpStatusCode.OK, aprovadoPelaDiretora.StatusCode);

        var isabela = await rh.IdAsync("Isabela Freitas");
        var pedidoIsabela = await (await rh.PostAsJsonAsync("/api/ausencias", Ferias("2027-01-04", "2027-01-15", isabela))).LerAsync();
        // Lançado pelo RH já vem aprovado; o gestor de outra equipe nem enxerga.
        var listaBruno = await (await bruno.GetAsync("/api/ausencias")).LerAsync();
        Assert.DoesNotContain(pedidoIsabela.GetProperty("id").GetInt32(), listaBruno.EnumerateArray().Select(a => a.GetProperty("id").GetInt32()));
    }

    [Fact]
    public async Task Recusar_exige_motivo_e_nao_se_decide_duas_vezes()
    {
        var bruno = fabrica.Como(CrachaFactory.Gestor);
        var pendente = (await (await bruno.GetAsync("/api/ausencias?paraDecidir=true")).LerAsync()).EnumerateArray()
            .First(a => a.Texto("funcionario") == "Diego Martins");
        var id = pendente.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.BadRequest, (await bruno.PostAsJsonAsync($"/api/ausencias/{id}/decisao", new { aprovar = false })).StatusCode);

        var recusa = await (await bruno.PostAsJsonAsync($"/api/ausencias/{id}/decisao", new { aprovar = false, motivo = "Entrega da sprint" })).LerAsync();
        Assert.Equal("Recusada", recusa.Texto("status"));
        Assert.Equal("Entrega da sprint", recusa.Texto("motivoRecusa"));

        Assert.Equal(HttpStatusCode.Conflict, (await bruno.PostAsJsonAsync($"/api/ausencias/{id}/decisao", new { aprovar = true })).StatusCode);
    }

    [Fact]
    public async Task Colaborador_cancela_o_proprio_pedido_pendente()
    {
        var ana = fabrica.Como(CrachaFactory.Colaborador);
        var pedido = (await (await ana.GetAsync("/api/ausencias?status=Pendente")).LerAsync()).EnumerateArray()
            .Single(a => a.Texto("funcionario") == "Ana Beatriz Souza");
        Assert.True(pedido.GetProperty("podeCancelar").GetBoolean());

        var cancelado = await (await ana.PostAsync($"/api/ausencias/{pedido.GetProperty("id").GetInt32()}/cancelar", null)).LerAsync();
        Assert.Equal("Cancelada", cancelado.Texto("status"));
    }

    [Fact]
    public async Task Colaborador_so_ve_as_proprias_ausencias()
    {
        var ana = fabrica.Como(CrachaFactory.Colaborador);
        var lista = await (await ana.GetAsync("/api/ausencias")).LerAsync();
        Assert.All(lista.EnumerateArray(), a => Assert.Equal("Ana Beatriz Souza", a.Texto("funcionario")));
    }

    [Fact]
    public async Task Desligar_cancela_pedidos_pendentes()
    {
        var rh = fabrica.Como(CrachaFactory.Rh);
        var diretoria = await rh.DepartamentoAsync("Diretoria");
        var novo = await (await rh.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = "Pessoa Temporária", cargo = "Estagiária", ramal = "9100", emailProfissional = "temp@teste.dev",
            departamentoId = diretoria, salario = 2000, dataAdmissao = "2026-01-10",
        })).LerAsync();
        var id = novo.GetProperty("id").GetInt32();
        await rh.PostAsJsonAsync("/api/ausencias", Ferias("2026-11-02", "2026-11-11", id));

        await rh.PostAsJsonAsync($"/api/funcionarios/{id}/desligar", new { });

        var dela = await (await rh.GetAsync($"/api/ausencias?funcionarioId={id}")).LerAsync();
        Assert.Equal(JsonValueKind.Array, dela.ValueKind);
        // Lançada pelo RH, já estava aprovada: continua no registro (o desligamento só cancela as pendentes).
        Assert.All(dela.EnumerateArray(), a => Assert.NotEqual("Pendente", a.Texto("status")));
    }
}
