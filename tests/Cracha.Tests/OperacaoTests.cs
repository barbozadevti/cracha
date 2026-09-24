using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Cracha.Api.Controllers;

namespace Cracha.Tests;

public class OperacaoTests(CrachaFactory fabrica) : IClassFixture<CrachaFactory>
{
    // Menor PNG válido (1x1 pixel transparente).
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private static MultipartFormDataContent Arquivo(byte[] bytes, string nome = "foto.png", string tipo = "image/png")
    {
        var conteudo = new ByteArrayContent(bytes);
        conteudo.Headers.ContentType = new MediaTypeHeaderValue(tipo);
        return new MultipartFormDataContent { { conteudo, "arquivo", nome } };
    }

    [Fact]
    public async Task Health_mostra_cada_componente_sem_login()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/health");
        var saude = await resposta.LerAsync();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Healthy", saude.Texto("status"));
        Assert.Equal(["banco", "historico", "fotos"], saude.GetProperty("componentes").EnumerateObject().Select(c => c.Name));
    }

    [Fact]
    public async Task Respostas_trazem_cabecalhos_de_seguranca()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/");

        Assert.Equal("nosniff", resposta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", resposta.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("script-src 'self'", resposta.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Colaborador_envia_a_propria_foto_e_ela_aparece_no_cadastro()
    {
        var ana = fabrica.Como(CrachaFactory.Colaborador);
        var id = await ana.IdAsync("Ana Beatriz Souza");

        var envio = await ana.PutAsync($"/api/funcionarios/{id}/foto", Arquivo(Png));
        Assert.Equal(HttpStatusCode.OK, envio.StatusCode);

        var url = (await ana.FuncionarioAsync("Ana Beatriz Souza")).Texto("fotoUrl");
        var foto = await ana.GetAsync(url);
        Assert.Equal("image/png", foto.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await foto.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await ana.DeleteAsync($"/api/funcionarios/{id}/foto")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync($"/api/funcionarios/{id}/foto")).StatusCode);
    }

    [Fact]
    public async Task Foto_e_validada_pelo_conteudo_e_pela_permissao()
    {
        var ana = fabrica.Como(CrachaFactory.Colaborador);
        var anaId = await ana.IdAsync("Ana Beatriz Souza");
        var brunoId = await ana.IdAsync("Bruno Carvalho");

        // Texto disfarçado de PNG: a extensão e o Content-Type não enganam a checagem dos bytes.
        var falso = await ana.PutAsync($"/api/funcionarios/{anaId}/foto", Arquivo(Encoding.UTF8.GetBytes("<script>alert(1)</script>")));
        Assert.Equal(HttpStatusCode.BadRequest, falso.StatusCode);

        var deOutro = await ana.PutAsync($"/api/funcionarios/{brunoId}/foto", Arquivo(Png));
        Assert.Equal(HttpStatusCode.Forbidden, deOutro.StatusCode);
    }

    [Fact]
    public async Task QR_Code_do_cracha_e_um_svg()
    {
        var rh = fabrica.Como(CrachaFactory.Rh);
        var resposta = await rh.GetAsync($"/api/funcionarios/{await rh.IdAsync("Helena Prado")}/qrcode");

        Assert.Equal("image/svg+xml", resposta.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("<svg", (await resposta.Content.ReadAsStringAsync()).TrimStart());
    }

    [Fact]
    public async Task CSV_de_funcionarios_abre_no_Excel_e_respeita_a_visibilidade()
    {
        var rh = await (await fabrica.Como(CrachaFactory.Rh).GetAsync("/api/exportar/funcionarios.csv")).Content.ReadAsByteArrayAsync();
        var ana = await (await fabrica.Como(CrachaFactory.Colaborador).GetAsync("/api/exportar/funcionarios.csv")).Content.ReadAsByteArrayAsync();

        // UTF-8 com BOM: o Excel reconhece os acentos.
        Assert.Equal(Encoding.UTF8.GetPreamble(), rh[..3]);
        var linhasRh = Encoding.UTF8.GetString(rh[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var linhasAna = Encoding.UTF8.GetString(ana[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Matrícula;Nome;Cargo", linhasRh[0]);
        Assert.Contains(linhasRh, l => l.Contains("Bruno Carvalho") && l.Contains("17.200,00"));
        Assert.Contains(linhasAna, l => l.Contains("Bruno Carvalho") && !l.Contains("17.200,00"));
        Assert.Contains(linhasAna, l => l.Contains("Ana Beatriz Souza") && l.Contains("10.500,00"));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+5511999", "'+5511999")]
    [InlineData("Rua A; 10", "\"Rua A; 10\"")]
    [InlineData("Normal", "Normal")]
    public void CSV_neutraliza_formulas_e_escapa_separadores(string valor, string esperado) =>
        Assert.Equal(esperado, ExportacaoController.Celula(valor));
}
