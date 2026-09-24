using System.Text.Json;
using System.Text.Json.Serialization;
using Cracha.Api.Modelos;

namespace Cracha.Api.Dados;

/// <summary>Monta os registros do histórico: foto do funcionário + campos que mudaram.</summary>
public static class Auditoria
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static RegistroHistorico Registro(TipoAcao tipo, FotoFuncionario? antes, FotoFuncionario depois, DateTimeOffset quando) => new()
    {
        FuncionarioId = depois.Id,
        NomeFuncionario = depois.Nome,
        Departamento = depois.Departamento,
        TipoAcao = tipo,
        Quando = quando,
        FotoJson = JsonSerializer.Serialize(depois, Json),
        // Na remoção, a lista de campos não interessa: a foto guarda como o funcionário estava.
        Alteracoes = tipo == TipoAcao.Remocao ? [] : FotoFuncionario.Comparar(antes, depois),
    };
}
