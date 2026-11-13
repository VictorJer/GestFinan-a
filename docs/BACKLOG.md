# Finora — Backlog de desenvolvimento

**Versão:** 0.1\
**Criado em:** 13 de novembro de 2026\
**Base:** `docs/PRD.md`, `docs/TDD.md` e `AGENTS.md` do projeto `D:\financeiro`.\
**Estado:** documentos e SDD da FNR-001 aprovados explicitamente pelo autor em 13/11/2026; revisão do PR, checks e integração pendentes. Nenhuma tarefa de implementação foi considerada concluída.

## 1. Resultado esperado

Entregar primeiro o fluxo completo: autenticar → consultar/importar o cartão → listar compras → organizar categorias e observações → visualizar gastos corretos no dashboard.

O foco é um único usuário e sua conexão Nubank via Meu Pluggy / API da Pluggy. Investimentos, simulador e CSV são complementos após a estabilidade desse núcleo.

Já existem PRD, TDD, README e configuração do OpenSpec. O autor visualizou compras no painel do provedor, mas a leitura pelo backend .NET ainda não foi validada. Esses fatos são o ponto de partida, não prova de implementação.

A primeira meta funcional é a prova de conceito local de FNR-004: ler transações reais pelo backend, sem exposição pública. O fluxo integrado acima e o dashboard vêm depois dessa validação, conforme a ordem dos marcos.

## 2. Como executar este backlog com SDD

O backlog organiza o trabalho; **não substitui as especificações nem aprova uma implementação**.

Para cada mudança, mantendo uma por vez:

1. Conferir os requisitos do PRD e as decisões do TDD; ajustar inconsistências antes de avançar.
2. Criar uma feature branch, conforme `AGENTS.md`. Não implementar diretamente na `main`.
3. Criar ou atualizar a mudança no OpenSpec usando o schema instalado, incluindo proposta, desenho, requisitos/cenários e tarefas aplicáveis.
4. Registrar as revisões e aprovações necessárias. Não presumir aprovação do SDD por existir um item neste backlog.
5. Implementar somente o escopo especificado, com os testes relevantes.
6. Validar o resultado, abrir PR e vincular PRD, TDD, mudança OpenSpec e IDs deste backlog.
7. Integrar por PR somente após revisão, aprovações e checks necessários; atualizar o status e a evidência de conclusão.

Não presumir comandos ou estrutura de artefatos do OpenSpec sem consultar a instalação do projeto. A publicação após atualização da `main` continua condicionada aos checks aprovados.

Na FNR-001 foi confirmado o schema local `spec-driven`. Por ser exclusivamente documental, a mudança declara `skip_specs: true`, sem criar requisitos artificiais da aplicação. O `design.md` registra seu SDD; a disponibilidade dos artefatos no CLI não aprova os documentos nem autoriza implementação.

### Prioridade e status

- **P0:** necessário para o núcleo e sua entrega segura.
- **P1:** complemento, iniciado após o marco de núcleo pronto.
- **P2:** evolução futura, sem compromisso na primeira entrega.
- **Status:** A fazer → Em especificação → Aguardando revisão → Em implementação → Em validação → Concluído. Usar Bloqueado com motivo explícito quando necessário.
- **Tamanho:** P = pequeno, M = médio, G = grande. Dividir itens G em tarefas menores no SDD; tamanho não é promessa de duração.
- Dependências indicam pré-requisitos de implementação. Testes, documentação e revisão acompanham cada tarefa, sem esperar a fase final.

## 3. Ordem de execução

| Marco | Itens | Evidência para avançar |
|---|---|---|
| M0 — Preparação | FNR-001 a FNR-003 | Documentos coerentes e backend mínimo compilando |
| M1 — Viabilidade principal | FNR-004 e FNR-005 | Consulta real pelo .NET e contrato de dados documentado |
| M2 — Persistência e acesso | FNR-006 a FNR-011 | Banco local, sessão JWT, revogação e comandos administrativos verificados |
| M3 — Dados utilizáveis | FNR-012 a FNR-018 | Importação consistente, APIs e totais conciliados |
| M4 — Aplicação integrada | FNR-019 a FNR-023 | Login, conexão, compras e gráficos funcionando no Angular |
| M5 — Entrega do núcleo | FNR-024 a FNR-028 | Checks, publicação e demonstração verificados |
| M6 — Complementos | FNR-029 a FNR-031 | Módulos P1 validados sem regressão no núcleo |

O primeiro marco de risco é M1. Se não for possível consultar os dados reais, registrar a causa e revisar o plano; dados fictícios permitem desenvolver a interface, mas não encerram a tarefa de integração.

O prazo original era de duas semanas. Como a data final não foi confirmada, este documento não reinicia o prazo nem atribui novas datas. Reavaliar a capacidade disponível ao iniciar a execução e preservar P0 antes de P1.

## 4. Backlog principal

O status inicial dos itens é **A fazer**; atualizações ficam no registro de execução (§7). As dependências usam os IDs da primeira coluna.

| ID | Pri. | Tamanho | Entrega | Depende de | Rastreabilidade |
|---|---|---|---|---|---|
| FNR-001 | P0 | P | Alinhar documentação e fluxo OpenSpec | — | PRD §§3, 7–10, 13, 15; TDD §§1, 4, 7–9, 15; AGENTS |
| FNR-002 | P0 | P | Verificar ambiente e fixar versões | 001 | TDD §7 |
| FNR-003 | P0 | M | Criar solução backend e testes mínimos | 001, 002 | TDD §3 |
| FNR-004 | P0 | M | Consultar cartão e transações reais pelo .NET | 003 | RF01; TDD §4 |
| FNR-005 | P0 | M | Validar contrato e normalização da Pluggy | 004 | RF02, RF07; PRD §8; TDD §§7, 10, 12 |
| FNR-006 | P0 | M | Preparar PostgreSQL local e EF Core | 003 | TDD §§9, 10 |
| FNR-007 | P0 | G | Modelar persistência do núcleo | 005, 006 | RF02, RF03; TDD §10 |
| FNR-008 | P0 | M | Criar e redefinir usuário por comando | 006 | RF14; TDD §5 |
| FNR-009 | P0 | G | Implementar login JWT e sessões persistentes | 008 | RF14; TDD §5 |
| FNR-010 | P0 | M | Revogar sessões no logout e reset de senha | 009 | RF14; TDD §5 |
| FNR-011 | P0 | M | Padronizar segurança e respostas da API | 009 | RF09; TDD §§2, 5, 11 |
| FNR-012 | P0 | G | Importar transações sem duplicação | 005, 007 | RF02, RF03; TDD §§10, 12 |
| FNR-013 | P0 | G | Controlar execução e retomada da sincronização | 011, 012 | RF08, RF09; TDD §12 |
| FNR-014 | P0 | M | Aplicar regra de atualização e estado da conexão | 013 | RF08, RF16; TDD §§6, 11 |
| FNR-015 | P0 | M | Criar API de cartões e consulta de transações | 011, 012 | RF04, RF05; TDD §11 |
| FNR-016 | P0 | M | Provisionar categorias e criar categorias pessoais | 007, 011 | RF06; TDD §15 |
| FNR-017 | P0 | M | Editar categoria e observação da compra | 015, 016 | RF06, RF15; TDD §§6, 15 |
| FNR-018 | P0 | G | Calcular indicadores do dashboard | 005, 015, 017 | RF07; PRD §8; TDD §13 |
| FNR-019 | P0 | M | Criar base Angular com Tailwind | 002, 003 | TDD §3 |
| FNR-020 | P0 | M | Integrar login, sessão e navegação protegida | 010, 011, 019 | RF14; TDD §§3, 5 |
| FNR-021 | P0 | M | Exibir cartão e atualização de dados | 014, 020 | RF08, RF09; TDD §§6, 13 |
| FNR-022 | P0 | G | Implementar compras, filtros e categorias na interface | 015, 016, 017, 020 | RF04, RF05, RF06, RF09 |
| FNR-023 | P0 | G | Implementar dashboard com Chart.js | 018, 020, 022 | RF07, RF09; TDD §13 |
| FNR-024 | P0 | M | Consolidar testes críticos e CI | 010, 013, 017, 018, 023 | TDD §14 |
| FNR-025 | P0 | M | Preparar Docker de produção e verificação de saúde | 003, 011, 013 | TDD §§2, 9 |
| FNR-026 | P0 | G | Configurar Neon, migrations e publicação da API | 024, 025 | TDD §§2, 9 |
| FNR-027 | P0 | M | Publicar Angular no Cloudflare Pages com gates | 024, 026 | TDD §§2, 9 |
| FNR-028 | P0 | M | Validar núcleo publicado e preparar entrega acadêmica | 021, 022, 023, 026, 027 | PRD §13; TDD §14 |
| FNR-029 | P1 | G | Implementar carteira manual de investimentos | 028 | RF10, RF11 |
| FNR-030 | P1 | G | Implementar simulador de aportes | 028 | RF12; PRD §8; TDD §13 |
| FNR-031 | P1 | M | Exportar transações filtradas em CSV | 028 | RF13; TDD §11 |

## 5. Critérios de conclusão por tarefa

### Preparação e prova de conceito

**FNR-001 — Documentos e fluxo**

- Atualizar o PRD para refletir as decisões posteriores: ambiente hospedado com autenticação, categorias pessoais e observações.
- Trocar a orientação antiga de envio direto à main pelo fluxo feature branch → revisão → PR → integração, preservando migrations anteriores ao deploy dependente.
- Confirmar a configuração OpenSpec e definir as primeiras mudanças pequenas; registrar o que é proposta técnica e o que já foi aprovado. Não iniciar código com bloqueios de especificação.

**FNR-002 — Ambiente**

- Conferir .NET SDK, Node, Angular CLI, Docker e gerenciador de pacotes; validar compatibilidade das versões escolhidas.
- Fixar versões compatíveis, arquivos de lock, ferramenta EF e runner dos testes Angular; documentar os comandos reais, sem presumir ferramentas instaladas.

**FNR-003 — Estrutura backend**

- Criar Api, Application, Domain e Infrastructure com referências na direção definida no TDD; Controllers sem regras financeiras.
- Preparar MSTest e um check real de build; incluir arquivos de configuração de exemplo sem segredos e regras de ignore adequadas.
- A solução compila e o comando documentado de verificação funciona. Não expor dados bancários em endpoint desprotegido.

**FNR-004 — Leitura real**

- Executar prova de conceito local pelo .NET: autenticar na Pluggy, consultar a conexão configurada, identificar o cartão e recuperar lançamentos.
- Configurar os segredos fora do código e não imprimir credenciais, JWTs ou compras completas em logs/CI.
- Registrar evidência sanitizada de sucesso ou motivo concreto do bloqueio. O painel web do provedor, sozinho, não conclui esta tarefa.

**FNR-005 — Contrato externo**

- Conferir paginação real, histórico disponível, tipos, sinais, status, datas, moeda e campos opcionais de parcelas/fatura/limite.
- Definir como reconhecer compra, estorno, pagamento de fatura, pendência, parcela futura e natureza desconhecida; não inferir apenas pelo sinal.
- Medir volume e duração de leitura; criar exemplos fictícios representativos para MSTest e atualizar o SDD com limitações descobertas.

### Banco e autenticação

**FNR-006 — Banco local**

- Disponibilizar PostgreSQL em Docker com persistência de desenvolvimento, parâmetros externos e instruções reproduzíveis.
- Configurar EF Core/Npgsql e executar uma migration de validação local. Não chamar `Database.Migrate()` ao iniciar a API.
- Testes de integração usam banco descartável, não o banco local pessoal nem a Neon hospedada.

**FNR-007 — Modelo relacional**

- Modelar conexão, cartão, transação, categoria, anotação e SyncRun com FKs, precisão decimal, moeda e datas consistentes.
- Criar restrições de identidade externa e anotação única, além dos índices necessários às consultas; IDs externos são opacos.
- Migrations funcionam em base vazia e atualização; testar unicidade e rollback de operações que precisam ser atômicas. Investimentos podem esperar P1.

**FNR-008 — Usuário administrativo**

- Criar usuário pelo Identity e permitir redefinir senha via comando, com entrada protegida, sem senha no código/argumentos/logs.
- Repetir criação não duplica usuário nem redefine senha silenciosamente; não disponibilizar cadastro público.
- Integrar posteriormente o reset à revogação transacional de FNR-010 antes da publicação.

**FNR-009 — Login**

- `POST /api/auth/login` valida credenciais com Identity e controle de tentativas; grava a sessão antes de entregar JWT.
- JWT dura 30 minutos, sem refresh token e sem dados financeiros; validar algoritmo, assinatura, emissor, destinatário e expiração.
- Sessão ativa é consultada no banco em cada chamada protegida. Chave forte fica em segredo estável por ambiente; token completo não é persistido.

**FNR-010 — Revogação**

- Logout invalida a sessão atual imediatamente; reset de senha invalida todas as sessões do usuário e lida com login concorrente.
- Token expirado, adulterado, revogado ou de sessão ausente não acessa dados. Indisponibilidade do banco não libera acesso nem simula senha incorreta.
- Testes HTTP exercitam login, acesso, logout, reset e revogação. Se o logout remoto falhar, a interface não pode afirmar que foi confirmado.

**FNR-011 — Base segura da API**

- Exigir autenticação e propriedade dos recursos; UserId não pode ser escolhido livremente no corpo/filtro.
- Configurar CORS para origens específicas, validação, Problem Details, códigos HTTP e traceId sem vazamento de dados.
- Diferenciar 401, 403, conflitos e indisponibilidade. Health mínimo não expõe configuração interna; não abrir cadastro nem conexão bancária pública por conveniência.

### Integração e APIs financeiras

**FNR-012 — Importação consistente**

- Processar todas as páginas do histórico de até 12 meses disponível, preservando as parcelas futuras retornadas em situação própria.
- Repetir a mesma importação não cria duplicatas; alterações de pendências e estornos são atualizadas e anotações pessoais são preservadas.
- Não fundir contas só por nome/final do cartão; ausência em resposta vazia/parcial não exclui histórico. Tratar reconexão e limites de reconciliação.
- Falha no meio da coleta não apresenta o período como completo nem mistura silenciosamente dados parciais com o conjunto publicado.

**FNR-013 — Execução persistente**

- Fechar no SDD o processamento proposto no TDD após a medição de FNR-005; registrar execução, contadores e erro sanitizado.
- Impedir duas execuções simultâneas da mesma conexão; tratar reinício, posse expirada e retomada idempotente sem execução eternamente pendente.
- Se adotar processamento assíncrono: responder 202, consultar SyncRun e usar trabalhador com estado persistente; não usar tarefa solta apenas em memória.
- Testar falha em página intermediária, reinício e erro de autorização; não manter transação de banco aberta durante HTTP externo.

**FNR-014 — Quando atualizar**

- Entrada autenticada solicita atualização se não há sucesso anterior ou se ele tem mais de 24 horas; validar a regra no servidor.
- Botão manual usa o mesmo fluxo, com intervalo mínimo definido após conferir limites; filtros e navegação não disparam importação.
- Exibir separadamente importação no Finora, atualização na origem e cobertura disponível; dados antigos permanecem acessíveis quando a origem falha.
- Implementar orientação de renovação no provedor. Retry limitado para falhas transitórias, respeitando limites; não repetir autorização inválida indefinidamente.

**FNR-015 — Consulta de dados**

- Implementar listagem de cartões e transações com busca, filtros combináveis, ordenação estável e paginação limitada.
- Total filtrado considera todas as páginas e identifica moedas; intervalo de datas é consistente entre API e interface.
- Testar filtros combinados, conjunto vazio e tentativa de acesso a outro titular. Campos externos ausentes aparecem como indisponíveis, sem exemplos fictícios.

**FNR-016 — Categorias**

- Provisionar categorias iniciais idempotentes: Alimentação, Transporte, Compras, Lazer, Saúde, Educação, Outros e Sem categoria.
- Permitir listar e criar categorias pessoais, com nome validado e política de normalização documentada; impedir duplicidade e associação a outro usuário.
- Não criar renomeação, arquivamento ou exclusão neste MVP. Testar criação concorrente e reinicialização sem duplicação.

**FNR-017 — Anotações**

- Permitir editar categoria e observação de até 500 caracteres; observação é texto simples e pode ser limpa.
- Preservar data, valor e descrição originais; uma nova sincronização não apaga anotações manuais.
- A alteração aparece nos filtros e agregados. Testar propriedade da compra e da categoria e o tratamento único de Sem categoria.

**FNR-018 — Indicadores**

- Calcular no backend compras efetivadas líquidas de estornos elegíveis, quantidade, evolução mensal e distribuição por categoria.
- Não contar pagamento de fatura como compra/abatimento duplicado; separar pendências, parcelas futuras, encargos e natureza desconhecida conforme a normalização validada.
- Agregar BRL sem conversão inventada. O conjunto elegível do dashboard deve conciliar com o da listagem, inclusive após reclassificação.
- Não apresentar renda, saldo bancário ou patrimônio líquido derivados só das compras. Testes cobrem limites de período e estornos.

### Frontend

**FNR-019 — Base Angular**

- Criar Angular, HTML/Tailwind, rotas e componentes compartilhados, com organização por funcionalidades.
- Serviços com Signals e HttpClient/RxJS; configurar URL da API por ambiente, sem segredos de servidor.
- Build e runner de testes funcionam. O visual usa o protótipo como referência, sem inventar funcionalidades prontas.

**FNR-020 — Sessão na interface**

- Implementar login, guard, interceptor restrito à origem da API e logout com token apenas em memória.
- Expiração ou reload exige novo login; 401 encerra a sessão local e 403 informa falta de permissão. Não usar localStorage, sessionStorage ou refresh token.
- Tratar falha de rede separadamente de credencial inválida e verificar os fluxos críticos.

**FNR-021 — Cartão e conexão**

- Exibir dados reais disponíveis, situação, cobertura e horários; não exibir credenciais nem pedir senha do banco.
- Integrar atualização automática elegível e botão manual, evitando cliques concorrentes e consultas sem término.
- Verificar sucesso, indisponibilidade, autorização expirada e retomada da instância; estados fictícios só em modo de demonstração identificado.

**FNR-022 — Compras**

- Construir lista paginada com busca, filtros e total de todo o conjunto filtrado.
- Permitir criar categoria e aplicá-la à compra sem recarregar a página; editar observação com retorno de validação.
- Mostrar situação e parcelamento quando disponíveis, estados vazio/carregando/erro e navegação acessível em desktop/celular.
- Testar filtros e preservação de anotações após atualização.

**FNR-023 — Dashboard**

- Integrar indicadores do backend a gráficos Chart.js em componentes próprios; não recalcular regras financeiras divergentes no Angular.
- Filtrar período e refletir alterações de categoria. Exibir resumos HTML acessíveis além dos gráficos.
- Conciliar valores exibidos com o backend e a lista; tratar dados incompletos, ausência de histórico e erro sem mostrar zeros enganosos.

### Qualidade, publicação e TCC

**FNR-024 — Checks**

- Consolidar testes MSTest e testes críticos Angular já criados com as funcionalidades; integração usa PostgreSQL descartável e Pluggy simulada.
- Builds e testes relevantes rodam em CI e falham para regressões de importação, segurança e cálculos; segredos/dados bancários reais não são necessários ao CI.
- Definir checks obrigatórios e impedir publicação anterior ao resultado. Não estabelecer percentual de cobertura arbitrário.

**FNR-025 — Imagem e operação**

- Criar Dockerfile da API, configuração de porta/ambiente e health mínimo, com passos reproduzíveis para o autor.
- Não depender de filesystem persistente no Render; segredos são externos. Validar inicialização, encerramento e tratamento da execução interrompida.
- Documentar geração/rotação de chaves e os mecanismos de proteção usados pelos comandos do Identity.

**FNR-026 — API hospedada**

- Configurar Neon e Render, TLS, segredos e conexão de banco apropriada; validar limites do plano e não registrar credenciais.
- Documentar/aplicar migrations compatíveis com a versão ativa antes do deploy dependente; falha impede a integração que dispararia a publicação.
- Publicar automaticamente apenas o commit aprovado pelos checks; impedir auto-deploy paralelo e versão antiga sobrescrevendo nova.
- Verificar saúde, login, autorização e leitura real no ambiente hospedado; registrar falhas e recuperação.

**FNR-027 — Frontend hospedado**

- Configurar Cloudflare Pages com build estático, rotas SPA e URL da API; ajustar CORS na API para a origem definitiva.
- Publicar após checks, a partir do commit validado; considerar que frontend/API não têm troca de versão atômica.
- Validar login, sessão, atualização, tabela e gráficos publicados, inclusive primeira abertura após inatividade da API.

**FNR-028 — Entrega do núcleo**

- Executar checklist do PRD: integração real, repetição sem duplicação, anotações persistidas, totais conciliados, sessão/revogação e falhas visíveis.
- Confirmar ausência de segredos/dados pessoais versionados e preparar demonstração com dados fictícios ou anonimizados, sem alterar a origem real.
- Atualizar README com comandos verificados, ambiente, limitações e roteiro de apresentação. Registrar evidências sanitizadas úteis ao texto do TCC.
- Núcleo pronto somente quando há evidência funcional e revisões/checks concluídos; abrir P1 não compensa P0 incompleto.

### Complementos

**FNR-029 — Carteira manual**

- Criar migration, APIs e telas para cadastrar, editar, excluir e listar investimentos do titular.
- Exibir aplicado, atual, diferença e distribuição por classe com persistência; não apresentar variação simples como rentabilidade ajustada a aportes/datas.
- Testar propriedade, validação de valores e conciliação de totais. Não integrar cotações ou operações de compra/venda.

**FNR-030 — Simulador**

- Calcular no backend a projeção do PRD com taxa anual efetiva convertida em mensal e aportes ao fim do mês.
- Exibir série, total aportado e rendimento projetado; validar entradas e explicitar ausência de impostos, taxas e inflação.
- Testar taxa zero e resultado independente com juros. O cenário não altera a carteira real e não promete retorno.

**FNR-031 — CSV**

- Exportar todo o conjunto filtrado autorizado, não apenas a página visível, com datas, moedas e valores coerentes.
- Escapar separadores, aspas e quebras de linha e neutralizar células de texto interpretáveis como fórmulas por planilhas.
- Testar filtros e conteúdo exportado; não expor dados de outro titular ou credenciais.

## 6. Evolução futura — P2

| ID | Evolução | Condição antes de iniciar |
|---|---|---|
| FNR-032 | Permanecer conectado após recarregar | Redesenhar sessão, renovação e armazenamento, preservando revogação; não habilitar localStorage como atalho |
| FNR-033 | Renomear e arquivar categorias | Definir regras de histórico e filtros; não apagar vínculos existentes |
| FNR-034 | Conexão bancária iniciada na interface | Validar fluxo, condições de acesso e consentimentos do provedor |
| FNR-035 | Novos usuários ou instituições | Redefinir escopo, isolamento, condições comerciais e autorização |
| FNR-036 | Cotações e investimentos automáticos | Validar fontes, cobertura, licenças e precisão das métricas |
| FNR-037 | Rotina automática com aplicação fechada | Escolher executor compatível com hospedagem e recuperação; não depender de instância gratuita suspensa |

Todos os itens P2 ficam **fora da entrega inicial** e exigem nova especificação.

## 7. Registro de execução

Ao iniciar uma tarefa, preencher este quadro sem alterar o significado dos critérios de aceite:

| Item | Status | Mudança OpenSpec | Branch / PR | Evidência ou bloqueio |
|---|---|---|---|---|
| FNR-001 | Aguardando revisão (PR) | [fnr-001-alinhar-documentacao](../openspec/changes/fnr-001-alinhar-documentacao/proposal.md) | `docs/fnr-001-alinhar-documentacao`; [PR #1](https://github.com/VictorJer/GestFinan-a/pull/1), commit documental `77dcde4` | Aprovação explícita dos documentos (PRD, TDD, README, backlog e artefatos) e do SDD pelo autor nesta conversa em 13/11/2026. OpenSpec estrito, diff check incluindo arquivos novos e oito links locais aprovados. PR aberto, sem revisão ou checks reportados pelo GitHub na consulta inicial e sem auto-merge. Aprovação documental não conclui a tarefa: revisão do PR, verificação dos checks exigidos e integração pendentes. Sem código, merge, arquivamento ou avanço para FNR-002. |

Adicionar os demais itens conforme entrarem em execução. Um PR aberto não significa tarefa concluída. Manter apenas uma mudança principal em implementação por vez no trabalho individual.

**Próxima ação:** revisar o PR #1 da FNR-001 e verificar os checks exigidos, mantendo a integração pendente e independente da aprovação documental. Não fazer merge, arquivar ou iniciar FNR-002 nesta etapa. A sequência posterior permanece FNR-002, FNR-003 e FNR-004, cada uma em sua mudança e após autorização. A escrita do TCC acompanha os marcos desde o início.
