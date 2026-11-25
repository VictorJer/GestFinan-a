# Design

## Context

Ver [proposal.md](proposal.md) e as deltas `web-authenticated-session`/`web-application-shell`. A FNR-019 deixou `frontend/src/app` com página inicial e 404, `provideHttpClient()`, `API_BASE_URL` e testes Vitest; não há serviço de sessão, guard ou requests no bootstrap. A API já oferece `POST /api/auth/login` com `accessToken`, `tokenType: Bearer`, `expiresIn: 1800`, `expiresAtUtc`; `POST /api/auth/logout` confirma 204, pode devolver 401/503 e não assegura rollback em commit incerto. O TDD §§3/5 fixa token apenas em memória, 401 distinto de 403 e saída local mesmo em falha remota. O build de produção mantém `apiBaseUrl: null`: isso é indisponibilidade explícita, não permissão para inventar endpoint. O protótipo não tem login; `docs/DESING.md` é somente referência visual.

## Goals / Non-Goals

**Goals:** composição Angular por funcionalidade de autenticação, fronteira HTTP segura, estados de sessão observáveis e testáveis sem banco/Pluggy, login/logout acessíveis e navegação coerente com o que existe.

**Non-Goals:** modificar API, CORS, contratos JWT, Identity ou migrations; simular cartões/movimentos, disparar sync, manter login em reload, adicionar refresh token, ativar origem hospedada ou declarar entrega de FNR-021/022/023/038.

## Decisions

### D1 — Sessão em memória, com validade local e geração

Usar serviço singleton Angular com signals privados para token e expiração UTC, expondo somente estado de sessão/ações; nunca gravar em Web Storage, cookie, URL ou logs. Validar resposta do login: token não vazio, tipo Bearer, expiração UTC parseável futura e duração coerente com `expiresIn` informado; usar expiração do servidor como limite, com verificação em cada leitura/guard/request e temporizador para atualizar a interface mesmo sem tráfego. Limpar temporizador na troca/saída. Cada sessão recebe geração local; respostas antigas de requests ou logout não devem apagar sessão posterior. Alternativas: persistência (contraria DT-28) ou confiar apenas em 401 (mantém acesso visual após prazo). A API conserva a autoridade definitiva e pode revogar antes do relógio local.

### D2 — HTTP somente na API configurada

Registrar `provideHttpClient(withInterceptors([...]))` e injetar `API_BASE_URL`. Normalizar a URL absoluta configurada uma vez, restringindo requisições à mesma origem e caminho-base com separador de segmentos; ignorar URLs similares, subdomínios, outro esquema, porta ou destino não configurado. Só anexar Bearer a caminhos protegidos `/api/...`, excetuando login público (e demais endpoints explicitamente públicos); chamadas públicas e externas passam intocadas. Antes de enviar, exigir sessão ainda válida. Tratar 401/403 somente de chamadas protegidas qualificadas, associadas à geração da sessão que as iniciou; login trata seu próprio 401 no formulário. Alternativas: `Authorization` global por `HttpClient` (risco de vazamento) ou confiar somente em CORS (não restringe cabeçalhos no cliente). Não anexar cookies/`withCredentials`.

### D3 — Fluxo de login e erros

Formulário próprio com rótulos, foco e anúncio textual de erros; limites 256 (e-mail), 1024 (senha) e corpo 16 KiB conforme contrato da API, sem transformar senha. Aparar somente espaços externos do e-mail ao enviar; bloquear envio repetido enquanto aguarda. Mapper de status com textos fixos e sanitizados: 400 entrada, 413 tamanho, 401 credenciais genéricas, 429 excesso de solicitações, 0/rede e 500/503 indisponibilidade; não renderizar `message` nem payload do servidor. Caso `apiBaseUrl` ausente/inválido, bloquear envio com orientação de indisponibilidade; não resolver por origem do frontend. Login válido direciona à rota `/area`, área apenas informativa. Alternativa de reutilizar dashboard do protótipo rejeitada por prometer dados não entregues.

### D4 — Navegação e autorização em camadas

Manter `/` e 404 públicos; adicionar `/login` e `/area` com guard funcional que retorna redirecionamento para `/login` quando sessão ausente/expirada. Layout exibe ações coerentes com o signal: login para visitante, área/logout para titular; sidebar e navegação móvel seguem a mesma regra, sem links ativos de FNR-021–023. Após 401 protegido ou expiração, remover token e levar à página de login com aviso genérico; 403 conserva sessão, informa falta de permissão sem pedir novo login. Guard é somente UX, não substitui autenticação e propriedade na API. Alternativa de ocultar apenas os links é insuficiente para URL direta.

### D5 — Logout e corridas

Enviar logout com o Bearer da geração atual usando a mesma fronteira protegida; limpar a memória após a tentativa, inclusive em 401/503/timeout/rede. Somente 204 comunica revogação confirmada; todo outro resultado informa estado remoto não confirmado (pode haver commit físico sem confirmação). Se 401 durante logout limpar a sessão pelo interceptor, o resultado continua **não confirmado**. Não fazer retries automáticos; bloquear clique duplicado. Referências a geração impedem que um logout atrasado encerre login posterior. Alternativa de limpar antes da chamada sem preservar o token de saída impossibilita revogação remota; alternativa de manter sessão no erro cria falsa sensação de saída. A tela pública exibe aviso seguro após saída; nunca mostra token ou erro bruto.

### D6 — Testes e documentação de gate

Usar Angular TestBed/HTTP testing e `RouterTestingHarness` já disponíveis, com relógio controlado, sem dados pessoais; cobrir login/códigos/validação, reload, expiração passiva, guard e rota direta, prefixo de URL/HTTPS/porta/origens parecidas, interceptor/401/403/rede, logout 204/falha/commit incerto simulado, corrida de sessões, shell responsivo/teclado e ausência de chamadas no bootstrap público. Executar `npm ci`, build dev/prod e Vitest no frontend; validação OpenSpec estrita, diff/links e registro de evidências locais. Corrigir os testes FNR-019 que hoje afirmam intencionalmente ausência de `/login`, formulário e sessão — não preservar assertions obsoletas como se fossem invariantes.

## Risks / Trade-offs

- [Diferença de relógio local] → expiração é conservadora no cliente, API decide validade final; 401 limpa a sessão imediatamente.
- [Resposta antiga após novo login] → geração local e limpeza condicional; testes com requests invertidos.
- [URL de API ausente em produção] → login indisponível com mensagem explícita; FNR-027 configura origem aprovada antes da publicação, sem fallback permissivo.
- [Logout remoto incerto] → limpar local e informar incerteza; não chamar falha de rollback nem confirmar revogação sem 204.
- [Base pública muda de navegação] → atualizar cenários/testes do shell sem ativar funcionalidades futuras; verificar acessibilidade e layout em tela pequena.

## Migration Plan

Sem migration de banco ou alteração de API. Planejamento e revisão/aceite explícito dos quatro artefatos são gate antes de código; reconciliar o fato do PR #37 já integrado no backlog pendente e aprovar a revisão pertinente. Se o detalhamento mudar decisão técnica canônica, alinhar TDD e obter aprovação do diff antes de código dependente. Implementação posterior em `feature/fnr-020-sessao-frontend`, testes locais antes de commits/PR por autorização própria. Rollback de UI por PR/reversão autorizada, sem alterar tokens/sessões persistidos na API; logout remoto confirmado não é reversível por rollback do frontend.
