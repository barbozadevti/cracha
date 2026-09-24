using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cracha.Tests;

/// <summary>
/// Relógio que começa em 24/09/2026 12:00 e anda um segundo a cada leitura: "hoje", tempo de casa
/// e aniversários ficam previsíveis, e as alterações seguidas de um teste ganham horários em ordem.
/// </summary>
public sealed class RelogioFixo : TimeProvider
{
    public static readonly DateTime Agora = new(2026, 9, 24, 12, 0, 0);
    private long _leituras;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public override DateTimeOffset GetUtcNow() => new(Agora.AddSeconds(Interlocked.Increment(ref _leituras)), TimeSpan.Zero);
}

/// <summary>
/// API com banco novo e os dados de exemplo. Por padrão usa SQLite num arquivo temporário e o
/// histórico no próprio banco. Variáveis de ambiente opcionais:
/// <list type="bullet">
/// <item>CRACHA_SQLSERVER: connection string (sem Database) para rodar contra o SQL Server;</item>
/// <item>CRACHA_TABLES: connection string da Storage Account (ex.: "UseDevelopmentStorage=true" com o Azurite)
/// para gravar o histórico numa Azure Table de verdade.</item>
/// </list>
/// Bancos e tabelas temporários são apagados no fim.
/// </summary>
public class CrachaFactory : WebApplicationFactory<Program>
{
    private static readonly string? Servidor = Environment.GetEnvironmentVariable("CRACHA_SQLSERVER");
    private static readonly string? Tabelas = Environment.GetEnvironmentVariable("CRACHA_TABLES");
    private readonly string _nome = $"cracha_teste_{Guid.NewGuid():N}";
    // Serve para a tabela do histórico e para o container de fotos (minúsculas, letras e números).
    private string Tabela => $"t{_nome.Replace("_", "")}"[..40];

    static CrachaFactory()
    {
        // Roda em pt-BR para pegar erros de cultura (vírgula decimal) que no CI em inglês passariam.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
    }

    private string ArquivoSqlite => Path.Combine(Path.GetTempPath(), $"{_nome}.db");
    private string PastaFotos => Path.Combine(Path.GetTempPath(), $"{_nome}_fotos");

    /// <summary>Alto nos testes comuns; a classe que testa o bloqueio usa um limite baixo.</summary>
    protected virtual int TentativasPorMinuto => 1000;

    public const string Senha = "Cracha@2026";
    public const string Admin = "admin@cracha.dev", Rh = "gabriela.nunes@cracha.dev", Diretora = "helena.prado@cracha.dev",
        Gestor = "bruno.carvalho@cracha.dev", Colaborador = "ana.souza@cracha.dev";

    private readonly Dictionary<string, HttpClient> _clientes = [];

    /// <summary>Cliente já logado com a conta de demonstração (um por conta, reaproveitado na classe).</summary>
    public HttpClient Como(string email)
    {
        lock (_clientes)
        {
            if (_clientes.TryGetValue(email, out var existente))
                return existente;
            var cliente = CreateClient();
            var resposta = cliente.PostAsJsonAsync("/api/conta/entrar", new { email, senha = Senha }).GetAwaiter().GetResult();
            resposta.EnsureSuccessStatusCode();
            return _clientes[email] = cliente;
        }
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(Servidor))
        {
            builder.UseSetting("Banco:Provedor", "Sqlite");
            builder.UseSetting("Banco:ConnectionString", $"Data Source={ArquivoSqlite}");
        }
        else
        {
            builder.UseSetting("Banco:Provedor", "SqlServer");
            builder.UseSetting("Banco:ConnectionString", new SqlConnectionStringBuilder(Servidor) { InitialCatalog = _nome }.ToString());
        }

        if (!string.IsNullOrWhiteSpace(Tabelas))
        {
            builder.UseSetting("Historico:Provedor", "AzureTable");
            builder.UseSetting("Historico:ConnectionString", Tabelas);
            builder.UseSetting("Historico:Tabela", Tabela);
        }

        builder.UseSetting("Acesso:TentativasPorMinuto", TentativasPorMinuto.ToString());
        builder.UseSetting("Fotos:Provedor", string.IsNullOrWhiteSpace(Tabelas) ? "Disco" : "Blob");
        builder.UseSetting("Fotos:ConnectionString", Tabelas ?? "");
        builder.UseSetting("Fotos:Container", Tabela);
        builder.UseSetting("Fotos:Pasta", PastaFotos);

        builder.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(new RelogioFixo());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(PastaFotos))
            Directory.Delete(PastaFotos, recursive: true);
        if (!string.IsNullOrWhiteSpace(Tabelas))
        {
            new TableServiceClient(Tabelas).DeleteTable(Tabela);
            new Azure.Storage.Blobs.BlobContainerClient(Tabelas, Tabela).DeleteIfExists();
        }

        if (string.IsNullOrWhiteSpace(Servidor))
        {
            SqliteConnection.ClearAllPools();
            File.Delete(ArquivoSqlite);
            return;
        }

        // O descarte pode ser chamado mais de uma vez (Dispose e DisposeAsync).
        SqlConnection.ClearAllPools();
        using var conexao = new SqlConnection(Servidor);
        conexao.Open();
        using var comando = new SqlCommand(
            $"IF DB_ID(N'{_nome}') IS NOT NULL BEGIN ALTER DATABASE [{_nome}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_nome}]; END",
            conexao);
        comando.ExecuteNonQuery();
    }
}

internal static class Json
{
    public static async Task<JsonElement> LerAsync(this HttpResponseMessage resposta) =>
        await resposta.Content.ReadFromJsonAsync<JsonElement>();

    public static string[] Nomes(this JsonElement lista) =>
        lista.EnumerateArray().Select(f => f.GetProperty("nome").GetString()!).ToArray();

    public static string Texto(this JsonElement e, string propriedade) => e.GetProperty(propriedade).GetString()!;
}
