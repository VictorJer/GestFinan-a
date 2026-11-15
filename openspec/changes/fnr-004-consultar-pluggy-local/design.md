# Design

## Context

Motivação e rastreabilidade em [proposal.md](proposal.md). Contrato em [pluggy-local-read](specs/pluggy-local-read/spec.md). Estado: SDD proposto, aguardando revisão/aprovação humana; nenhuma consulta real foi executada neste planejamento.

A base observada contém apenas `GET /health`, registro de Controllers/OpenAPI e Swagger condicional a Development em `backend/Finora.Api/Program.cs`. Application e Infrastructure contêm apenas referências de projeto. Há testes MSTest/MTP de health, isolamento da documentação e direção de referências. Não existe MediatR instalado, cliente Pluggy, banco ou login. As specs existentes de diagnóstico/exploração foram lidas integralmente e não precisam mudar.

O TDD 0.5 aprovado preserva DT-27 (segredos), DT-38 (camadas), DT-48/49 (MediatR 12.x com versão exata pendente). DT-50–53 não tornam RabbitMQ pré-requisito desta consulta local: este comando não solicita sincronização persistente.

## Goals / Non-Goals

**Goals:**
- Reutilizar a composição e camadas do backend sem abrir um caminho financeiro HTTP antes da autenticação.
- Separar contrato de leitura, comunicação HTTPS e apresentação de diagnóstico.
- Demonstrar leitura real com no mínimo um lançamento e limites explícitos, sem guardar payloads reais.

**Non-Goals:**
- Não criar outro projeto executável ou dependências de domínio artificiais.
- Não fechar o contrato financeiro, paginação completa, cobertura de 12 meses, deduplicação ou regras de indicadores de FNR-005/012/018.
- Não iniciar serviços de banco, mensageria, atualização bancária ou endpoints financeiros.

## Decisions

### D1 — Modo de comando no entry point existente

Propor `pluggy-read` como argumento não sensível do executável Finora.Api. A implementação reconhecerá o comando antes de iniciar o host HTTP, preparará configuração/DI, executará um escopo e encerrará o processo sem `Run`/`Start` do servidor. Exemplo planejado, ainda não executável: `dotnet run --project backend/Finora.Api --no-launch-profile -- pluggy-read`. Sem o comando, o comportamento atual da API permanece igual, inclusive sem validar opções Pluggy na inicialização normal. Comando desconhecido falha sem cair silenciosamente no modo servidor.

Alternativas: endpoint Swagger exporia uma superfície desnecessária antes de RF14; projeto console separado duplicaria composição. O modo de comando é a escolha confirmada do autor; nome e códigos abaixo são propostas deste SDD.

### D2 — Query/handler na Application e cliente na Infrastructure

O comando resolve `ISender` em escopo e envia uma query de leitura. O handler orquestra um contrato específico de consulta Pluggy, sem HTTP, console, entidade EF ou configuração secreta nos requests. Infrastructure encapsula autenticação, itens, contas e transações com HttpClient e mapeamento explícito; retorna DTOs mínimos e falhas tipadas sem corpo externo. Domain permanece sem pacotes/regras financeiras artificiais. A Api transforma o resultado em resumo e exit code. Nenhum Controller novo.

MediatR segue DT-49: antes de instalar, selecionar/aprovar a versão exata 12.x, verificar licença do pacote/transitivas, manutenção, avisos de segurança, frameworks e compatibilidade .NET 10. O tag 12.4.1 é referência documental, não pacote já selecionado. Registrar evidências, gerar locks reais e testar descoberta do handler/DI. Falha nesse gate interrompe a adoção e exige decisão do autor; não substituir silenciosamente por chamada direta ou outra linha. A aprovação deste SDD não seleciona implicitamente a versão.

Alternativas: chamada direta dispensaria o pacote, mas contrariaria a direção documental atual; broker seria inadequado a uma consulta pontual não persistente.

### D3 — Configuração e privacidade por lista permitida

Usar `Pluggy:ClientId`, `Pluggy:ClientSecret`, `Pluggy:ItemId` e `Pluggy:AccountId` opcional via segredos locais do .NET em Development ou variáveis externas (`Pluggy__...`). UserSecretsId poderá ser acrescentado ao projeto na implementação. Não aceitar valores sensíveis nos argumentos, exemplos, logs ou histórico de comandos documentados. O guia deve orientar configuração privada pelo titular sem solicitar que cole valores no chat. Segredos locais não são cofre criptografado.

Validar apenas no modo de comando. O AccountId, quando configurado, precisa pertencer ao item e ser do tipo cartão; sem ele, aceitar exatamente um cartão. Não selecionar por descrição/final do cartão ou imprimir IDs para resolver ambiguidades. O titular obtém a seleção no ambiente privado do provedor.

Base externa fixa `https://api.pluggy.ai`, sem override arbitrário em execução real; testes injetam transporte simulado, não uma URL para receber segredos. Não seguir redirects para outros destinos. API key vive apenas na memória da execução. Nenhum dump de corpo HTTP, headers de autenticação, URL com query, exceção bruta ou resposta inválida. Suprimir logs automáticos do cliente que registrem URLs; testar stdout, stderr e provedores de log com sentinelas. Resumo por lista permitida: etapa, resultado, contadores, continuação, duração e categoria de falha/código HTTP. Horário UTC pode compor a evidência; IDs e detalhes financeiros não.

Alternativas: payload JSON local facilitaria investigação, mas amplia retenção e risco; nesta prova não será exportado.

### D4 — Fluxo mínimo e limite de leitura

```text
pluggy-read --> validar configuracao --> POST /auth
    --> GET /items/{id} --> verificar estado/consentimento
    --> GET /accounts?itemId=... --> selecionar cartao
    --> GET /v2/transactions?accountId=... --> resumo --> sair
```

Autenticar com Client ID/Secret e usar `X-API-KEY` nas leituras. Validar campos necessários, vínculo da conta e pertencimento dos lançamentos recebidos. Verificar `consentExpiresAt` em UTC e estado/erro do item: autorização vencida ou estado que não permita leitura confiável resulta em bloqueio. Campo de expiração ausente não significa autorização eterna; registrar verificação não disponível, exigir leitura válida com lançamentos e não alegar validação completa do consentimento. Estados/valores não reconhecidos falham de forma explícita, sem inferir aptidão.

Preferir `/v2/transactions`, documentado com cursor `next`, e ler apenas a primeira página, sem filtro temporal nesta prova. Contar registros recebidos, validar envelope e indicar se `next` existe; não seguir o cursor nem declarar histórico completo. Esse limite reduz chamadas e não antecipa FNR-005. Contas devem ser enumeradas conforme o envelope/paginação documentado para não selecionar um cartão ignorando contas em páginas seguintes. Contrato externo deve ser conferido na referência oficial antes de codificar; indisponibilidade de v2 neste acesso é bloqueio a registrar, não fallback oculto para v1.

Alternativas: guia pessoal exemplifica `/transactions` antigo; misturar os parâmetros de ambos causaria falso diagnóstico. Leitura completa de histórico pertence à etapa de validação do contrato/importação, não ao aceite mínimo RF01.

### D5 — Falhas e tempo de execução previsíveis

Propor timeout de 30 segundos por chamada, limite total de 120 segundos e zero retries automáticos nesta prova diagnóstica. Propagar CancellationToken e Ctrl+C, descartar respostas/cliente adequadamente. HTTP 401/403 indica falha de autorização sem imprimir detalhes; 429 indica limite sem repetição; 5xx/transporte/timeout distinguem indisponibilidade de configuração/contrato inválido. Não renovar credenciais automaticamente em loop.

Exit codes propostos: `0` leitura comprovada com lançamentos; `1` falha operacional/contrato; `2` configuração/seleção inválida; `3` leitura inconclusiva sem lançamentos; `4` conexão bloqueada/renovação necessária; `130` cancelamento. Informar a etapa sem dados sensíveis. Nunca converter falha parcial em sucesso; nenhum dado é persistido.

Alternativa: retries ajudariam em falhas transitórias, mas esconderiam o diagnóstico inicial e aumentariam chamadas sem política real de limites validada.

### D6 — Validação automatizada separada da prova real

Reutilizar `backend/tests/Finora.Api.Tests` com transporte HTTP simulado e testes do handler, comando, DI e captura de logs. Cobrir os cenários da spec: configuração incompleta; autenticação negada; consentimento expirado com status anterior de sucesso; estado desconhecido; cartão inexistente/ambíguo/seleção explícita inválida; contas paginadas; transações válidas com/sem continuação; vazio; payload inválido; vínculo incorreto; timeout/429/5xx/cancelamento; ausência de vazamentos e listener. Preservar todos os testes HTTP/arquitetura existentes e garantir que o modo normal funciona sem segredos Pluggy.

Testes automatizados nunca dependem da conta real. A validação manual usa configuração privada do titular e registra somente data, comando sem valores, etapas, contadores, continuação, duração e resultado sanitizado em AMBIENTE.md. Se a execução real não recuperar lançamentos, FNR-004 permanece bloqueada/inconclusiva, mesmo com build e mocks aprovados. Fixtures futuras são fictícias, não cópias de respostas reais.

## Risks / Trade-offs

- [API/condições do acesso pessoal diferentes do painel] --> confirmar referência e executar prova real; não declarar integração pelo painel ou por mocks.
- [Vazio com autorização expirada] --> checar item e validade antes da leitura; vazio nunca conclui RF01.
- [MediatR histórico incompatível ou vulnerável] --> gate DT-49 antes da instalação; interromper em vez de alterar baseline silenciosamente.
- [Logs padrão do HttpClient vazam itemId/accountId] --> categorias de log controladas, redação por lista permitida e testes com sentinelas em sucesso/falha.
- [Uma página não revela volume/cobertura totais] --> rótulo explícito de amostra e continuação; medição completa e normalização reservadas a FNR-005.
- [Comando no assembly web exige isolamento] --> não iniciar listener e testar que execução normal/health não disparam a integração.

## Migration Plan

Sem migration de banco ou deploy aplicável. Após revisão/aprovação explícita do SDD e gate DT-49, implementar somente na feature branch; validar restore bloqueado, build, MSTest e prova local. Atualizar guia/backlog com resultados verdadeiros. Commits locais, PR e integração dependem das autorizações correspondentes; nada disso é autorizado por gerar estes artefatos.

Reversão do incremento: remover o modo de comando, cliente e dependências introduzidas por PR de reversão, preservando health/Swagger e histórico documental; não há dados persistidos a reverter. Não publicar endpoint financeiro como alternativa de teste. Sync e arquivamento somente após validação e aceite, preferencialmente no PR da entrega.

## Open Questions

- Qual versão exata MediatR 12.x passará pelo gate DT-49? Seleção/aprovação exigida antes de instalação, sem mudar o contrato da capability.
- O acesso real do titular permite os endpoints/fields documentados e retorna lançamentos? A prova resolverá esse fato externo; falha impede conclusão, não autoriza ampliar o escopo.

## Referências externas

Documentação oficial consultada na exploração em 15/11/2026; descreve contrato esperado, não resposta observada da conta:
- https://docs.pluggy.ai/en/docs/guides/meu-pluggy-personal-use — autenticação, acesso pessoal, item configurado, espelhamento e consentimento.
- https://docs.pluggy.ai/en/reference/transaction/transactions-list-by-cursor — envelope de transações v2 e cursor.
- https://docs.pluggy.ai/en/reference/basic-concepts — diferenças entre paginação v1/v2.

O guia pessoal informa atualização espelhada a cada 24 horas, impossibilidade de atualização manual dos itens MeuPluggy e indisponibilidade de `GET /v2/items` nesse acesso. Não confundir leitura de dados disponíveis com atualização no banco.
