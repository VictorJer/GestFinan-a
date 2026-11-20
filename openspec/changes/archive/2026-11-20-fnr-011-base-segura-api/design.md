# Design

## Context

Ver [proposal.md](proposal.md). Hoje `backend/Finora.Api/Program.cs` registra Controllers, autenticação e `AuthenticationBoundary`, mas não configura CORS, política geral de autorização, limitação de requisições ou tratamento uniforme dos erros de toda a API. `AuthenticationHttp` e `AuthenticationServiceCollectionExtensions` já tratam falhas de login/logout e challenge de sessão; `HealthController` e OpenAPI/Swagger em Development possuem contratos consolidados. `AuthenticationTestHost` demonstra rotas protegidas somente no assembly de testes; não existem endpoints financeiros de produção. Consultar [api-access-boundary](specs/api-access-boundary/spec.md) e [authenticated-sessions](specs/authenticated-sessions/spec.md) para comportamento proposto.

## Goals / Non-Goals

**Goals:**
- Compor a borda HTTP de forma que rotas novas não esqueçam autenticação; compartilhar formato de erros sem mudar os contratos de autenticação já aceitos.
- Disponibilizar políticas testáveis de CORS e limitação de tentativas, com parâmetros seguros e inspeção de regressões de privacidade.
- Deixar uma política de compatibilidade explícita no TDD antes de contratos de dados futuros, sem forçar migração de cliente nesta entrega.

**Non-Goals:**
- Criar rotas/DTOs financeiros, buscar dados reais, implementar guards Angular, autenticação entre instâncias ou novas tabelas/migrations.
- Confundir CORS com autenticação, garantir limite distribuído em múltiplos hosts, restringir endpoints CLI ou introduzir cadastro/reset HTTP.

## Decisions

### D1 — Política padrão fechada e exceções expressas

Adotar fallback de autorização no pipeline HTTP para endpoints MVC/rotas mapeadas, com `[AllowAnonymous]` somente no login, health e documentação mapeada em Development. Logout continua `[Authorize]`. Adaptar a identificação de rota protegida usada por `AuthenticationBoundary`/JwtBearer à política fallback, para que os comportamentos de no-store e falha fechada não dependam exclusivamente de `[Authorize]`; rotas inexistentes continuam 404. Identidade confiável vem do principal validado e sessão consultada por chamada; handlers de dados futuros recebem somente o identificador obtido dessa fonte, e repositórios aplicam predicado de propriedade. Usar Controller/probe exclusivos dos testes para comprovar acesso cruzado, 403 e 404, sem expor endpoint fictício em produção.

Alternativa rejeitada: depender só de `[Authorize]` em cada Controller futuro, que deixa rotas esquecidas públicas. Também não fazer check de propriedade somente no guard/frontend: isso permite consulta direta à API.

### D2 — CORS explícito, antes da autenticação

Configurar origens exatas por ambiente (lista vazia por padrão; sem `*` e sem credenciais/cookies), permitir somente métodos/cabeçalhos necessários ao cliente (`GET`, `POST`, `PATCH`, `Authorization`, `Content-Type`, conforme contrato real) e colocar CORS após roteamento e antes da autenticação/limitação. Verificar preflight autorizado/não autorizado e respostas de erro com Origin; `Origin` ausente não substitui Bearer. Rejeitar configuração malformada de origens sem conceder permissão ampla e documentar valores de exemplo sem domínio pessoal.

Alternativa rejeitada: `AllowAnyOrigin` ou refletir o cabeçalho Origin, que amplia o acesso entre sites; fixar uma URL de hospedagem antes do ambiente existir também seria frágil.

### D3 — Fonte única de erro e preservação da autenticação

Na Api, centralizar geração de Problem Details com títulos/códigos fixos e traceId não sensível para falhas tratadas de MVC, challenge/forbid, limitação e exceções antes da resposta iniciada. Integrar a fábrica `InvalidModelStateResponseFactory` e `AuthenticationHttp` sem reintroduzir valores de `ModelState`, e manter o tratamento restrito ao corpo de login (16 KiB, `no-store`, limpeza do buffer). Usar a borda de exceção para 500 inesperado e mapear 503 somente quando dependência indisponível for identificada com segurança; conflitos de negócio 409 e não encontrado/inacessível 404 vêm de resultados tipados dos futuros casos de uso, nunca de mensagem bruta. Erros 401 mantêm challenge quando cabível; 503 não é senha incorreta. Resposta já iniciada não pode ser reescrita; abortar/registrar categoria fixa sem vazar exceção.

Alternativa rejeitada: deixar cada Controller emitir seu próprio erro ou confiar nos detalhes padrão em Development; ambas permitem formatos divergentes ou exposição acidental.

### D4 — Limitação local parametrizada, distinta do lockout

Usar rate limiter nativo da API com política de login particionada pela origem de rede observada pelo servidor (sem confiar em `X-Forwarded-For` não validado) e política separada para requisições autenticadas particionada pelo titular validado. Defaults propostos para revisão: 10 logins/minuto por origem por instância e 120 chamadas protegidas/minuto por titular por instância, janela fixa, sem fila; parâmetros configuráveis com validação de inteiros positivos, sem desativação silenciosa. O limitador ocorre depois do roteamento/CORS e, para rota protegida, depois da autenticação; login é anônimo, então uma rejeição 429 não lê corpo, não avalia senha nem altera contador do Identity. Requisições sem sessão seguem o fluxo 401/503 da autenticação; o lockout de cinco falhas/cinco minutos permanece independente. Health, preflight e comandos CLI ficam fora da política. No proxy de produção, só aceitar cabeçalhos de encaminhamento de proxies explicitamente confiáveis; sem essa configuração, usar conexão direta e documentar possível compartilhamento da partição.

Alternativas rejeitadas: limitar só pelo e-mail fornecido (permite evasão e enumeração), armazenar contadores em banco de sessões (complexidade e dependência adicional nesta entrega), ou alegar limite global com memória por instância. Os dois valores são propostas novas e exigem aprovação no SDD/TDD antes de código dependente.

### D5 — Compatibilidade e versão

Conservar rotas existentes sem prefixo de versão e formatos de sucesso; tratar novas propriedades opcionais como aditivas somente quando não quebram consumidores. Alteração incompatível futura deverá introduzir rota/contrato explicitamente versionado e período de convivência definido no SDD da entrega afetada (FNR-015/026), nunca reutilizar `/openapi/v1.json` como prova de versão de rota. Registrar no TDD esta política proposta antes de estabilizar os contratos; não alterar rotas nesta FNR.

Alternativa rejeitada: renomear login/logout para `/api/v1/...` agora, por ser mudança de cliente fora do escopo e sem frontend entregue.

## Risks / Trade-offs

- [Rate limiter por instância e NAT/proxy compartilhado] → declarar limite local, configurar proxy confiável e testar origens distintas sem cabeçalhos forjados; proteção distribuída exigiria mudança própria.
- [Fallback altera diagnóstico/documentação ou probes de teste] → testar Development, Staging e Production com/sem JWT e sem banco; exceções anônimas explícitas, sem criar rota financeira real.
- [Ordem de middlewares muda 401/503 ou CORS em falhas] → matriz HTTP para login, logout, preflight, sessão revogada, erro de validação e exceção, incluindo status, headers, tipo e corpo.
- [Defaults do limitador afetam testes e cliente legítimo] → escolher/aceitar limites antes da implementação, parametrizar fixtures/relógio e exercitar fronteira sem sleeps; observar 429 sem prometer `Retry-After` sem semântica comprovada.
- [Formato uniforme poderia quebrar o login aprovado] → comparar byte/semântica dos casos existentes com a spec consolidada e restringir mudanças à nova possibilidade 429.

## Migration Plan

1. Revisar e aprovar proposta/specs/design/tarefas e registrar no TDD/backlog a política nova e os defaults aceitos antes do código dependente. O PR #23 já está integrado; reconciliar apenas o registro de estado sem apagar o texto histórico.
2. Em branch de feature, integrar a borda HTTP e executar testes de segurança/regressão em hosts isolados; não há migration nem seed. Configurar apenas origens reais aprovadas por ambiente ao publicar a aplicação, fora deste escopo.
3. Publicar somente por PR e checks aplicáveis. Em caso de regressão, reverter a entrega pela integração normal; enquanto não houver origens configuradas, o CORS permanece fechado, e retirar temporariamente rate limiting não deve retirar autenticação/propriedade.
