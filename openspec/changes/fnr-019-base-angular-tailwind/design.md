# Design

## Context

O repositório contém `backend/`, documentos e specs, mas não contém `frontend/`, `package.json` ou testes Angular. A FNR-019 depende de FNR-002/003, já registradas como concluídas em seus escopos; a FNR-018 está presente no merge local do PR #35, embora BACKLOG 0.63 e README ainda descrevam estado anterior. Ver `proposal.md` e a delta `specs/web-application-shell/spec.md` para o limite funcional. `docs/DESING.md` é a referência visual disponível; o HTML original não foi localizado no repositório. A baseline de versões está em TDD §7 e AMBIENTE.md; não há autorização para alterá-la implicitamente.

## Goals / Non-Goals

**Goals:**
- Estabelecer um workspace frontend independente do backend, com build e testes repetíveis em Windows e no fluxo futuro de CI.
- Prover um shell visual simples e honesto, testável sem API nem dados reais, que não abra rotas financeiras antes da autenticação da FNR-020.
- Deixar extensão localizada para rotas, cliente HTTP e serviços por funcionalidade sem instalar implementações vazias ou abstrações genéricas.

**Non-Goals:**
- Fazer chamadas reais de login/dados, aplicar guard/interceptor ou configurar CORS/deploy; os contratos da API continuam como estão.
- Recriar integralmente o protótipo, dashboard monetário ou funcionalidades P1; não instalar Chart.js antes de existir um gráfico autorizado.

## Decisions

### D1. Workspace de aplicação única em `frontend/`

Gerar um único app Angular CLI chamado `finora-web` com roteamento, CSS, templates HTML e configuração de build estático; não habilitar SSR/prerender nesta entrega. Fixar as versões aprovadas no manifesto em vez de usar `latest`; gerar o lock a partir das dependências reais e conferir `npm ci`. Preferir uma aplicação única no diretório indicado pelo TDD a um monorepo de vários apps ou um pacote frontend misturado ao backend. Confirmar a combinação CLI/framework/TypeScript/runner pela instalação e build reais; incompatibilidade material exige revisão da baseline, não ajuste silencioso.

### D2. Layout próprio com Tailwind CSS 4 e tokens limitados

Integrar Tailwind v4 ao CSS do Angular pelo plugin `@tailwindcss/postcss` e import global; aplicar tokens para roxo/lavanda, fundo claro, navegação escura, bordas, tipografia e espaçamento de DESING §§2–5/12–13. Criar somente shell, marca, cabeçalho e padrões visuais efetivamente usados; CSS específico fica perto de casos não expressivos por utilitários. Não importar o HTML original ausente, dados fictícios ou componentes inteiros de telas futuras. Preferir componentes próprios pequenos a Angular Material, conforme DT-10; não tentar construir antecipadamente uma biblioteca visual completa.

### D3. Apenas páginas públicas verdadeiras

Rotas `/` e curinga de não encontrado apresentam o conteúdo estático desta entrega. Navegação exibe somente destino existente; não registrar `/login`, `/dashboard`, `/card`, `/transactions`, `/investments` ou `/simulator` enquanto não houver páginas e, quando aplicável, guard/sessão. A página de entrada informa que a aplicação está em preparação, sem estado de conexão ou indicador numérico. Layout com região `main`, navegação identificada, foco visível, textos legíveis e links utilizáveis em mobile/desktop; sem drawer/modal que exija gerenciamento de foco antes de ter caso de uso. Preferir 404 explícito e link de retorno a redirecionar rotas futuras silenciosamente para a página inicial.

### D4. Configuração pública e ponto de integração HTTP, sem consumo

Separar a origem pública da API por ambiente/build, com valor local de desenvolvimento verificável e valor de produção a configurar somente quando houver publicação. Registrar `provideHttpClient` como ponto de composição, sem serviço de negócio, interceptor nem request automática. Configuração frontend não transporta Client Secret, connection string, JWT ou token de demonstração; CORS e autenticação permanecem definidos na API e serão tratados nas FNR-020/027. Preferir configuração pública explícita a incluir segredos no bundle ou a acoplar o shell a uma API disponível; uma configuração de produção ainda não definida não implica publicar a aplicação.

### D5. Vitest pela integração suportada no Angular CLI

Usar o test target do Angular com runner Vitest fixado na baseline e testes de componente/roteamento: página pública sem API, menu sem funcionalidades futuras, 404 com retorno, landmarks/foco e apresentação responsiva conferida em larguras 560/900 px. Executar `npm ci`, build e teste em modo sem watch; registrar limitações quando checagens de layout/teclado exigirem navegador real, sem transformar teste DOM em prova visual. Preferir integração CLI a um runner paralelo independente ou fixture de transações que criaria falsa evidência de produto.

## Risks / Trade-offs

- [Versões aprovadas podem ter incompatibilidade transitiva ou indisponibilidade local] -> Diagnosticar no workspace real; registrar erro e pedir revisão técnica antes de trocar versões.
- [Protótipo original ausente] -> Usar somente `docs/DESING.md` como referência verificável; registrar limite visual, sem alegar fidelidade pixel a pixel.
- [Shell público pode parecer produto financeiro pronto] -> Textos de preparação, nenhum número financeiro, nenhum link inoperante; testar ausência de requests de negócio.
- [Roteamento SPA em hospedagem futura exige fallback de rotas] -> Validar localmente na FNR-019; configurar host/Cloudflare apenas em FNR-027, sem anunciar deep links publicados agora.
- [Backlog e README defasados em relação ao Git] -> Reconciliar registros factuais em revisão documental da execução; não apagar aprovações nem chamar o merge de deploy.

## Migration Plan

Não há migration de banco, API nem alteração de dados. A entrega acrescenta somente `frontend/` e documentação proporcional na feature branch; após aceite, integrar por PR conforme gates aplicáveis. Sem publicação nesta mudança. Em caso de falha antes da integração, corrigir a branch; após integração, um eventual recuo segue novo PR sem afetar banco/API existentes.
