using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Cracha.Api.Dados;

public enum ProvedorFotos { Disco, Blob }

public sealed class OpcoesFotos
{
    public ProvedorFotos Provedor { get; set; } = ProvedorFotos.Disco;

    /// <summary>Connection string da Storage Account. "UseDevelopmentStorage=true" usa o Azurite.</summary>
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";

    public string Container { get; set; } = "fotos";

    /// <summary>Pasta do provedor Disco; vazia usa App_Data/fotos.</summary>
    public string? Pasta { get; set; }
}

/// <summary>Onde ficam as fotos dos funcionários: Azure Blob Storage ou uma pasta local.</summary>
public interface IArmazenamentoFotos
{
    string Descricao { get; }
    Task SalvarAsync(int funcionarioId, byte[] conteudo, string tipo, CancellationToken ct = default);
    Task<(byte[] Conteudo, string Tipo)?> AbrirAsync(int funcionarioId, CancellationToken ct = default);
    Task RemoverAsync(int funcionarioId, CancellationToken ct = default);
}

public static class Imagens
{
    public const int TamanhoMaximo = 2 * 1024 * 1024;

    /// <summary>Descobre o tipo pelos primeiros bytes (a extensão e o Content-Type enviados não são confiáveis).</summary>
    public static string? Tipo(ReadOnlySpan<byte> b) => b switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };
}

/// <summary>Fotos num container privado do Blob Storage (blob "funcionarios/{id}").</summary>
public sealed class FotosNoBlob : IArmazenamentoFotos
{
    private readonly BlobContainerClient _container;
    private readonly Lazy<Task> _criacao;

    public FotosNoBlob(OpcoesFotos opcoes)
    {
        _container = new BlobServiceClient(opcoes.ConnectionString).GetBlobContainerClient(opcoes.Container);
        _criacao = new Lazy<Task>(() => _container.CreateIfNotExistsAsync(PublicAccessType.None));
        Descricao = opcoes.ConnectionString.Contains("UseDevelopmentStorage", StringComparison.OrdinalIgnoreCase)
            || opcoes.ConnectionString.Contains("127.0.0.1")
            ? "Azure Blob (Azurite)"
            : "Azure Blob Storage";
    }

    public string Descricao { get; }

    private BlobClient Blob(int id) => _container.GetBlobClient($"funcionarios/{id}");

    public async Task SalvarAsync(int funcionarioId, byte[] conteudo, string tipo, CancellationToken ct = default)
    {
        await _criacao.Value;
        await Blob(funcionarioId).UploadAsync(new BinaryData(conteudo),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = tipo } }, ct);
    }

    public async Task<(byte[] Conteudo, string Tipo)?> AbrirAsync(int funcionarioId, CancellationToken ct = default)
    {
        await _criacao.Value;
        var blob = Blob(funcionarioId);
        if (!await blob.ExistsAsync(ct))
            return null;
        var resposta = await blob.DownloadContentAsync(ct);
        return (resposta.Value.Content.ToArray(), resposta.Value.Details.ContentType ?? "application/octet-stream");
    }

    public async Task RemoverAsync(int funcionarioId, CancellationToken ct = default)
    {
        await _criacao.Value;
        await Blob(funcionarioId).DeleteIfExistsAsync(cancellationToken: ct);
    }
}

/// <summary>Fotos numa pasta local (App_Data/fotos), para rodar sem nenhum serviço do Azure.</summary>
public sealed class FotosEmDisco(string pasta) : IArmazenamentoFotos
{
    private static readonly Dictionary<string, string> Extensoes = new()
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp",
    };

    public string Descricao => "Pasta local";

    private IEnumerable<string> Arquivos(int id) =>
        Directory.Exists(pasta) ? Directory.EnumerateFiles(pasta, $"{id}.*") : [];

    public async Task SalvarAsync(int funcionarioId, byte[] conteudo, string tipo, CancellationToken ct = default)
    {
        Directory.CreateDirectory(pasta);
        await RemoverAsync(funcionarioId, ct);
        await File.WriteAllBytesAsync(Path.Combine(pasta, $"{funcionarioId}{Extensoes[tipo]}"), conteudo, ct);
    }

    public async Task<(byte[] Conteudo, string Tipo)?> AbrirAsync(int funcionarioId, CancellationToken ct = default)
    {
        var arquivo = Arquivos(funcionarioId).FirstOrDefault();
        if (arquivo is null)
            return null;
        var tipo = Extensoes.First(e => e.Value == Path.GetExtension(arquivo)).Key;
        return (await File.ReadAllBytesAsync(arquivo, ct), tipo);
    }

    public Task RemoverAsync(int funcionarioId, CancellationToken ct = default)
    {
        foreach (var arquivo in Arquivos(funcionarioId).ToList())
            File.Delete(arquivo);
        return Task.CompletedTask;
    }
}
