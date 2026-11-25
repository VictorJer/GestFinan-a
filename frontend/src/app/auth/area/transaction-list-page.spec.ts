import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../api-base-url.token';
import { apiAuthInterceptor } from '../api-auth.interceptor';
import { SessionService } from '../session';
import { TransactionListPage } from './transaction-list-page';

const API_BASE = 'https://api.example.test/proxy/';
const CARDS_URL = `${API_BASE}api/cards`;
const CATEGORIES_URL = `${API_BASE}api/categories`;
const TRANSACTIONS_URL = `${API_BASE}api/transactions`;
const CARD_ID = '2a990192-cb59-43eb-8520-cfef6a0e3e40';
const CATEGORY_ID = '6e209084-7488-4d97-930f-10f52df4b116';
const MOVEMENT_ID = '5b76333c-6d8e-4d9e-aac6-3a6096f97ab1';
const CREATED_CATEGORY_ID = '11b8435c-912c-4528-9e5d-89c924f950a2';

let backend: HttpTestingController;
let fixture: ComponentFixture<TransactionListPage> | undefined;

// Compõe a página real com interceptor e transporte sintético para impedir acesso a serviços externos.
function configurePage(): void {
  TestBed.configureTestingModule({
    imports: [TransactionListPage],
    providers: [
      { provide: API_BASE_URL, useValue: API_BASE },
      provideHttpClient(withInterceptors([apiAuthInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  TestBed.inject(SessionService).startSession({
    accessToken: 'synthetic-transaction-page-token',
    tokenType: 'Bearer',
    expiresIn: 1800,
    expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
  });
}

// Monta a rota após instalar a sessão para que os GETs fiquem vinculados à geração correta.
function openPage(): ComponentFixture<TransactionListPage> {
  fixture = TestBed.createComponent(TransactionListPage);
  fixture.detectChanges();
  return fixture;
}

// Responde filtros de cartão e categoria com opções sintéticas, incluindo o virtual nulo não filtrável.
function flushFilterOptions(): void {
  backend
    .expectOne(CARDS_URL)
    .flush([{ id: CARD_ID, name: 'Cartão principal', lastFour: '1234', currencyCode: 'BRL' }]);
  backend.expectOne(CATEGORIES_URL).flush([
    { id: CATEGORY_ID, name: 'Pessoal', kind: 'personal' },
    { id: null, name: 'Sem categoria', kind: 'unassigned' },
  ]);
}

// Localiza GET de movimentos pela URL-base e deixa parâmetros disponíveis para asserções separadas.
function expectTransactionRequest() {
  return backend.expectOne((request) => request.url === TRANSACTIONS_URL);
}

// Mantém cada movimento completo conforme o contrato, permitindo testar textos desconhecidos e campos ausentes.
function movement(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: '5b76333c-6d8e-4d9e-aac6-3a6096f97ab1',
    cardId: CARD_ID,
    description: 'Compra de teste sintético',
    amount: 17.25,
    currencyCode: 'BRL',
    originalDate: 'data original textual de origem',
    transactionDate: '2026-11-24',
    dateReason: null,
    nature: 'Purchase',
    natureReason: null,
    situation: 'Effective',
    situationReason: null,
    installmentNumber: null,
    totalInstallments: null,
    categoryId: null,
    note: null,
    ...overrides,
  };
}

// Conserva sucesso, janela e execução como informações independentes, sem completar campos desconhecidos.
function coverage(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    lastSuccessfulImportAtUtc: null,
    lastSuccessfulWindow: null,
    latestExecution: null,
    ...overrides,
  };
}

// Cria envelopes paginados sintéticos com contagem do conjunto e cobertura da mesma resposta.
function pageResponse(
  items: readonly Record<string, unknown>[] = [movement()],
  overrides: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    items,
    page: 1,
    pageSize: 25,
    filteredCount: items.length,
    coverage: coverage(),
    ...overrides,
  };
}

// Atualiza um controle nativo como uma pessoa usuária faria e emite o evento DOM correspondente.
function setControlValue(element: HTMLElement, selector: string, value: string): void {
  const control = element.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(
    selector,
  );
  if (control === null) {
    throw new Error(`Controle ausente no cenário: ${selector}`);
  }
  control.value = value;
  control.dispatchEvent(
    new Event(control.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }),
  );
}

// Garante lista descritiva, filtros e recuperação sem sincronização nem dados financeiros agregados.
describe('Página protegida de movimentos', () => {
  beforeEach(() => configurePage());

  afterEach(() => {
    fixture?.destroy();
    fixture = undefined;
    backend.expectNone((request) => request.url.toLowerCase().includes('/sync'));
    backend.verify({ ignoreCancelled: true });
  });

  // Natureza/situação desconhecidas, datas, parcelas e valores aparecem separados sem total ou zero inventado.
  it('apresenta movimentos individuais, campos ausentes e cobertura sem agregados', () => {
    const page = openPage();
    flushFilterOptions();
    expectTransactionRequest().flush(
      pageResponse(
        [
          movement({
            nature: 'Unknown',
            natureReason: 'UNKNOWN_OPERATION_TYPE',
            situation: 'Pending',
            originalDate: 'data original textual de origem',
            transactionDate: '2026-11-24',
          }),
        ],
        {
          filteredCount: 1,
          coverage: coverage({
            lastSuccessfulImportAtUtc: '2026-11-24T10:00:00Z',
            lastSuccessfulWindow: {
              requestedFrom: '2026-11-01',
              requestedToExclusive: null,
              observedFrom: '2026-11-02',
              observedTo: '2026-11-24',
              coverageState: 'Partial',
            },
            latestExecution: {
              state: 'Failed',
              coverageState: 'Unknown',
              failureCode: 'PRIVATE_CODE',
            },
          }),
        },
      ),
    );
    page.detectChanges();

    const rendered = page.nativeElement as HTMLElement;
    const article = rendered.querySelector('article');
    const itemText = article?.textContent ?? '';
    const categoryFilter = rendered.querySelector<HTMLSelectElement>('#movement-category-filter');

    expect(rendered.querySelectorAll('article')).toHaveLength(1);
    expect(itemText).toContain('R$');
    expect(itemText).toContain('Cartão principal');
    expect(itemText).toContain('data original textual de origem');
    expect(itemText).toContain('2026-11-24');
    expect(itemText).toContain('Natureza não reconhecida');
    expect(itemText).toContain('Motivo não reconhecido');
    expect(itemText).toContain('Pendente');
    expect(itemText).toContain('Não informadas');
    expect(itemText).toContain('Sem categoria');
    expect(itemText).not.toContain('UNKNOWN_OPERATION_TYPE');
    expect(itemText).not.toContain('Efetivado');
    expect(itemText).not.toContain('Não informado: 0');
    expect(categoryFilter?.textContent).not.toContain('Sem categoria');
    expect(rendered.textContent).toContain('1 movimento no conjunto filtrado');
    expect(rendered.textContent).toContain('cobertura Parcial');
    expect(rendered.textContent).toContain('Execução mais recente');
    expect(rendered.textContent).not.toContain('PRIVATE_CODE');
    expect(rendered.querySelectorAll('canvas, svg, table')).toHaveLength(0);
    expect(rendered.textContent).not.toMatch(/\b(?:saldo|soma|média|gráfico)\b/i);
  });

  // Os filtros da tela chegam juntos; uma página vazia com contagem positiva não vira conjunto vazio.
  it('aplica filtros combinados e distingue página vazia de filtro sem movimentos', async () => {
    const page = openPage();
    flushFilterOptions();
    expectTransactionRequest().flush(pageResponse([movement()], { filteredCount: 31 }));
    page.detectChanges();

    const rendered = page.nativeElement as HTMLElement;
    setControlValue(rendered, '#movement-card-filter', CARD_ID);
    expect(rendered.querySelector<HTMLSelectElement>('#movement-card-filter')?.value).toBe(CARD_ID);
    setControlValue(rendered, '#movement-category-filter', CATEGORY_ID);
    expect(rendered.querySelector<HTMLSelectElement>('#movement-category-filter')?.value).toBe(
      CATEGORY_ID,
    );
    setControlValue(rendered, '#movement-situation-filter', 'Pending');
    setControlValue(rendered, '#movement-from-filter', '2026-11-01');
    setControlValue(rendered, '#movement-to-filter', '2026-12-01');
    setControlValue(rendered, '#movement-search-filter', 'café 20%_literal');
    page.detectChanges();
    await page.whenStable();
    page.detectChanges();
    expect(page.componentInstance.draft().cardId).toBe(CARD_ID);
    expect(page.componentInstance.draft().categoryId).toBe(CATEGORY_ID);
    rendered.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();

    const filteredRequest = expectTransactionRequest();
    expect(filteredRequest.request.method).toBe('GET');
    expect(filteredRequest.request.params.get('cardId')).toBe(CARD_ID);
    expect(filteredRequest.request.params.get('categoryId')).toBe(CATEGORY_ID);
    expect(filteredRequest.request.params.get('situation')).toBe('Pending');
    expect(filteredRequest.request.params.get('from')).toBe('2026-11-01');
    expect(filteredRequest.request.params.get('toExclusive')).toBe('2026-12-01');
    expect(filteredRequest.request.params.get('search')).toBe('café 20%_literal');
    expect(filteredRequest.request.params.get('page')).toBe('1');
    filteredRequest.flush(
      pageResponse([movement({ situation: 'Pending' })], { filteredCount: 31 }),
    );
    page.detectChanges();

    rendered.querySelector<HTMLButtonElement>('button[aria-label="Próxima página"]')?.click();
    const nextPageRequest = expectTransactionRequest();
    expect(nextPageRequest.request.params.get('page')).toBe('2');
    expect(nextPageRequest.request.params.get('categoryId')).toBe(CATEGORY_ID);
    nextPageRequest.flush(pageResponse([], { page: 2, filteredCount: 31 }));
    page.detectChanges();

    expect(rendered.textContent).toContain('Não há movimentos nesta página');
    expect(rendered.textContent).toContain('conjunto filtrado contém 31 movimentos');
    expect(rendered.textContent).not.toContain('Não há movimentos persistidos para esta consulta');
    expect(
      rendered.querySelector('button[aria-label="Página anterior"]')?.hasAttribute('disabled'),
    ).toBe(false);
  });

  // Uma falha de rede identifica a resposta anterior; retry é acionado somente após ação explícita.
  it('mostra falha transitória, resposta anterior e recuperação somente por GET', () => {
    const page = openPage();
    flushFilterOptions();
    expectTransactionRequest().flush(pageResponse([movement()], { filteredCount: 1 }));
    page.detectChanges();

    (page.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('button[type="submit"]')
      ?.click();
    expectTransactionRequest().error(new ProgressEvent('error'));
    page.detectChanges();

    const rendered = page.nativeElement as HTMLElement;
    expect(rendered.textContent).toContain('Não foi possível conectar à API');
    expect(rendered.textContent).toContain('resposta confirmada anterior');
    expect(rendered.querySelectorAll('article')).toHaveLength(1);
    backend.expectNone((request) => request.url === TRANSACTIONS_URL);

    rendered
      .querySelector<HTMLButtonElement>('button[aria-label="Tentar consulta novamente"]')
      ?.click();
    const retryRequest = expectTransactionRequest();
    expect(retryRequest.request.method).toBe('GET');
    retryRequest.flush(pageResponse([], { filteredCount: 0 }));
    page.detectChanges();

    expect(rendered.querySelectorAll('article')).toHaveLength(0);
    expect(rendered.textContent).toContain('Não há movimentos persistidos para esta consulta');
    expect(rendered.textContent).toContain('Cobertura desconhecida');
    expect(rendered.textContent).toContain('não confirma ausência de movimentos na origem');
  });

  // Categoria nova só entra nas opções após 201; a associação é confirmada por PATCH e nova leitura.
  it('cria categoria pessoal e aplica a opção confirmada sem recarregar a rota', () => {
    const page = openPage();
    flushFilterOptions();
    expectTransactionRequest().flush(pageResponse([movement({ note: 'Nota preservada' })]));
    page.detectChanges();

    const rendered = page.nativeElement as HTMLElement;
    setControlValue(rendered, '#personal-category-name', 'Viagens');
    rendered
      .querySelector<HTMLFormElement>('#personal-category-form')
      ?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

    const createRequest = backend.expectOne(CATEGORIES_URL);
    expect(createRequest.request.method).toBe('POST');
    expect(createRequest.request.body).toEqual({ name: 'Viagens' });
    createRequest.flush(
      { id: CREATED_CATEGORY_ID, name: 'Viagens', kind: 'personal' },
      { status: 201, statusText: 'Created' },
    );
    page.detectChanges();

    const editor = rendered.querySelector<HTMLSelectElement>('[data-category-editor]');
    expect(
      page.componentInstance
        .categoryOptions()
        .some((category) => category.id === CREATED_CATEGORY_ID),
    ).toBe(true);
    expect(Array.from(editor?.options ?? []).map((option) => option.textContent?.trim())).toContain(
      'Viagens',
    );
    expect(Array.from(editor?.options ?? []).map((option) => option.textContent?.trim())).toContain(
      'Sem categoria',
    );
    expect(page.componentInstance.categoryCreationStatus()).toBe('saved');

    setControlValue(rendered, `#movement-category-editor-${MOVEMENT_ID}`, CREATED_CATEGORY_ID);
    page.detectChanges();
    rendered.querySelector<HTMLButtonElement>(`#save-movement-category-${MOVEMENT_ID}`)?.click();
    const patchRequest = backend.expectOne(`${TRANSACTIONS_URL}/${MOVEMENT_ID}/annotation`);
    expect(patchRequest.request.method).toBe('PATCH');
    expect(patchRequest.request.body).toEqual({ categoryId: CREATED_CATEGORY_ID });
    patchRequest.flush(
      { categoryId: CREATED_CATEGORY_ID, note: 'Nota preservada' },
      { status: 200, statusText: 'OK' },
    );
    page.detectChanges();

    expect(rendered.textContent).toContain('Categoria salva; atualizando movimentos e contagens.');
    const rereadRequest = expectTransactionRequest();
    expect(rereadRequest.request.method).toBe('GET');
    rereadRequest.flush(
      pageResponse([movement({ categoryId: CREATED_CATEGORY_ID, note: 'Nota preservada' })]),
    );
    page.detectChanges();

    expect(rendered.querySelector<HTMLSelectElement>('[data-category-editor]')?.value).toBe(
      CREATED_CATEGORY_ID,
    );
    expect(rendered.textContent).toContain('Categoria confirmada');
    expect(page.componentInstance.categoryLabel(CREATED_CATEGORY_ID)).toBe('Viagens');
    expect(rendered.textContent).toContain('Nota preservada');
  });

  // Conflito de nome é distinto de rejeição geral; nenhum erro adiciona opção não confirmada ao catálogo.
  it.each([
    { status: 400, statusText: 'Bad Request', expectedMessage: 'A API recusou a criação' },
    { status: 409, statusText: 'Conflict', expectedMessage: 'Já existe uma categoria' },
  ])(
    'não apresenta categoria não confirmada após resposta $status',
    ({ status, statusText, expectedMessage }) => {
      const page = openPage();
      flushFilterOptions();
      expectTransactionRequest().flush(pageResponse([movement()]));
      page.detectChanges();

      const rendered = page.nativeElement as HTMLElement;
      setControlValue(rendered, '#personal-category-name', 'Viagens');
      rendered
        .querySelector<HTMLFormElement>('#personal-category-form')
        ?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      backend
        .expectOne(CATEGORIES_URL)
        .flush(
          { detail: 'synthetic-private-error', traceId: 'synthetic-private-trace' },
          { status, statusText },
        );
      page.detectChanges();

      const editor = rendered.querySelector<HTMLSelectElement>('[data-category-editor]');
      expect(rendered.textContent).toContain(expectedMessage);
      expect(rendered.textContent).not.toContain('synthetic-private-error');
      expect(
        Array.from(editor?.options ?? []).map((option) => option.textContent?.trim()),
      ).not.toContain('Viagens');
      expect(
        page.componentInstance.categoryOptions().some((category) => category.name === 'Viagens'),
      ).toBe(false);
    },
  );

  // Falha de catálogo não fabrica “Sem categoria” nem habilita edição com opções que não foram recebidas.
  it('não cria opções virtuais quando o catálogo de categorias está indisponível', () => {
    const page = openPage();
    backend
      .expectOne(CARDS_URL)
      .flush([{ id: CARD_ID, name: 'Cartão principal', lastFour: '1234', currencyCode: 'BRL' }]);
    backend
      .expectOne(CATEGORIES_URL)
      .flush(
        { detail: 'synthetic-private-error' },
        { status: 503, statusText: 'Service Unavailable' },
      );
    expectTransactionRequest().flush(pageResponse([movement()]));
    page.detectChanges();

    const rendered = page.nativeElement as HTMLElement;
    const editor = rendered.querySelector<HTMLSelectElement>('[data-category-editor]');
    expect(page.componentInstance.categoriesStatus()).toBe('error');
    expect(page.componentInstance.categoryOptions()).toEqual([]);
    expect(
      Array.from(editor?.options ?? []).map((option) => option.textContent?.trim()),
    ).not.toContain('Sem categoria');
    expect(editor?.disabled).toBe(true);
    expect(rendered.textContent).toContain('Não foi possível carregar todas as opções de filtro');
  });

  // O PATCH de observação envia somente o campo editado, aceita 500 caracteres e relê fatos atuais.
  it('salva observação de até 500 caracteres e mostra os fatos retornados na releitura', () => {
    const page = openPage();
    flushFilterOptions();
    expectTransactionRequest().flush(
      pageResponse([
        movement({
          description: 'Descrição anterior',
          note: 'Nota anterior',
          categoryId: CATEGORY_ID,
        }),
      ]),
    );
    page.detectChanges();

    const rendered = page.nativeElement as HTMLElement;
    const note = 'T'.repeat(500);
    expect(note.length).toBe(500);
    setControlValue(rendered, `#movement-note-editor-${MOVEMENT_ID}`, note);
    page.detectChanges();
    expect(
      rendered.querySelector<HTMLTextAreaElement>(`#movement-note-editor-${MOVEMENT_ID}`)
        ?.maxLength,
    ).toBe(500);
    expect(
      rendered.querySelector<HTMLButtonElement>(`#save-movement-note-${MOVEMENT_ID}`)?.disabled,
    ).toBe(false);

    rendered.querySelector<HTMLButtonElement>(`#save-movement-note-${MOVEMENT_ID}`)?.click();
    const patchRequest = backend.expectOne(`${TRANSACTIONS_URL}/${MOVEMENT_ID}/annotation`);
    expect(patchRequest.request.method).toBe('PATCH');
    expect(patchRequest.request.body).toEqual({ note });
    expect(Object.keys(patchRequest.request.body)).toEqual(['note']);
    patchRequest.flush({ categoryId: CATEGORY_ID, note }, { status: 200, statusText: 'OK' });
    page.detectChanges();

    expect(rendered.textContent).toContain('Observação salva; atualizando movimentos e contagens.');
    const rereadRequest = expectTransactionRequest();
    rereadRequest.flush(
      pageResponse([
        movement({
          description: 'Descrição de origem atualizada',
          amount: 21.5,
          originalDate: 'data original atualizada',
          categoryId: CATEGORY_ID,
          note,
        }),
      ]),
    );
    page.detectChanges();

    expect(rendered.textContent).toContain('Descrição de origem atualizada');
    expect(rendered.textContent).toContain('data original atualizada');
    expect(rendered.textContent).toContain(note);
    expect(rendered.textContent).toContain('Pessoal');
  });

  // Campo vazio limpa a observação por null explícito e preserva a categoria retornada pela API.
  it('limpa observação sem enviar nem modificar a categoria', () => {
    const page = openPage();
    flushFilterOptions();
    expectTransactionRequest().flush(
      pageResponse([movement({ categoryId: CATEGORY_ID, note: 'Nota para limpar' })]),
    );
    page.detectChanges();

    const rendered = page.nativeElement as HTMLElement;
    setControlValue(rendered, `#movement-note-editor-${MOVEMENT_ID}`, '');
    page.detectChanges();
    rendered.querySelector<HTMLButtonElement>(`#save-movement-note-${MOVEMENT_ID}`)?.click();

    const patchRequest = backend.expectOne(`${TRANSACTIONS_URL}/${MOVEMENT_ID}/annotation`);
    expect(patchRequest.request.body).toEqual({ note: null });
    patchRequest.flush({ categoryId: CATEGORY_ID, note: null }, { status: 200, statusText: 'OK' });
    const rereadRequest = expectTransactionRequest();
    rereadRequest.flush(pageResponse([movement({ categoryId: CATEGORY_ID, note: null })]));
    page.detectChanges();

    expect(
      rendered.querySelector<HTMLTextAreaElement>(`#movement-note-editor-${MOVEMENT_ID}`)?.value,
    ).toBe('');
    expect(rendered.textContent).toContain('Sem observação');
    expect(rendered.textContent).toContain('Pessoal');
    expect(patchRequest.request.body).not.toHaveProperty('categoryId');
  });

  // Uma alteração que remove o movimento do filtro conserva o filtro aplicado e usa nova contagem da API.
  it('reconsulta o filtro e distingue lista vazia após remover a categoria', () => {
    const page = openPage();
    flushFilterOptions();
    expectTransactionRequest().flush(pageResponse([movement({ categoryId: CATEGORY_ID })]));
    page.detectChanges();

    const rendered = page.nativeElement as HTMLElement;
    setControlValue(rendered, '#movement-category-filter', CATEGORY_ID);
    rendered.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
    const filteredRequest = expectTransactionRequest();
    expect(filteredRequest.request.params.get('categoryId')).toBe(CATEGORY_ID);
    filteredRequest.flush(pageResponse([movement({ categoryId: CATEGORY_ID })]));
    page.detectChanges();

    setControlValue(rendered, `[data-category-editor]`, page.componentInstance.noCategorySelection);
    page.detectChanges();
    rendered.querySelector<HTMLButtonElement>(`#save-movement-category-${MOVEMENT_ID}`)?.click();
    const patchRequest = backend.expectOne(`${TRANSACTIONS_URL}/${MOVEMENT_ID}/annotation`);
    expect(patchRequest.request.body).toEqual({ categoryId: null });
    patchRequest.flush({ categoryId: null, note: null }, { status: 200, statusText: 'OK' });

    const rereadRequest = expectTransactionRequest();
    expect(rereadRequest.request.params.get('categoryId')).toBe(CATEGORY_ID);
    rereadRequest.flush(pageResponse([], { filteredCount: 0 }));
    page.detectChanges();

    expect(rendered.querySelectorAll('article')).toHaveLength(0);
    expect(rendered.textContent).toContain('Não há movimentos persistidos para esta consulta');
    expect(rendered.textContent).toContain('0 movimentos no conjunto filtrado');
  });

  // Erro 404 preserva a anotação apresentada e não ecoa corpo remoto nem marca o rascunho como salvo.
  it('mantém a anotação anterior quando o PATCH é recusado com 404', () => {
    const page = openPage();
    flushFilterOptions();
    expectTransactionRequest().flush(pageResponse([movement({ note: 'Nota persistida' })]));
    page.detectChanges();

    const rendered = page.nativeElement as HTMLElement;
    setControlValue(rendered, `#movement-note-editor-${MOVEMENT_ID}`, 'Nota não confirmada');
    page.detectChanges();
    rendered.querySelector<HTMLButtonElement>(`#save-movement-note-${MOVEMENT_ID}`)?.click();
    backend
      .expectOne(`${TRANSACTIONS_URL}/${MOVEMENT_ID}/annotation`)
      .flush({ detail: 'synthetic-private-error' }, { status: 404, statusText: 'Not Found' });
    page.detectChanges();

    expect(rendered.textContent).toContain('O recurso necessário não está mais disponível');
    expect(rendered.textContent).not.toContain('synthetic-private-error');
    expect(rendered.querySelector('dd.whitespace-pre-wrap')?.textContent).toContain(
      'Nota persistida',
    );
    expect(
      rendered.querySelector<HTMLTextAreaElement>(`#movement-note-editor-${MOVEMENT_ID}`)?.value,
    ).toBe('Nota não confirmada');
    backend.expectNone((request) => request.url === TRANSACTIONS_URL);
  });

  // A validação local impede PATCHs acima do limite mesmo quando o evento é produzido fora do navegador.
  it('recusa rascunho de observação acima de 500 caracteres antes do PATCH', () => {
    const page = openPage();
    flushFilterOptions();
    expectTransactionRequest().flush(pageResponse([movement()]));
    page.detectChanges();

    const rendered = page.nativeElement as HTMLElement;
    setControlValue(rendered, `#movement-note-editor-${MOVEMENT_ID}`, 'x'.repeat(501));
    page.detectChanges();

    expect(
      rendered.querySelector<HTMLButtonElement>(`#save-movement-note-${MOVEMENT_ID}`)?.disabled,
    ).toBe(true);
    expect(rendered.textContent).toContain('A observação deve ter no máximo 500 caracteres');
    backend.expectNone((request) => request.url.endsWith('/annotation'));
  });

  // Opções privadas e movimentos ficam invisíveis assim que a geração local termina.
  it('redige snapshots de cartões, categorias e movimentos no logout', () => {
    const page = openPage();
    flushFilterOptions();
    expectTransactionRequest().flush(pageResponse());
    const session = TestBed.inject(SessionService);
    const generation = session.currentSession()!.generation;

    session.endSessionForGeneration(generation);

    expect(page.componentInstance.cardOptions()).toEqual([]);
    expect(page.componentInstance.categoryOptions()).toEqual([]);
    expect(page.componentInstance.state().snapshot).toBeNull();
  });

  // Nomes, foco, envio nativo por Enter e classes responsivas mantêm inclusive os novos editores acessíveis.
  it('mantém controles acessíveis por teclado e dados longos legíveis em layouts responsivos', () => {
    const page = openPage();
    flushFilterOptions();
    const description = 'Descrição sem espaço '.repeat(20);
    const note = 'Observação extensa '.repeat(20);
    expectTransactionRequest().flush(
      pageResponse([movement({ description, note })], { filteredCount: 26 }),
    );
    page.detectChanges();

    const rendered = page.nativeElement as HTMLElement;
    const form = rendered.querySelector<HTMLFormElement>('form');
    const controls = Array.from(
      rendered.querySelectorAll<
        HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | HTMLButtonElement
      >('input, select, textarea, button'),
    ).filter((control) => !control.disabled);
    const article = rendered.querySelector('article');
    const title = article?.querySelector('h3');
    const noteValue = article?.querySelector('dd.whitespace-pre-wrap');

    expect(rendered.querySelector('h1')?.textContent).toContain('Movimentos');
    expect(form?.className).toContain('sm:grid-cols-2');
    expect(form?.className).toContain('lg:grid-cols-3');
    expect(article?.querySelector('dl')?.className).toContain('min-w-0');
    expect(title?.className).toContain('break-words');
    expect(noteValue?.textContent).toContain(note);
    expect(title?.textContent).toContain(description);
    expect(article?.className).not.toMatch(/overflow-hidden|truncate|line-clamp|whitespace-nowrap/);

    for (const control of controls) {
      const label = control.id
        ? rendered.querySelector<HTMLLabelElement>(`label[for="${control.id}"]`)?.textContent
        : null;
      if (
        control.tagName === 'INPUT' ||
        control.tagName === 'SELECT' ||
        control.tagName === 'TEXTAREA'
      ) {
        expect(label?.trim().length).toBeGreaterThan(0);
      }
      const accessibleName = control.getAttribute('aria-label') ?? label ?? control.textContent;
      expect(accessibleName?.trim().length).toBeGreaterThan(0);
      expect(control.tabIndex).toBeGreaterThanOrEqual(0);
      expect(control.className).toContain('focus-visible:outline-3');
      control.focus();
      expect(document.activeElement).toBe(control);
    }

    form?.requestSubmit();
    const keyboardSubmitRequest = expectTransactionRequest();
    expect(keyboardSubmitRequest.request.method).toBe('GET');
    keyboardSubmitRequest.flush(pageResponse([movement()], { filteredCount: 1 }));
    page.detectChanges();
  });
});
