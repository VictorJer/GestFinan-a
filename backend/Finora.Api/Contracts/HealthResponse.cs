namespace Finora.Api.Contracts;

// O contrato de liveness não transporta configurações, versões ou informações financeiras.
public sealed record HealthResponse(string Status);
