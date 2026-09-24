using System.Text.Json;
using Cracha.Api.Dados;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cracha.Api.Seguranca;

/// <summary>Health checks, cabeçalhos de segurança e proxy reverso (App Service, contêiner).</summary>
public static class Operacao
{
    public static IServiceCollection AdicionarOperacao(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<BancoSaudavel>("banco", tags: ["pronto"])
            .AddCheck<HistoricoSaudavel>("historico", tags: ["pronto"])
            .AddCheck<FotosSaudaveis>("fotos", tags: ["pronto"]);

        // No App Service e atrás de proxies o HTTPS termina antes da aplicação.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
        });
        return services;
    }

    /// <summary>/health/vivo responde se o processo está de pé; /health, se banco e armazenamentos respondem.</summary>
    public static void MapearSaude(this WebApplication app)
    {
        app.MapHealthChecks("/health/vivo", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains("pronto"),
            ResponseWriter = async (contexto, relatorio) =>
            {
                contexto.Response.ContentType = "application/json";
                await contexto.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    status = relatorio.Status.ToString(),
                    duracaoMs = Math.Round(relatorio.TotalDuration.TotalMilliseconds),
                    componentes = relatorio.Entries.ToDictionary(e => e.Key, e => new
                    {
                        status = e.Value.Status.ToString(),
                        descricao = e.Value.Description,
                        duracaoMs = Math.Round(e.Value.Duration.TotalMilliseconds),
                    }),
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            },
        }).AllowAnonymous();
    }

    /// <summary>Cabeçalhos que endurecem o navegador contra XSS, clickjacking e vazamento de dados.</summary>
    public static IApplicationBuilder UsarCabecalhosDeSeguranca(this IApplicationBuilder app) => app.Use(async (contexto, proximo) =>
    {
        var h = contexto.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "same-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        // O Swagger UI usa scripts inline; o resto do sistema só carrega arquivos do próprio servidor.
        if (!contexto.Request.Path.StartsWithSegments("/swagger"))
            h.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; " +
                "script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        await proximo();
    });
}

public sealed class BancoSaudavel(CrachaContext contexto) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await contexto.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy(contexto.Database.ProviderName?.Split('.').Last())
            : HealthCheckResult.Unhealthy("Sem conexão com o banco.");
}

public sealed class HistoricoSaudavel(IHistorico historico) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await historico.ListarAsync(new FiltroHistorico(Limite: 1), ct);
            return HealthCheckResult.Healthy(historico.Descricao);
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy($"{historico.Descricao}: {e.GetType().Name}");
        }
    }
}

public sealed class FotosSaudaveis(IArmazenamentoFotos fotos) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await fotos.AbrirAsync(0, ct);
            return HealthCheckResult.Healthy(fotos.Descricao);
        }
        catch (Exception e)
        {
            // Sem fotos o sistema segue funcionando: degradado, não fora do ar.
            return HealthCheckResult.Degraded($"{fotos.Descricao}: {e.GetType().Name}");
        }
    }
}
