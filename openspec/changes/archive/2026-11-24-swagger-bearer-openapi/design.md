# Design

## Context

Ver [proposal.md](proposal.md) e [delta `api-exploration`](specs/api-exploration/spec.md). `backend/Finora.Api/Program.cs` usa `Microsoft.AspNetCore.OpenApi` 10.0.15 (`AddOpenApi`, `MapOpenApi().AllowAnonymous()`) e somente `Swashbuckle.AspNetCore.SwaggerUI` 10.2.3 em Development. Já existe um transformer de operação para o PATCH de anotações. `AuthenticationController` marca login com `[AllowAnonymous]` e logout com `[Authorize]`; health usa `[AllowAnonymous]`, os Controllers financeiros usam `[Authorize]`, e `AddFinoraHttpAuthentication` registra uma `FallbackPolicy` autenticada para endpoints não anônimos. A documentação deve refletir também essa fallback sem depender de conexão, configuração JWT válida ou broker no host de exploração. `docs/TDD.md` §§3/5/11 e a spec `api-access-boundary` preservam esses contratos.

## Goals / Non-Goals

**Goals:**
- Gerar esquema Bearer resolvível e requisitos por operação a partir dos mesmos metadados/políticas usados pela API, inclusive a fallback; continuar funcionando no host Development sem segredos configurados.
- Reutilizar a UI e os testes HTTP existentes para comprovar o contrato OpenAPI sem credenciais reais nem serviços externos.

**Non-Goals:**
- Configurar novo esquema de autenticação, modificar a fallback ou criar fluxo de emissão/armazenamento de tokens na UI.
- Substituir o gerador nativo por SwaggerGen, acrescentar pacotes, expor documentação fora de Development ou provar um login/sessão real sem banco.

## Decisions

1. **Esquema no documento, exigência em operações.** Registrar um document transformer no `AddOpenApi` para acrescentar a `components.securitySchemes` o identificador `Bearer` com `type: http`, `scheme: bearer` e `bearerFormat: JWT`, preservando componentes existentes. Registrar também um operation transformer que referencie esse componente, sem requisito `security` global. A geração de segurança por operação evita impor token em login/health; a alternativa de security global com exceções explícitas é mais sujeita a esquecimentos. A documentação oficial do ASP.NET Core 10 distingue transformers de documento e de operação para esse caso ([Customize OpenAPI documents](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-10.0)).

2. **Política efetiva como fonte do mapa de operações.** O transformer usa `ApiDescription.ActionDescriptor.EndpointMetadata`: `[AllowAnonymous]`/`IAllowAnonymous` prevalece; autorização explícita (`IAuthorizeData`) exige Bearer; quando não há anotação de autorização, consultar a política fallback configurada e marcar as operações efetivamente autenticadas por ela. Não codificar uma lista fixa de paths nem supor que todo endpoint sem `[Authorize]` é público. Manter o comportamento de endpoint/controller exatamente como está; testes devem cobrir tanto rotas explícitas quanto uma rota sintética coberta somente pela fallback, caso ela seja publicada no documento de teste. A alternativa de inferir proteção só por `[Authorize]` não cobre a fallback existente; adicionar `[Authorize]` em todas as actions alteraria código de segurança por motivo puramente documental. Verificar na implementação a representação de metadados MVC para actions/classes e `AllowAnonymous` efetivo, mantendo as decisões acima.

3. **UI existente e token informado manualmente.** O Swagger UI atual lê `/openapi/v1.json`; um esquema HTTP bearer com referências por operação habilita Authorize e o envio do cabeçalho apenas onde requerido. Inserir somente o valor do accessToken (sem prefixo); não configurar armazenamento persistente de autorização, valores default, exemplos, injeção de credenciais ou logs de headers. O token inserido manualmente continua sujeito à validação JwtBearer e à consulta de sessão já existentes; a documentação nunca autentica por conta própria.

4. **Validação sem integrações.** Ampliar `ExplorationTests` para inspecionar JSON completo do documento (esquema, `security` por verbo/rota, referências válidas, ausência de requisito global e de token em valores/exemplos) usando `ApiFactory("Development")` sem strings de conexão nem broker. Exercitar login/health sem Bearer, logout/rotas protegidas sem credenciais e indisponibilidade do mecanismo autenticado quando não configurado conforme os testes existentes; não esperar sucesso autenticado nem afirmar integração real. Manter `ExplorationIsolationTests` em Production/Staging. Se houver verificação manual de Authorize/Try it out no navegador, usar apenas valor sintético e não registrar cabeçalhos/valor; o retorno 401/503 sem banco não comprova sessão válida. Não transformar ausência de navegador em teste aprovado.

## Risks / Trade-offs

- [Metadados MVC ou fallback podem não aparecer como esperado no ApiDescription] → Conferir matriz por método no JSON gerado e teste de operação protegida só pela fallback; ajustar a leitura de metadados sem alterar as policies executadas.
- [Referência OpenAPI malformada ou requisito global bloqueia login/health na UI] → Validar `components.securitySchemes`, `$ref`, segurança por operação e ausência de segurança global no documento servido; conferir botão Authorize na UI local.
- [Inserção manual de token pode expor valor via exemplo, log ou persistência no navegador] → Nunca versionar valor de token nem ligar persistência; usar somente sentinelas sintéticas transitórias sem registrá-las, revisar diff e testar ausência de exemplos/segredos no documento.
- [Sem banco não se prova uma chamada autenticada real] → Limitar a evidência à estrutura OpenAPI, UI e regressões HTTP sem integração; registrar explicitamente a limitação.

## Migration Plan

Não há migration, alteração de banco, deploy ou mudança de configuração necessária. Após revisão/aceite da mudança e validações locais em feature branch, um PR futuro pode apresentar código/testes; reversão consiste em remover apenas os transformers de documentação, mantendo JwtBearer e a fallback inalterados. Esta proposta não autoriza commit, push, PR ou publicação.
