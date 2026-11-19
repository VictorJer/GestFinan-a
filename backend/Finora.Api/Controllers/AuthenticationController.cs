using System.ComponentModel.DataAnnotations;
using Finora.Api.Authentication;
using Finora.Application.Authentication;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finora.Api.Controllers;

// O Controller transporta o caso de uso; a confirmação transacional pertence à Infrastructure.
[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(ISender sender, TimeProvider clock) : ControllerBase
{
    // Não aplica a política de criação ao login nem transforma a senha, inclusive espaços externos.
    [HttpPost("login")]
    [AllowAnonymous]
    [EndpointSummary("Autentica o titular e confirma uma nova sessão")]
    [ProducesResponseType<LoginResponse>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(413)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new LoginCommand(request.Email!, request.Password!), cancellationToken);
        if (result.State == LoginState.Success)
        {
            var token = result.Token!;
            return Ok(new LoginResponse(token.AccessToken, token.TokenType, token.ExpiresIn, token.ExpiresAtUtc));
        }
        var status = result.State == LoginState.Unavailable ? 503 : 401;
        return new ObjectResult(AuthenticationHttp.Problem(HttpContext, status))
        { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    // A referência vem exclusivamente do principal validado; query/body não podem escolher o alvo.
    [HttpPost("logout")]
    [Authorize]
    [EndpointSummary("Revoga a sessão autenticada atual")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var reference = JwtTokenValidation.ReadSessionClaims(User, clock);
        if (reference is null) { return AuthenticationFailure(401, challenge: true); }

        var result = await sender.Send(new LogoutCommand(reference), cancellationToken);
        return result.State switch
        {
            LogoutState.Confirmed => NoContent(),
            LogoutState.InvalidSession => AuthenticationFailure(401, challenge: true),
            LogoutState.Unavailable => AuthenticationFailure(503, challenge: false),
            _ => AuthenticationFailure(500, challenge: false)
        };
    }

    // Mantém logout sem detalhes do provider e diferencia token inválido de indisponibilidade operacional.
    private IActionResult AuthenticationFailure(int status, bool challenge)
    {
        if (challenge) { Response.Headers.WWWAuthenticate = "Bearer"; }
        return new ObjectResult(AuthenticationHttp.Problem(HttpContext, status))
        { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}

// DTO de entrada com limites próprios de login; texto incidental não revela os campos.
public sealed class LoginRequest
{
    [Required, MaxLength(256)] public string? Email { get; init; }
    [Required, MaxLength(1024)] public string? Password { get; init; }
    public override string ToString() => "LoginRequest { [redacted] }";
}

// Somente o corpo de sucesso publica o token; a representação textual do DTO continua redigida.
public sealed class LoginResponse(string accessToken, string tokenType, int expiresIn, DateTimeOffset expiresAtUtc)
{
    public string AccessToken { get; } = accessToken;
    public string TokenType { get; } = tokenType;
    public int ExpiresIn { get; } = expiresIn;
    public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
    public override string ToString() => "LoginResponse { AccessToken = [redacted] }";
}
