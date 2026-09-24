using Cracha.Api.Modelos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Dados;

public enum Provedor { Sqlite, SqlServer }

public sealed class OpcoesBanco
{
    public Provedor Provedor { get; set; } = Provedor.Sqlite;
    public string ConnectionString { get; set; } = "Data Source=cracha.db";

    /// <summary>Cria departamentos, funcionários e histórico de exemplo na primeira execução.</summary>
    public bool CriarExemplos { get; set; } = true;
}

public static class ConfiguracaoBanco
{
    public static IServiceCollection AdicionarBanco(this IServiceCollection services, OpcoesBanco opcoes, string pastaDados)
    {
        if (opcoes.Provedor == Provedor.SqlServer)
        {
            services.AddDbContext<CrachaSqlServerContext>(o => o.UseSqlServer(opcoes.ConnectionString, sql => sql.EnableRetryOnFailure()));
            services.AddScoped<CrachaContext>(sp => sp.GetRequiredService<CrachaSqlServerContext>());
        }
        else
        {
            var connectionString = CaminhoSqlite(opcoes.ConnectionString, pastaDados);
            services.AddDbContext<CrachaSqliteContext>(o => o.UseSqlite(connectionString));
            services.AddScoped<CrachaContext>(sp => sp.GetRequiredService<CrachaSqliteContext>());
        }
        return services;
    }

    public static IServiceCollection AdicionarHistorico(this IServiceCollection services, OpcoesHistorico opcoes)
    {
        services.AddSingleton(opcoes);
        if (opcoes.Provedor == ProvedorHistorico.AzureTable)
            services.AddSingleton<IHistorico, HistoricoAzureTable>();
        else
            services.AddScoped<IHistorico, HistoricoNoBanco>();
        return services;
    }

    /// <summary>Aplica as migrations pendentes e, se o banco estiver vazio, cria os dados de exemplo.</summary>
    public static async Task PrepararBancoAsync(this IServiceProvider services, OpcoesBanco opcoes)
    {
        await using var escopo = services.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<CrachaContext>();
        await contexto.Database.MigrateAsync();

        if (!opcoes.CriarExemplos || await contexto.Departamentos.AnyAsync())
            return;

        var agora = escopo.ServiceProvider.GetRequiredService<TimeProvider>().GetLocalNow();
        var historico = escopo.ServiceProvider.GetRequiredService<IHistorico>();
        await Exemplos.CriarAsync(contexto, historico, agora);
    }

    private static string CaminhoSqlite(string connectionString, string pastaDados)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.DataSource != ":memory:" && !Path.IsPathRooted(builder.DataSource))
        {
            Directory.CreateDirectory(pastaDados);
            builder.DataSource = Path.Combine(pastaDados, builder.DataSource);
        }
        return builder.ToString();
    }
}
