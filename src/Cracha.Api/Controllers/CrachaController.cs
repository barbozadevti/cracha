using Cracha.Api.Dados;
using Cracha.Api.Modelos;
using Cracha.Api.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace Cracha.Api.Controllers;

/// <summary>Foto do funcionário (Blob Storage ou disco) e QR Code do crachá impresso.</summary>
[ApiController]
[Route("api/funcionarios/{id:int}")]
public class CrachaController(CrachaContext contexto, IArmazenamentoFotos fotos, UsuarioAtual usuario) : ControllerBase
{
    /// <summary>Foto do funcionário. A URL traz a versão, então o navegador pode guardar em cache.</summary>
    [HttpGet("foto")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Foto(int id)
    {
        var foto = await fotos.AbrirAsync(id);
        if (foto is not { } f)
            return NotFound();
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return File(f.Conteudo, f.Tipo);
    }

    /// <summary>Envia a foto (JPEG, PNG ou WebP, até 2 MB). Pode o RH ou a própria pessoa.</summary>
    [HttpPut("foto")]
    [RequestSizeLimit(Imagens.TamanhoMaximo + 64 * 1024)]
    [ProducesResponseType<FuncionarioSaida>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> EnviarFoto(int id, IFormFile arquivo)
    {
        var funcionario = await contexto.Funcionarios.FindAsync(id);
        if (funcionario is null)
            return NotFound();
        if (!usuario.VeTudo && usuario.FuncionarioId != id)
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Só o RH ou a própria pessoa podem trocar a foto.");
        if (arquivo.Length is 0 or > Imagens.TamanhoMaximo)
            return Invalido("A foto deve ter até 2 MB.");

        using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria);
        var bytes = memoria.ToArray();
        if (Imagens.Tipo(bytes) is not { } tipo)
            return Invalido("Envie uma imagem JPEG, PNG ou WebP.");

        await fotos.SalvarAsync(id, bytes, tipo);
        funcionario.FotoVersao = Guid.NewGuid().ToString("N")[..12];
        await contexto.SaveChangesAsync();
        return Ok(new { fotoUrl = $"/api/funcionarios/{id}/foto?v={funcionario.FotoVersao}" });
    }

    /// <summary>Remove a foto.</summary>
    [HttpDelete("foto")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoverFoto(int id)
    {
        var funcionario = await contexto.Funcionarios.FindAsync(id);
        if (funcionario is null)
            return NotFound();
        if (!usuario.VeTudo && usuario.FuncionarioId != id)
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Só o RH ou a própria pessoa podem remover a foto.");

        await fotos.RemoverAsync(id);
        funcionario.FotoVersao = null;
        await contexto.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>QR Code (SVG) que abre a ficha da pessoa no Crachá; vai impresso no verso do crachá.</summary>
    [HttpGet("qrcode")]
    [Produces("image/svg+xml")]
    public async Task<IActionResult> QrCode(int id)
    {
        if (!await contexto.Funcionarios.AnyAsync(f => f.Id == id))
            return NotFound();
        var endereco = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/#/funcionarios/{id}";
        using var dados = QRCodeGenerator.GenerateQrCode(endereco, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(dados).GetGraphic(8, "#0f172a", "#ffffff", drawQuietZones: true);
        return Content(svg, "image/svg+xml");
    }

    private BadRequestObjectResult Invalido(string mensagem) =>
        BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["arquivo"] = [mensagem] }) { Status = 400 });
}
