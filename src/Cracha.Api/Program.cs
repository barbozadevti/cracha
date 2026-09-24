using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Serialization;
using Cracha.Api.Dados;
using Cracha.Api.Seguranca;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var opcoesBanco = builder.Configuration.GetSection("Banco").Get<OpcoesBanco>() ?? new OpcoesBanco();
var opcoesHistorico = builder.Configuration.GetSection("Historico").Get<OpcoesHistorico>() ?? new OpcoesHistorico();
var opcoesFotos = builder.Configuration.GetSection("Fotos").Get<OpcoesFotos>() ?? new OpcoesFotos();
var opcoesAcesso = builder.Configuration.GetSection("Acesso").Get<OpcoesAcesso>() ?? new OpcoesAcesso();
var pastaDados = Path.Combine(builder.Environment.ContentRootPath, "App_Data");

builder.Services.AdicionarBanco(opcoesBanco, pastaDados);
builder.Services.AdicionarHistorico(opcoesHistorico);
builder.Services.AdicionarFotos(opcoesFotos, pastaDados);
builder.Services.AdicionarAcesso(opcoesAcesso);
builder.Services.AdicionarOperacao();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Crachá API",
        Version = "v1",
        Description = "Sistema de RH: funcionários, organograma, férias e ausências, painel e histórico de alterações em Azure Table. " +
            "Autenticação por cookie: entre pela tela do sistema (ou POST /api/conta/entrar) e use o Swagger na mesma aba.",
    });
    o.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));
});

var app = builder.Build();

await app.Services.PrepararBancoAsync(opcoesBanco);
await app.Services.PrepararAcessosAsync(opcoesBanco.CriarExemplos);

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UsarCabecalhosDeSeguranca();
app.UseSwagger();
app.UseSwaggerUI(o => o.DocumentTitle = "Crachá API");
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapearSaude();
app.MapControllers();

// Usado pelo atalho da Área de Trabalho: abre o navegador quando o servidor fica pronto.
if (app.Configuration.GetValue<bool>("AbrirNavegador"))
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var endereco = app.Urls.FirstOrDefault() ?? "http://localhost:5210";
        Process.Start(new ProcessStartInfo(endereco) { UseShellExecute = true });
    });
}

app.Run();

public partial class Program;
