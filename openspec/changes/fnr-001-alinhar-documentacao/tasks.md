# Tasks

Esta lista acompanha somente a FNR-001 documental. Itens marcados registram trabalho realizado; a aprovação humana está registrada explicitamente em 2.2, não inferida dos checkboxes ou do CLI. Nenhum código da aplicação foi iniciado. Critérios C1–C6: `design.md`, seção Rastreabilidade e critérios de revisão.

## 1. Alinhamento e rastreabilidade

- [x] 1.1 Criar a branch `docs/fnr-001-alinhar-documentacao` e a mudança pelo CLI; verificar branch ativa e metadados do schema `spec-driven`, com `skip_specs: true` por escopo exclusivamente documental.
- [x] 1.2 Alinhar o PRD aos comportamentos confirmados, mantendo RF01–RF13 e acrescentando RF14–RF16; verificar C1–C4 e ausência de SQLite como proposta pendente ou de versões técnicas duplicadas no PRD.
- [x] 1.3 Alinhar o TDD ao PR obrigatório e deploy condicionado aos checks, sem alterar decisões confirmadas nem aprovar propostas; verificar C1, C3–C5 e manutenção das validações de ambiente e Pluggy.
- [x] 1.4 Conferir README e preservar o backlog preexistente, atualizando links, referências e status apenas da FNR-001; verificar C6, renderização do bloco de árvore e ausência de início das tarefas seguintes.
- [x] 1.5 Preparar proposta e SDD rastreável; verificar arquivos existentes e estado de revisão/aprovação explícito, sem specs artificiais de comportamento.

## 2. Verificação integrada e revisão

- [x] 2.1 Executar `openspec validate fnr-001-alinhar-documentacao --strict` e `git diff --check`, verificar links locais dos documentos e conferir diff/status para confirmar que somente documentação foi alterada; registrar resultados nesta lista.
- [x] 2.2 Apresentar o diff para revisão do PRD, TDD, README, backlog e artefatos, receber aprovação explícita ou incorporar correções; conclusão verificável por registro da revisão e aprovação do SDD, sem presumir aceite pelo estado do CLI. O autor aprovou explicitamente os documentos e o SDD nesta conversa em 13/11/2026 ao solicitar: “Registre a aprovação explícita dos documentos e do SDD da FNR-001”.

Verificação em 13 de novembro de 2026: validação OpenSpec estrita aprovada (deltas dispensados por `skip_specs`); `git diff --check` aprovado, apenas avisos de conversão LF/CRLF do Git; oito links locais verificados em sete documentos. Diff e status conferidos: apenas README, PRD, TDD, backlog e os artefatos desta mudança. Testes de aplicação não se aplicam; não há código criado. A aprovação documental de 2.2 não conclui a FNR-001 nem aprova as propostas de implementação de futuras mudanças.

## 3. Entrega por Pull Request

- [x] 3.1 Reexecutar validações documentais e conferir o diff incluindo os arquivos novos; verificar OpenSpec estrito, diff check, links e ausência de código no conjunto a commitar. Em 13/11/2026, OpenSpec estrito e oito links locais passaram; `git diff --cached --check` passou após substituir três quebras Markdown com espaços finais no backlog novo por barras invertidas, sem alteração de conteúdo. Os oito arquivos staged são exclusivamente documentais; não há workflows de CI versionados neste estado do projeto, e checks remotos não foram presumidos aprovados.
- [ ] 3.2 Criar commit na branch atual e abrir PR para `main` com resumo, rastreabilidade, validações e riscos; verificar hash, URL e estado aberto do PR, sem merge.
- [ ] 3.3 Obter revisão/aprovações do PR e checks exigidos; conclusão verificável no GitHub, sem equiparar aprovação documental à aprovação do PR. Não executar integração sem autorização posterior.

## Workflow follow-up

- Somente após revisão e autorização para continuar, abrir PR com PRD, TDD, SDD, testes documentais e riscos vinculados; integrar exclusivamente por PR após aprovações e checks necessários, sem contornar proteções.
- Registrar PR, evidências e conclusão da FNR-001 no backlog; não declarar concluída enquanto revisão/integração estiverem pendentes. Não há deploy de aplicação nesta mudança documental.
- Não iniciar FNR-002 nem arquivar esta mudança nesta etapa.
