using Finora.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

// Mantém o diagnóstico técnico isolado de casos de uso e dependências externas.
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    // Responde apenas pela capacidade de atender HTTP, mesmo sem banco ou provedor configurado.
    [HttpGet]
    [AllowAnonymous]
    [EndpointSummary("Verifica se a API atende requisições")]
    [EndpointDescription("Liveness do processo; não verifica banco de dados nem provedor financeiro.")]
    [Produces("application/json")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get() => Ok(new HealthResponse("ok"));
}
