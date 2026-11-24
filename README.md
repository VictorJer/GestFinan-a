# Finora

**Gestão financeira pessoal com foco nas transações do cartão de crédito.**

Projeto desenvolvido como proposta de Trabalho de Conclusão de Curso (TCC), com desenvolvimento individual. O Finora pretende reunir dados do cartão Nubank em um dashboard que permita consultar compras, organizar categorias e compreender os gastos por período.

Como complementos, o projeto prevê uma carteira de investimentos com cadastro manual e um simulador de evolução patrimonial com aportes.

> **Status:** ideia aprovada pelo professor e protótipo navegável disponível. FNR-001/002/003 concluídas nos respectivos escopos conforme o backlog. Base backend FNR-003 validada localmente, revisada, aceita pelo autor e integrada pelo [PR #8](https://github.com/VictorJer/GestFinan-a/pull/8), merge `2bd63ae`. FNR-004 foi aceita e integrada pelo [PR #10](https://github.com/VictorJer/GestFinan-a/pull/10), com prova real limitada a uma página (321 transações). FNR-005 está em validação parcial: incremento técnico e três coletas reais limitadas a uma página cada; a matriz não reconhece compras e a competência permanece semânticamente não confirmada, portanto o gate financeiro está bloqueado. Ainda não há importação persistente, dashboard ou publicação desta funcionalidade; a tarefa 5.3 e o aceite final continuam pendentes.

## Objetivo

Desenvolver uma aplicação web que consulte dados de cartão de crédito por meio de um provedor de integração bancária, preserve o histórico e transforme as transações em informações úteis para o controle financeiro pessoal.

**Pergunta orientadora:** como uma aplicação web pode apoiar o controle financeiro pessoal ao reunir dados de cartão de crédito, indicadores de gastos e recursos de planejamento em uma única interface?

## Situação atual

| Entrega | Situação |
|---|---|
| Aprovação da ideia | Concluída, conforme confirmação do autor |
| Protótipo com dados fictícios | Disponível |
| PRD v0 | Revisão 0.2 aprovada documentalmente |
| TDD v0 | Revisões 0.5 e 0.6 aprovadas documentalmente; 0.6 registra DT-49/MediatR 12.5.0 |
| Base backend FNR-003 | Restore com --locked-mode/build e 20 testes aprovados localmente; Swagger verificado no navegador; revisão e aceite explícitos; PR #8 integrado. Sem reviews formais/checks reportados no GitHub; evidências e limites no backlog e guia. |
| Conexão no painel do provedor | Cartão e transações visualizados pelo autor |
| Consulta local pelo backend .NET (FNR-004) | Concluída no escopo limitado, com prova real de uma página; não comprova importação completa |
| Contrato e normalização Pluggy (FNR-005) | Em validação; análise limitada documentada; regra positiva de compra e competência bloqueadas por evidência insuficiente |
| Login JWT e sessões persistentes (FNR-009) | Implementação aceita pelo titular e [PR #20](https://github.com/VictorJer/GestFinan-a/pull/20) integrado; 225 testes sem PostgreSQL e 64 no PostgreSQL descartável aprovados localmente. Revogação completa FNR-010, FNR-011/020 e publicação permanecem pendentes; publicação autenticada bloqueada até FNR-010 e demais gates. |
| Aplicação integrada e publicação definitiva | Pendentes |

A visualização no painel da Pluggy é evidência preliminar de acesso aos dados. A leitura local pela API foi demonstrada em FNR-004; FNR-005 observou campos e agregados em três coletas autorizadas de uma página cada, mas não valida histórico completo, regra positiva de compra, competência por instituição ou comportamento de sincronização.

A prova de conceito local de consulta real foi concluída em FNR-004 sem exposição pública. Validar o contrato financeiro conservador em FNR-005 ainda é um gate anterior à persistência, dashboard e aplicação hospedada.

## Documentação e fluxo de trabalho

- [PRD](docs/PRD.md): requisitos e critérios de aceite do produto.
- [TDD](docs/TDD.md): decisões técnicas, propostas e validações pendentes.
- [Backlog](docs/BACKLOG.md): ordem de execução e evidências por tarefa.
- [Ambiente de desenvolvimento](docs/AMBIENTE.md): versões fixadas, inventário e gates de validação por mudança.
- [FNR-001 no OpenSpec](openspec/changes/archive/2026-11-13-fnr-001-alinhar-documentacao/proposal.md) e [desenho/SDD](openspec/changes/archive/2026-11-13-fnr-001-alinhar-documentacao/design.md): aprovados e concluídos no escopo documental; mudança arquivada em 13/11/2026 por solicitação do autor, com 11/11 tarefas concluídas e sem delta specs. Evidências e limites da verificação preservados nas tarefas e no backlog; FNR-002 não iniciada.

Uma mudança por vez: backlog → feature branch → especificação OpenSpec e revisão → implementação → testes → PR → integração na `main` → deploy após checks. Não desenvolver nem enviar alterações diretamente à `main`; artefatos prontos não significam aprovação.

## Executar o backend local

Requer SDK .NET `10.0.402`. Da raiz do repositório:

```powershell
# Restaura somente o grafo aceito nos locks versionados.
dotnet restore backend/Finora.slnx --locked-mode
# Compila a solução sem novo restore.
dotnet build backend/Finora.slnx --no-restore
# Executa a suíte sem PostgreSQL pelo runner MTP selecionado em global.json.
dotnet test --solution backend/Finora.slnx --no-build --filter 'FullyQualifiedName!~PostgreSql'
# Inicia em Development e loopback, sem banco ou segredos.
dotnet run --project backend/Finora.Api --launch-profile http --no-build --no-restore
```

Acesse `http://127.0.0.1:5080/health`: HTTP 200 e `{"status":"ok"}`. É apenas **liveness**: não valida banco, Pluggy, autenticação ou dados reais. A API também possui rotas protegidas de conexão/sincronização/status e, nesta branch, `GET /api/cards` e `GET /api/transactions`.

Para testar pelo navegador, abra `http://127.0.0.1:5080/swagger`, expanda `GET /health`, clique em **Try it out** e **Execute**. Confira código 200 e corpo `{"status":"ok"}`. O documento fica em `/openapi/v1.json`; documento, UI e assets locais estão disponíveis **somente em Development** (404 em Production/Staging). O OpenAPI descreve as rotas implementadas, incluindo consultas de cartões e movimentos; não há cadastro público. Encerre com `Ctrl+C`.

### Consulta de cartões persistidos (FNR-015, estado local)

`GET /api/cards` exige Bearer válido e sessão ativa. Com uma conexão própria única, responde `200` com uma lista JSON de cartões persistidos; cada item contém somente `id` interno, `name`, `lastFour` e `currencyCode`. Nome e final permanecem `null` quando não foram fornecidos na origem. A lista `[]` significa apenas que nenhum cartão está persistido para aquela conexão; não afirma ausência de cartões no provedor. A implementação local desta branch ainda não foi aceita ou integrada. O contrato normativo está em [TDD §11](docs/TDD.md).

Exemplo inteiramente sintético, inclusive os identificadores e dados de apresentação:

```json
[
  {
    "id": "c6d392d9-ff26-4e26-8c39-5e44620a7409",
    "name": "Cartão principal",
    "lastFour": "1234",
    "currencyCode": "BRL"
  },
  {
    "id": "4f930625-d9cb-4777-9871-287f50be3b21",
    "name": null,
    "lastFour": null,
    "currencyCode": "USD"
  }
]
```

Falhas usam `application/problem+json` sanitizado. `401` inclui challenge Bearer; `404 RESOURCE_NOT_FOUND` cobre ausência de conexão própria; `409 RESOURCE_CONFLICT` indica mais de uma conexão própria, sem seleção arbitrária; `429 RATE_LIMITED` aplica o limitador já configurado. A consulta não inicia sincronização e não chama broker ou provedor. Ausência/ambiguidade e os demais erros HTTP estão detalhados em TDD §11 e no OpenAPI.

### Consulta paginada de movimentos (FNR-015, estado local)

`GET /api/transactions` exige Bearer válido e sessão ativa. A API deriva o titular da sessão; não aceita `UserId` do cliente. Os parâmetros opcionais combinam-se por interseção:

| Parâmetro | Contrato |
|---|---|
| `cardId` | ID interno de cartão próprio. ID ausente ou de outro titular retorna o mesmo `404 RESOURCE_NOT_FOUND`. |
| `categoryId` | ID interno de categoria vinculada a anotação própria; categoria inexistente/alheia não revela existência e não seleciona movimentos. |
| `situation` | `Effective`, `Pending`, `FutureInstallment` ou `Unknown`; situação não substitui natureza. |
| `from` | Data `yyyy-MM-dd` inclusiva da `TransactionDate` técnica; pode ser usada sem `toExclusive`. |
| `toExclusive` | Data `yyyy-MM-dd` exclusiva da `TransactionDate` técnica; pode ser usada sem `from`. |
| `search` | Substring case-insensitive literal da descrição; `%`, `_` e `\\` não são curingas; máximo de 200 caracteres. |
| `page` | Página positiva; padrão `1`. |
| `pageSize` | De `1` a `100`; padrão `25`. |

O intervalo temporal usa somente `TransactionDate`, com início inclusivo e fim exclusivo; não representa competência nem ciclo de fatura. Sem filtro de data, movimentos cuja data técnica seja nula continuam visíveis, com `originalDate` preservada e `transactionDate`/motivo apresentados sem inventar data. A ordenação é por `transactionDate` decrescente, nulos por último e ID interno decrescente como desempate. `filteredCount` conta todo o conjunto filtrado antes de paginar e não é quantidade monetária nem gasto. `coverage` é separado dessa contagem: `lastSuccessfulImportAtUtc` mostra o último sucesso persistido, `lastSuccessfulWindow` só existe quando uma `SyncRun` de sucesso da mesma conexão termina exatamente naquele instante, e `latestExecution` mostra a execução mais recente independentemente do sucesso anterior. Códigos de falha são allowlisted; estado e cobertura são strings estáveis.

Exemplo inteiramente sintético de uma página com data técnica indeterminável:

```json
{
  "items": [
    {
      "id": "5b76333c-6d8e-4d9e-aac6-3a6096f97ab1",
      "cardId": "2a990192-cb59-43eb-8520-cfef6a0e3e40",
      "description": "movimento sintético não classificado",
      "amount": 17.25,
      "currencyCode": "BRL",
      "originalDate": "2026-99-99T00:00:00Z",
      "transactionDate": null,
      "dateReason": "INVALID_DATE",
      "nature": "Unknown",
      "natureReason": "UNKNOWN_OPERATION_TYPE",
      "situation": "Effective",
      "situationReason": null,
      "installmentNumber": null,
      "totalInstallments": null,
      "categoryId": null,
      "note": null
    }
  ],
  "page": 1,
  "pageSize": 25,
  "filteredCount": 1,
  "coverage": {
    "lastSuccessfulImportAtUtc": null,
    "lastSuccessfulWindow": null,
    "latestExecution": null
  }
}
```

Os valores são individuais por movimento; natureza, situação, cobertura e motivos são strings allowlisted, não enums numéricos. `CompleteForRequestedWindow` significa conclusão somente daquela janela solicitada, não histórico bancário integral ou classificação completa. Sucesso legado sem `SyncRun` correlata mantém a janela ausente; falha ou execução pendente posterior não apaga o sucesso anterior. Não há soma, total por moeda, média ou qualquer agregado monetário; `filteredCount: 0` não significa gasto zero. Falhas usam `application/problem+json` sanitizado: `400 REQUEST_INVALID_INPUT`, `401 AUTH_INVALID_CREDENTIALS` com challenge Bearer, `404 RESOURCE_NOT_FOUND`, `409 RESOURCE_CONFLICT` para conexão própria ambígua, `429 RATE_LIMITED`, `503 DEPENDENCY_UNAVAILABLE` somente para indisponibilidade tipada de persistência e `500 INTERNAL_ERROR` inesperado. A leitura consulta somente o histórico persistido e não inicia sincronização, broker ou provedor. Esta implementação local da branch ainda não foi aceita ou integrada; o contrato normativo está em [TDD §11](docs/TDD.md), e o OpenAPI expõe parâmetros, descrições e respostas.

As consultas `GET` acima leem somente o histórico persistido no PostgreSQL: filtros e troca de página não acionam Pluggy nem broker, não criam `SyncRun` e continuam servindo os dados já gravados quando o broker está indisponível (se API e banco estiverem disponíveis). A atualização é separada e explícita: `POST /api/connections/current/sync` solicita execução manual; `POST /api/connections/current/sync/on-entry` aplica a política de atualização ao entrar. Uma resposta `202` confirma a aceitação persistente da execução/outbox, não que a coleta terminou. `coverage` descreve o último sucesso e a janela correlata comprovada; mesmo `CompleteForRequestedWindow` vale só para aquela janela, não para todo o histórico do provedor. Assim, a leitura continua disponível durante indisponibilidade externa, mas não promete cobertura nova ou integral.

### Login e sessões locais (FNR-009)

`POST /api/auth/login` recebe JSON com `email` e `password`: até 256/1024 caracteres, corpo de até 16 KiB. Identity verifica a senha exata, normaliza somente o e-mail e bloqueia novos logins por cinco minutos após cinco falhas. Sucesso confirma uma nova `AuthSessions` antes de entregar JWT HS256 de 30 minutos, sem cookie/refresh; o token completo não é persistido. Toda resposta de login usa no-store. Conta desconhecida/senha incorreta/bloqueio retornam 401 genérico; falha operacional retorna 503, não senha incorreta. A sessão é consultada em cada chamada protegida; a rota de prova existe somente no projeto de testes.

Configure externamente `ConnectionStrings__Finora`, `Authentication__Jwt__Issuer`, `Authentication__Jwt__Audience` e `Authentication__Jwt__SigningKeyBase64`, sem argumentos ou arquivos versionados. A chave codifica pelo menos 32 bytes aleatórios e precisa ser estável por ambiente/reinício; rotação coordenada invalida tokens anteriores. Sem configuração válida, login/proteção retornam 503 e health continua independente. A migration `20261118173427_AddAuthenticationSessions` precisa ser aplicada explicitamente pelo processo do [guia de ambiente](docs/AMBIENTE.md); nunca é executada no startup.

**Não publicar autenticação antes de FNR-010/011 e demais gates.** A afirmação de que logout/reset revogatório não estavam entregues refere-se ao escopo histórico da FNR-009; atualizar security stamp ou ler uma revogação SQL de fixture não substituía essa entrega. O estado local posterior da FNR-010 está descrito abaixo, sem converter a evidência histórica em prova retroativa. Contrato, configuração, erros, chaves e evidências estão em [AMBIENTE.md](docs/AMBIENTE.md). Para a suíte PostgreSQL, primeiro preparar somente o Compose descartável e a variável dedicada `FINORA_TEST_CONNECTION_STRING` conforme a barreira documentada; depois executar:

```powershell
# A fixture recusa banco pessoal/Neon e aplica migrations somente no database descartável allowlisted.
dotnet test --solution backend/Finora.slnx --no-build --filter 'FullyQualifiedName~PostgreSql'
```

### Logout da sessão atual (FNR-010, implementação local)

Na implementação local da FNR-010, `POST /api/auth/logout` exige Bearer e não recebe corpo nem seletor de titular/sessão. Query ou corpo enviados pelo cliente não escolhem o alvo: somente a sessão das claims autenticadas pode ser revogada. A resposta 204 é vazia e só é emitida depois da confirmação do commit; não há cookie. Todas as respostas usam `Cache-Control: no-store`.

| Resultado | Significado para o cliente |
|---|---|
| `204` | Revogação da sessão autenticada confirmada pelo servidor. Novas autenticações com esse token são recusadas. |
| `401` + challenge Bearer | Bearer ausente ou sessão/token inválido, expirado, revogado, inexistente ou incompatível; a mutação de logout não foi executada. |
| `503` sem challenge | Configuração, banco/schema ou timeout indisponível; também pode representar confirmação de commit perdida. Não significa que a sessão permaneceu ativa nem confirma logout remoto. |
| `500` sanitizado | Falha inesperada; não confirma logout remoto e não expõe exceção interna. |

Se a conexão falhar ou for cancelada, a interface futura FNR-020 não deve mostrar logout remoto como confirmado: deve limpar o token local da memória e informar que não foi possível confirmar o encerramento no servidor. Em especial, após commit incerto a sessão pode já estar revogada; o servidor não repete a mutação automaticamente e o cliente não deve presumir rollback. Essa limpeza/mensagem da interface não é implementada nesta mudança.

A revogação afeta autenticações iniciadas depois da confirmação, inclusive em outro host que use o mesmo banco/chave. Não cancela retroativamente uma requisição cuja autenticação já terminou. Esta seção registra comportamento e testes locais da feature branch, ainda sem aceite final/integração/publicação; o gate de publicação autenticada e as dependências FNR-011/020 permanecem.

### Consulta local Pluggy (em validação)

O comando explícito `pluggy-read` executa sem iniciar listener HTTP. Para executá-lo após configurar os segredos localmente, use:

```powershell
dotnet run --project backend/Finora.Api --no-build --no-restore --no-launch-profile -- pluggy-read
```

Configure `Pluggy:ClientId`, `Pluggy:ClientSecret` e `Pluggy:ItemId` em User Secrets do projeto `backend/Finora.Api` ou em variáveis de ambiente protegidas; `Pluggy:AccountId` é opcional e serve para selecionar um cartão quando houver mais de um. Não passe credenciais ou IDs como argumentos do comando, não os cole em logs/saídas e não os adicione a arquivos versionados. As chaves de configuração são descritas em [AMBIENTE.md](docs/AMBIENTE.md#início-e-aprovação-do-sdd--fnr-004--15112026) e os segredos de desenvolvimento devem permanecer fora do repositório.

Para analisar a cobertura disponível, sem iniciar listener HTTP, usar explicitamente `pluggy-analyze` com a mesma configuração protegida:

```powershell
dotnet run --project backend/Finora.Api --no-build --no-restore --no-launch-profile -- pluggy-analyze
```

A análise consulta desde a data de calendário de 12 meses antes da referência UTC, não envia `dateTo` para preservar registros futuros e percorre no máximo 100 páginas, com 30 segundos por chamada e 120 segundos totais, sem retry automático. A saída contém somente contagens agregadas, duração, meses distintos com registros e disponibilidade de campos; não comprova 12 meses de histórico e não valida regras financeiras sem evidência. Consulte [PLUGGY-CONTRACT.md](docs/PLUGGY-CONTRACT.md) para filtros, limites e limitações. O código 0 indica apenas coleta terminada com registros; amostra vazia retorna 3, falhas operacionais/incompletas 1, configuração/argumentos 2, conexão bloqueada 4 e cancelamento 130.

O comando consulta a conexão, seleciona uma conta de cartão e lê **somente a primeira página** de `/v2/transactions`. A saída resume etapa, quantidade e existência de continuação; cursor e dados de compra não são exibidos. Uma resposta vazia é inconclusiva, e uma página com transações não significa importação completa, persistência no Finora ou atualização forçada na instituição. Cada chamada tem timeout de 30 segundos; o fluxo inteiro, 120 segundos; não há retries. Códigos atuais: `0` leitura com lançamentos; `1` falha operacional/timeout; `2` configuração/argumentos inválidos ou conta não selecionável; `3` leitura sem lançamentos (inconclusiva); `4` conexão bloqueada; `130` cancelamento. Logs padrão do `HttpClientFactory` estão desativados para o cliente Pluggy. A prova limitada da FNR-004 e a evidência agregada da FNR-005 estão registradas em `docs/AMBIENTE.md`; a segunda não confirmou regra positiva de compra nem competência. A revisão final e o aceite da FNR-005 continuam pendentes.

Configuração versionada contém apenas opções não sensíveis. Detalhes de JSON, locks, portas e evidências no [guia de ambiente](docs/AMBIENTE.md#backend-fnr-003--versões-verificadas-em-14112026). Banco, Docker e credenciais de provedor não são pré-requisitos para estes comandos.

### Provisionamento administrativo local (FNR-008; reset revogatório FNR-010 local)

Os comandos `user-create` e `user-reset-password` usam o processo da API sem iniciar listener HTTP, autenticação JWT ou cliente Pluggy. Execute um comando por vez, sem argumentos adicionais e sem perfil de lançamento:

```powershell
# Development precisa ser escolhido explicitamente para usar User Secrets locais.
$env:DOTNET_ENVIRONMENT = 'Development'
# Configure ConnectionStrings:Finora fora do repositório; não inclua o valor neste comando.
dotnet run --project backend/Finora.Api --no-launch-profile -- user-create

# O reset solicita o titular e a nova senha com confirmação, no mesmo terminal protegido.
dotnet run --project backend/Finora.Api --no-launch-profile -- user-reset-password
```

Configure `ConnectionStrings:Finora` em User Secrets somente para `Development` explicitamente escolhido ou por configuração externa protegida nos demais ambientes. Os comandos não aceitam e-mail, senha, connection string ou opções como argumentos; não usam pipe, arquivo ou `.env`. Digite e-mail, senha e confirmação apenas nos prompts: as teclas são interceptadas, sem eco; Enter finaliza o campo, Backspace remove o caractere anterior e Ctrl+C cancela. EOF, stdin/stdout redirecionados, terminal sem leitura protegida e confirmação divergente são recusados sem fallback com eco. A criação usa o schema FNR-007; o reset revogatório local da FNR-010 também exige a migration existente `20261118173427_AddAuthenticationSessions`. Ambos os comandos não criam/aplicam migrations nem alteram o schema no startup; o processo explícito está no [guia de ambiente](docs/AMBIENTE.md).

Códigos de saída administrativos:

| Código | Significado |
|---|---|
| `0` | Titular criado (`created`), já existente sem alteração (`already_exists`) ou reset confirmado (`password_reset session_revocation=confirmed`). |
| `1` | Falha operacional, incluindo banco/schema indisponível ou commit não confirmado/incerto; não informa se o commit ocorreu. |
| `2` | Argumentos/configuração/entrada inválidos, política de senha não atendida, confirmação divergente ou terminal interativo indisponível. |
| `3` | Nenhum titular encontrado para redefinição. |
| `4` | Conflito, como outro titular, múltiplos usuários ou concorrência. |
| `130` | Cancelamento com Ctrl+C antes da tentativa de commit; nenhuma alteração é publicada. Cancelamento/falha durante ou após a tentativa é código `1`, sem alegar rollback. |

O aviso antigo `session_revocation=not_implemented` e a afirmação de que o reset não revogava sessões pertencem à evidência histórica da FNR-008; não descrevem a implementação local posterior da FNR-010. Neste escopo local, reset de senha e revogação de todas as sessões ainda não revogadas — inclusive expiradas — compartilham a mesma transação; sessões já revogadas mantêm o timestamp. O comando não envia e-mail, não desbloqueia a conta nem cria JWT/sessão. Somente um commit confirmado produz o texto de sucesso acima. Em falha anterior à tentativa, não há alteração; em cancelamento/falha de commit ou confirmação perdida, o código `1` não permite inferir rollback nem confirmar o resultado remoto. O código `130` é reservado ao cancelamento antes da tentativa de commit. A FNR-010 permanece em implementação local, sem aceite final/integração/publicação; a entrega FNR-008 isolada não atende RF14 e os gates FNR-011/020 e de publicação continuam obrigatórios. A verificação manual TTY histórica e evidências atuais, com limites, estão no [guia de ambiente](docs/AMBIENTE.md); testes PostgreSQL usam somente o Compose descartável local.

## Funcionalidades planejadas

### Núcleo do MVP

- Login por e-mail e senha para um único usuário, sem cadastro público.
- Consulta das transações do cartão por integração com Meu Pluggy / API da Pluggy.
- Importação inicial de até 12 meses anteriores, limitada ao histórico disponibilizado pelo provedor.
- Persistência e atualização das compras sem duplicações.
- Atualização ao entrar quando a última importação completa tiver mais de 24 horas, além de acionamento manual.
- Busca e filtros por período, categoria e situação.
- Categorias iniciais e criação de novas categorias pessoais.
- Edição de categoria e observação, preservando os dados originais da compra.
- Dashboard com gastos efetivados, distribuição por categoria e evolução mensal.
- Exibição da situação da conexão e dos horários de atualização.

### Complementos após o núcleo

- Carteira de investimentos cadastrada manualmente.
- Simulador com valor inicial, aporte mensal, taxa anual e prazo.
- Exportação das transações filtradas em CSV.

Esses complementos dependem da estabilidade do núcleo e do prazo disponível. Compra de ativos, pagamentos, recomendações de investimento e atendimento a múltiplos usuários ficam fora da primeira entrega.

## Tecnologias definidas

| Área | Escolha |
|---|---|
| Backend | ASP.NET Core Web API, .NET 10 e Controllers |
| Frontend | Angular 22, conforme ambiente informado pelo autor; Node 24 |
| Interface | HTML e Tailwind CSS |
| Estado no frontend | Serviços com Signals e HttpClient/RxJS |
| Gráficos | Chart.js |
| Persistência | Entity Framework Core 10 com migrations |
| Banco | PostgreSQL |
| Autenticação | ASP.NET Core Identity e JWT |
| Testes do backend | MSTest |
| Desenvolvimento do banco | PostgreSQL local em Docker |
| Hospedagem planejada | Angular no Cloudflare Pages, API no Render e banco na Neon |

As versões exatas dos pacotes e sua compatibilidade serão verificadas no início da implementação. SQL Server e Angular Material foram substituídos pelas escolhas acima. O mapeamento entre entidades e DTOs será explícito em C#, sem AutoMapper no MVP.

## Arquitetura

Monólito modular com frontend e backend publicados separadamente, um único banco e um único repositório Git.

```text
Navegador
  ├── HTTPS → Cloudflare Pages → Angular
  └── HTTPS → Render → API ASP.NET Core
                         ├── PostgreSQL na Neon
                         └── API da Pluggy
```

O dashboard consulta os dados persistidos pelo backend. Abrir telas ou alterar filtros não dispara uma nova importação bancária.

### Organização prevista do código

```text
finora/
├── backend/
│   ├── Finora.Api/              # Controllers, autenticação e configuração
│   ├── Finora.Application/      # Casos de uso, DTOs e interfaces
│   ├── Finora.Domain/           # Entidades e regras de negócio
│   ├── Finora.Infrastructure/   # EF Core, PostgreSQL e cliente Pluggy
│   └── tests/                  # Testes com MSTest
├── frontend/                   # Angular e Tailwind CSS
├── docs/                       # PRD, TDD e backlog
└── openspec/                   # Mudanças e especificações
```
