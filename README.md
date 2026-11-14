# Finora

**Gestão financeira pessoal com foco nas transações do cartão de crédito.**

Projeto desenvolvido como proposta de Trabalho de Conclusão de Curso (TCC), com desenvolvimento individual. O Finora pretende reunir dados do cartão Nubank em um dashboard que permita consultar compras, organizar categorias e compreender os gastos por período.

Como complementos, o projeto prevê uma carteira de investimentos com cadastro manual e um simulador de evolução patrimonial com aportes.

> **Status:** ideia aprovada pelo professor e protótipo navegável disponível. FNR-001/002 concluídas conforme o backlog. Base backend FNR-003 implementada, validada localmente, revisada e aceita pelo autor nesta branch: diagnóstico e Swagger de desenvolvimento, sem dados financeiros, banco ou autenticação. Aguarda integração por PR; não publicada. Angular e fluxo financeiro integrado continuam pendentes.

## Objetivo

Desenvolver uma aplicação web que consulte dados de cartão de crédito por meio de um provedor de integração bancária, preserve o histórico e transforme as transações em informações úteis para o controle financeiro pessoal.

**Pergunta orientadora:** como uma aplicação web pode apoiar o controle financeiro pessoal ao reunir dados de cartão de crédito, indicadores de gastos e recursos de planejamento em uma única interface?

## Situação atual

| Entrega | Situação |
|---|---|
| Aprovação da ideia | Concluída, conforme confirmação do autor |
| Protótipo com dados fictícios | Disponível |
| PRD v0 | Revisão 0.2 aprovada documentalmente |
| TDD v0 | Revisão 0.3 aprovada documentalmente; base backend validada localmente, demais validações futuras pendentes |
| Base backend FNR-003 | Restore bloqueado/build e 20 testes aprovados localmente; Swagger verificado no navegador; entrega ainda não integrada |
| Conexão no painel do provedor | Cartão e transações visualizados pelo autor |
| Consulta pelo backend .NET | Pendente |
| Aplicação integrada e publicação definitiva | Pendentes |

A visualização no painel da Pluggy é evidência preliminar de acesso aos dados. A leitura pela API do Finora, a cobertura dos campos e o comportamento da sincronização ainda precisam ser validados.

A primeira meta funcional é consultar transações reais pelo backend .NET em prova de conceito local, sem exposição pública. Dashboard e aplicação hospedada vêm depois dessa validação.

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
# Executa os testes MSTest pelo runner MTP selecionado em global.json.
dotnet test --solution backend/Finora.slnx --no-build
# Inicia em Development e loopback, sem banco ou segredos.
dotnet run --project backend/Finora.Api --launch-profile http --no-build --no-restore
```

Acesse `http://127.0.0.1:5080/health`: HTTP 200 e `{"status":"ok"}`. É apenas **liveness**: não valida banco, Pluggy, autenticação ou dados reais. Não existem endpoints financeiros nesta base.

Para testar pelo navegador, abra `http://127.0.0.1:5080/swagger`, expanda `GET /health`, clique em **Try it out** e **Execute**. Confira código 200 e corpo `{"status":"ok"}`. O documento fica em `/openapi/v1.json`; documento, UI e assets locais estão disponíveis **somente em Development** (404 em Production/Staging). Não há Authorize/JWT neste incremento. Encerre com `Ctrl+C`.

Configuração versionada contém apenas opções não sensíveis. Detalhes de JSON, locks, portas e evidências no [guia de ambiente](docs/AMBIENTE.md#backend-fnr-003--versões-verificadas-em-14112026). Banco, Docker e credenciais de provedor não são pré-requisitos para estes comandos.

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
