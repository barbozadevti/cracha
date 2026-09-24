using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.Data.Tables;
using Cracha.Api.Modelos;
using Microsoft.EntityFrameworkCore;

namespace Cracha.Api.Dados;

public sealed record FiltroHistorico(int? FuncionarioId = null, string? Departamento = null, TipoAcao? Tipo = null, int Limite = 100);

/// <summary>Onde o histórico de alterações é gravado: Azure Table ou o próprio banco.</summary>
public interface IHistorico
{
    string Descricao { get; }
    Task RegistrarAsync(RegistroHistorico registro, CancellationToken ct = default);
    Task<IReadOnlyList<RegistroHistorico>> ListarAsync(FiltroHistorico filtro, CancellationToken ct = default);
}

public enum ProvedorHistorico { Banco, AzureTable }

public sealed class OpcoesHistorico
{
    public ProvedorHistorico Provedor { get; set; } = ProvedorHistorico.Banco;

    /// <summary>Connection string da Storage Account. "UseDevelopmentStorage=true" usa o Azurite.</summary>
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";

    public string Tabela { get; set; } = "FuncionarioLog";
}

/// <summary>Histórico numa tabela do mesmo banco. Funciona sem nenhum serviço do Azure.</summary>
public sealed class HistoricoNoBanco(CrachaContext contexto) : IHistorico
{
    public string Descricao => "Banco de dados";

    public async Task RegistrarAsync(RegistroHistorico registro, CancellationToken ct = default)
    {
        contexto.Historico.Add(registro);
        await contexto.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RegistroHistorico>> ListarAsync(FiltroHistorico filtro, CancellationToken ct = default)
    {
        var consulta = contexto.Historico.AsNoTracking();
        if (filtro.FuncionarioId is int id)
            consulta = consulta.Where(r => r.FuncionarioId == id);
        if (!string.IsNullOrWhiteSpace(filtro.Departamento))
            consulta = consulta.Where(r => r.Departamento == filtro.Departamento);
        if (filtro.Tipo is TipoAcao tipo)
            consulta = consulta.Where(r => r.TipoAcao == tipo);

        return await consulta.OrderByDescending(r => r.Quando).Take(filtro.Limite).ToListAsync(ct);
    }
}

/// <summary>
/// Histórico numa Azure Table, como pede o desafio: PartitionKey = departamento e
/// RowKey = ticks invertidos + id, para os registros mais novos virem primeiro.
/// </summary>
public sealed partial class HistoricoAzureTable : IHistorico
{
    private readonly TableClient _tabela;
    private readonly Lazy<Task> _criacao;

    public HistoricoAzureTable(OpcoesHistorico opcoes)
    {
        _tabela = new TableServiceClient(opcoes.ConnectionString).GetTableClient(opcoes.Tabela);
        _criacao = new Lazy<Task>(() => _tabela.CreateIfNotExistsAsync());
        Descricao = opcoes.ConnectionString.Contains("UseDevelopmentStorage", StringComparison.OrdinalIgnoreCase)
            || opcoes.ConnectionString.Contains("127.0.0.1")
            ? "Azure Table (Azurite)"
            : "Azure Table";
    }

    public string Descricao { get; }

    public async Task RegistrarAsync(RegistroHistorico registro, CancellationToken ct = default)
    {
        await _criacao.Value;
        await _tabela.UpsertEntityAsync(FuncionarioLog.De(registro), TableUpdateMode.Replace, ct);
    }

    public async Task<IReadOnlyList<RegistroHistorico>> ListarAsync(FiltroHistorico filtro, CancellationToken ct = default)
    {
        await _criacao.Value;

        var condicoes = new List<string>();
        if (filtro.FuncionarioId is int id)
            condicoes.Add(TableClient.CreateQueryFilter($"FuncionarioId eq {id}"));
        if (!string.IsNullOrWhiteSpace(filtro.Departamento))
            condicoes.Add(TableClient.CreateQueryFilter($"PartitionKey eq {Chave(filtro.Departamento)}"));
        if (filtro.Tipo is TipoAcao tipo)
            condicoes.Add(TableClient.CreateQueryFilter($"TipoAcao eq {tipo.ToString()}"));
        var filtroOData = condicoes.Count == 0 ? null : string.Join(" and ", condicoes);

        // Cada partição vem ordenada, mas entre partições não; ordena aqui.
        var registros = new List<RegistroHistorico>();
        await foreach (var entidade in _tabela.QueryAsync<FuncionarioLog>(filtroOData, cancellationToken: ct))
            registros.Add(entidade.ParaRegistro());

        return registros.OrderByDescending(r => r.Quando).Take(filtro.Limite).ToList();
    }

    /// <summary>PartitionKey não aceita / \ # ? nem caracteres de controle.</summary>
    internal static string Chave(string texto) => CaracteresProibidos().Replace(texto.Trim(), "-");

    [GeneratedRegex(@"[/\\#?\u0000-\u001F\u007F-\u009F]")]
    private static partial Regex CaracteresProibidos();

    /// <summary>A entidade gravada na Azure Table (o FuncionarioLog do desafio).</summary>
    public sealed class FuncionarioLog : ITableEntity
    {
        public string PartitionKey { get; set; } = "";
        public string RowKey { get; set; } = "";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Id { get; set; } = "";
        public int FuncionarioId { get; set; }
        public string Nome { get; set; } = "";
        public string Departamento { get; set; } = "";
        public string TipoAcao { get; set; } = "";
        public DateTimeOffset Quando { get; set; }
        public double Salario { get; set; }
        public string JSON { get; set; } = "{}";
        public string AlteracoesJSON { get; set; } = "[]";

        public static FuncionarioLog De(RegistroHistorico r)
        {
            var foto = JsonSerializer.Deserialize<FotoFuncionario>(r.FotoJson, Auditoria.Json);
            return new FuncionarioLog
            {
                PartitionKey = Chave(r.Departamento),
                RowKey = $"{DateTimeOffset.MaxValue.UtcTicks - r.Quando.UtcTicks:D19}_{r.Id}",
                Id = r.Id,
                FuncionarioId = r.FuncionarioId,
                Nome = r.NomeFuncionario,
                Departamento = r.Departamento,
                TipoAcao = r.TipoAcao.ToString(),
                Quando = r.Quando,
                Salario = (double)(foto?.Salario ?? 0),
                JSON = r.FotoJson,
                AlteracoesJSON = JsonSerializer.Serialize(r.Alteracoes),
            };
        }

        public RegistroHistorico ParaRegistro() => new()
        {
            Id = Id,
            FuncionarioId = FuncionarioId,
            NomeFuncionario = Nome,
            Departamento = Departamento,
            TipoAcao = Enum.Parse<TipoAcao>(TipoAcao),
            Quando = Quando,
            FotoJson = JSON,
            Alteracoes = JsonSerializer.Deserialize<List<CampoAlterado>>(AlteracoesJSON) ?? [],
        };
    }
}
