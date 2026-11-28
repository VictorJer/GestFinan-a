import { HttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, RouterOutlet } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { App } from './app';
import { appConfig } from './app.config';
import { API_BASE_URL } from './api-base-url.token';
import { LoginPage } from './auth/login/login';
import { SessionService } from './auth/session';
import { routes } from './app.routes';
import { HomePage } from './home/home';
import { NotFoundPage } from './not-found/not-found';

const API_BASE = 'http://127.0.0.1:5080/api';
const SYNTHETIC_INVESTMENT_ID = '87b65749-c927-431f-8d51-35fdf3106336';

// Mantém a resposta sintética no contrato allowlisted para testar navegação sem dados reais.
function dashboardSummaryResponse(): Record<string, unknown> {
  return {
    filteredCount: 0,
    countsByNature: [
      { nature: 'Purchase', count: 0 },
      { nature: 'Refund', count: 0 },
      { nature: 'BillPayment', count: 0 },
      { nature: 'Fee', count: 0 },
      { nature: 'Adjustment', count: 0 },
      { nature: 'Unknown', count: 0 },
    ],
    countsBySituation: [
      { situation: 'Effective', count: 0 },
      { situation: 'Pending', count: 0 },
      { situation: 'FutureInstallment', count: 0 },
      { situation: 'Unknown', count: 0 },
    ],
    coverage: {
      lastSuccessfulImportAtUtc: null,
      lastSuccessfulWindow: null,
      latestExecution: null,
    },
    dateBasis: 'TransactionDate',
  };
}

// A carteira vazia usa somente strings BRL do contrato, inclusive em classes sem posições.
function emptyInvestmentPortfolioResponse(): Record<string, unknown> {
  return {
    items: [],
    summary: {
      totalAppliedAmount: '0.00',
      totalCurrentAmount: '0.00',
      difference: '0.00',
      currentAmountsByClass: [
        { class: 'fixed_income', label: 'Renda fixa', currentAmount: '0.00' },
        { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
        { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
      ],
    },
  };
}

// Mantém o item HTTP allowlisted e valores BRL canônicos nas respostas sintéticas da jornada.
function investmentItemResponse(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: SYNTHETIC_INVESTMENT_ID,
    name: 'Reserva da jornada',
    class: 'fixed_income',
    appliedAmount: '120.50',
    currentAmount: '125.75',
    ...overrides,
  };
}

// O resumo permanece uma resposta do serviço sintético, nunca calculada pela página Angular.
function investmentPortfolioResponse(
  item: Record<string, unknown>,
  summary: {
    readonly totalAppliedAmount: string;
    readonly totalCurrentAmount: string;
    readonly difference: string;
    readonly fixedIncome: string;
    readonly variableIncome: string;
  },
): Record<string, unknown> {
  return {
    items: [item],
    summary: {
      totalAppliedAmount: summary.totalAppliedAmount,
      totalCurrentAmount: summary.totalCurrentAmount,
      difference: summary.difference,
      currentAmountsByClass: [
        { class: 'fixed_income', label: 'Renda fixa', currentAmount: summary.fixedIncome },
        {
          class: 'variable_income',
          label: 'Renda variável',
          currentAmount: summary.variableIncome,
        },
        { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
      ],
    },
  };
}

// Responde às três chamadas permitidas ao abrir /area sem depender de uma API real.
function flushAuthenticatedAreaStartup(http: HttpTestingController): void {
  const cards = http.expectOne(`${API_BASE}/cards`);
  const syncState = http.expectOne(`${API_BASE}/connections/current/sync-state`);
  const onEntry = http.expectOne(`${API_BASE}/connections/current/sync/on-entry`);

  expect(cards.request.method).toBe('GET');
  expect(syncState.request.method).toBe('GET');
  expect(onEntry.request.method).toBe('POST');
  expect(cards.request.headers.get('Authorization')).toMatch(/^Bearer /);
  expect(syncState.request.headers.get('Authorization')).toMatch(/^Bearer /);
  expect(onEntry.request.headers.get('Authorization')).toMatch(/^Bearer /);
  cards.flush([]);
  syncState.flush({
    updateNeeded: false,
    reauthorizationRequired: false,
    lastSuccessfulImportAtUtc: null,
    sourceUpdatedAtUtc: null,
    lastAttemptAtUtc: null,
    nextAllowedAtUtc: null,
    latestExecution: null,
  });
  onEntry.flush({ decision: 'not_required' });
}

// Exercita o shell real com HTTP interceptado para detectar chamadas acidentais ao montar páginas.
describe('Página inicial pública', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [...(appConfig.providers ?? []), provideHttpClientTesting()],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  // Confirma que a página identificável e seu texto de preparação renderizam sem backend.
  it('exibe a marca e o estado de preparação sem API ou sessão', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/', HomePage);
    const page = harness.routeNativeElement as HTMLElement;

    expect(page.querySelector('section[aria-labelledby="home-title"]')).not.toBeNull();
    expect(page.querySelector('h1')?.textContent).toContain('O Finora está sendo preparado');
    expect(page.textContent).toContain('Em preparação');
  });

  // Impede que exemplos, valores agregados ou estados de conexão apareçam como dados reais.
  it('não apresenta valores ou estados financeiros demonstrativos', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/', HomePage);
    const text = harness.routeNativeElement?.textContent?.toLocaleLowerCase('pt-BR') ?? '';

    expect(text).not.toMatch(/r\$\s*[\d.,]+/);
    expect(text).not.toMatch(/\b\d{1,3}(?:\.\d{3})*(?:,\d{2})\b/);
    expect(text).not.toMatch(/\b(?:saldo|receita|gasto|fatura|conectado|sincronizado)\b/);
  });

  // Confirma o retorno ao início e o indicador de foco do link da página não encontrada.
  it('mostra 404 para rota desconhecida e permite retornar ao início', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/pagina-inexistente', NotFoundPage);
    const page = harness.routeNativeElement;
    const returnLink = page?.querySelector<HTMLAnchorElement>('a[href="/"]');
    const links = Array.from(page?.querySelectorAll<HTMLAnchorElement>('a') ?? []);

    expect(page?.querySelector('h1')?.textContent).toContain('Esta página não existe');
    expect(returnLink?.textContent).toContain('Voltar ao início');
    expect(returnLink?.classList.contains('focus-visible:outline-solid')).toBe(true);
    expect(returnLink?.classList.contains('outline-finora-brand')).toBe(true);
    expect(links.map((link) => link.getAttribute('href'))).toEqual(['/']);
    expect(page?.querySelectorAll('button, form')).toHaveLength(0);
    await harness.navigateByUrl('/', HomePage);
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
      'O Finora está sendo preparado',
    );
  });

  // O guard redireciona acesso direto sem sessão sem consultar contagens nem cobertura privadas.
  it('encaminha acesso anônimo à visão para login sem iniciar requests', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    await TestBed.inject(Router).navigateByUrl('/area/visao');
    await fixture.whenStable();
    fixture.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(fixture.nativeElement.textContent).toContain('Entrar no Finora');
    TestBed.inject(HttpTestingController).expectNone(() => true);
    fixture.destroy();
  });

  // A rota autenticada é independente de /area e consulta somente GET /api/dashboard ao entrar.
  it('abre diretamente a visão protegida sem avaliação automática ou importação', async () => {
    TestBed.inject(SessionService).startSession({
      accessToken: 'synthetic-overview-route-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/area/visao');
    await fixture.whenStable();
    fixture.detectChanges();
    const dashboard = TestBed.inject(HttpTestingController).expectOne(`${API_BASE}/dashboard`);

    expect(router.url).toBe('/area/visao');
    expect(dashboard.request.method).toBe('GET');
    expect(dashboard.request.headers.get('Authorization')).toMatch(/^Bearer /);
    dashboard.flush(dashboardSummaryResponse());
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Visão descritiva');
    expect(
      fixture.nativeElement.querySelector('app-dashboard-overview-page a[href="/area/movimentos"]')
        ?.textContent,
    ).toContain('Abrir lista de movimentos');
    TestBed.inject(HttpTestingController).expectNone(
      (request) =>
        request.url.toLowerCase().includes('/sync') ||
        request.url.endsWith('/cards') ||
        request.url.includes('/connections/') ||
        request.url === `${API_BASE}/transactions`,
    );
    // Abrir a visão só pode consultar o resumo; nenhuma navegação da página inicia atualização.
    TestBed.inject(HttpTestingController).expectNone((request) => request.method === 'POST');
    fixture.destroy();
  });

  // Mantém só início/login no menu público e confirma ausência de chamadas financeiras nas duas rotas.
  it('vincula a navegação pública às rotas entregues', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    fixture.detectChanges();
    const shell = fixture.nativeElement as HTMLElement;
    const navigation = Array.from(
      shell.querySelectorAll<HTMLAnchorElement>('nav[aria-label="Navegação principal"] a'),
    );
    const linkTargets = Array.from(shell.querySelectorAll<HTMLAnchorElement>('a')).map((link) =>
      link.getAttribute('href'),
    );
    const unavailablePaths = [
      '/dashboard',
      '/card',
      '/transactions',
      '/area/movimentos',
      '/area/visao',
      '/investments',
      '/simulator',
      '/simulador',
      '/area/simulador',
      '/area/csv',
      '/area/exportar',
      '/sync',
    ];

    expect(routes.map((route) => route.path)).toEqual([
      '',
      'login',
      'area',
      'area/movimentos',
      'area/visao',
      'area/investimentos',
      '**',
    ]);
    expect(navigation.map((link) => link.getAttribute('href'))).toEqual([
      '/',
      '/login',
      '/',
      '/login',
    ]);
    expect(unavailablePaths.some((path) => linkTargets.includes(path))).toBe(false);
    expect(navigation.every((link) => link.classList.contains('focus-visible:outline-solid'))).toBe(
      true,
    );
    expect(navigation.every((link) => !link.classList.contains('outline-none'))).toBe(true);
    expect(navigation[0]?.classList.contains('outline-finora-focus')).toBe(true);
    expect(navigation[2]?.classList.contains('outline-finora-brand')).toBe(true);
    expect(shell.querySelector('aside')?.classList.contains('mobile:flex')).toBe(true);
    expect(shell.querySelector('nav.fixed')?.classList.contains('mobile:hidden')).toBe(true);
    expect(shell.querySelectorAll('button, form, input, select, textarea')).toHaveLength(0);
    expect(shell.querySelector('header')).not.toBeNull();
    expect(shell.querySelector('header')?.textContent).toContain('Aplicação pública');
    expect(shell.querySelector('main#conteudo[tabindex="-1"]')).not.toBeNull();
    expect(shell.querySelector('a[href="#conteudo"]')?.textContent).toContain(
      'Pular para o conteúdo',
    );
    expect(shell.querySelectorAll('nav a[aria-current="page"]')).toHaveLength(2);
    expect(shell.textContent).toContain('Entrar');
    TestBed.inject(HttpTestingController).expectNone(() => true);

    await TestBed.inject(Router).navigateByUrl('/login');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(shell.textContent).toContain('Entrar no Finora');
    TestBed.inject(HttpTestingController).expectNone(() => true);
    fixture.destroy();
  });

  // Reflete sessão ativa nos dois menus e nunca inclui o token em markup ou URL.
  it('vincula a navegação autenticada à área e à saída', async () => {
    TestBed.inject(SessionService).startSession({
      accessToken: 'synthetic-shell-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    fixture.detectChanges();
    const shell = fixture.nativeElement as HTMLElement;
    const navigation = Array.from(
      shell.querySelectorAll<HTMLAnchorElement>('nav[aria-label="Navegação principal"] a'),
    );
    const linkTargets = Array.from(shell.querySelectorAll<HTMLAnchorElement>('a')).map((link) =>
      link.getAttribute('href'),
    );

    expect(navigation.map((link) => link.getAttribute('href'))).toEqual([
      '/',
      '/area',
      '/area/movimentos',
      '/area/visao',
      '/area/investimentos',
      '/',
      '/area',
      '/area/movimentos',
      '/area/visao',
      '/area/investimentos',
    ]);
    expect(navigation.map((link) => link.getAttribute('aria-label'))).toEqual([
      'Início',
      'Cartões e conexão',
      'Movimentos',
      'Visão descritiva',
      'Carteira de investimentos',
      'Início',
      'Cartões e conexão',
      'Movimentos',
      'Visão descritiva',
      'Carteira de investimentos',
    ]);
    expect(navigation[1]?.textContent).toContain('Cartões e conexão');
    expect(navigation[2]?.textContent).toContain('Movimentos');
    expect(navigation[3]?.textContent).toContain('Visão descritiva');
    expect(navigation[4]?.textContent).toContain('Investimentos');
    expect(navigation[6]?.textContent).toContain('Cartões');
    expect(navigation[7]?.textContent).toContain('Movimentos');
    expect(navigation[8]?.textContent).toContain('Visão');
    expect(navigation[9]?.textContent).toContain('Carteira');
    expect(shell.querySelector('app-home a[href="/area"]')?.textContent).toContain(
      'Abrir cartões e conexão',
    );
    expect(shell.querySelectorAll('button[aria-label="Sair da sessão"]')).toHaveLength(1);
    expect(shell.querySelectorAll('nav button')).toHaveLength(2);
    expect(shell.querySelector('header')?.textContent).toContain('Sessão autenticada');
    expect(shell.textContent).toContain('Sair');
    expect(linkTargets.some((path) => path?.includes('synthetic-shell-token'))).toBe(false);
    expect(shell.innerHTML).not.toContain('synthetic-shell-token');
    expect(TestBed.inject(Router).url).not.toContain('synthetic-shell-token');
    expect(
      linkTargets
        .filter((path) => path?.startsWith('/'))
        .every((path) =>
          [
            '/',
            '/area',
            '/area/movimentos',
            '/area/visao',
            '/area/investimentos',
            '/login',
          ].includes(path ?? ''),
        ),
    ).toBe(true);
    TestBed.inject(HttpTestingController).expectNone(() => true);
    fixture.destroy();
  });

  // Links desktop e mobile alcançam a carteira e cada entrada emite somente GET /api/investments.
  it('abre a carteira pelos menus desktop e mobile sem avaliar ou sincronizar cartões', async () => {
    TestBed.inject(SessionService).startSession({
      accessToken: 'synthetic-portfolio-navigation-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    const http = TestBed.inject(HttpTestingController);
    const shell = fixture.nativeElement as HTMLElement;
    await router.navigateByUrl('/');
    await fixture.whenStable();
    fixture.detectChanges();

    const desktopLink = shell.querySelector<HTMLAnchorElement>(
      'aside nav a[href="/area/investimentos"]',
    )!;
    expect(desktopLink).not.toBeNull();
    desktopLink.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/area/investimentos');
    expect(desktopLink.getAttribute('aria-current')).toBe('page');
    const desktopRead = http.expectOne(`${API_BASE}/investments`);
    expect(desktopRead.request.method).toBe('GET');
    expect(desktopRead.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-portfolio-navigation-token',
    );
    http.expectNone(() => true);
    desktopRead.flush(emptyInvestmentPortfolioResponse());
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('A carteira está vazia');

    await router.navigateByUrl('/');
    await fixture.whenStable();
    fixture.detectChanges();
    const mobileLink = shell.querySelector<HTMLAnchorElement>(
      'nav.fixed a[href="/area/investimentos"]',
    )!;
    expect(mobileLink).not.toBeNull();
    expect(mobileLink.textContent).toContain('Carteira');
    mobileLink.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/area/investimentos');
    expect(mobileLink.getAttribute('aria-current')).toBe('page');
    const mobileRead = http.expectOne(`${API_BASE}/investments`);
    expect(mobileRead.request.method).toBe('GET');
    http.expectNone(() => true);
    mobileRead.flush(emptyInvestmentPortfolioResponse());
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('A carteira está vazia');
    http.expectNone(() => true);
    fixture.destroy();
  });

  // Percorre CRUD pela rota real Angular; a saída/volta simula recarga visual com respostas HTTP sintéticas.
  it('percorre cadastro, resumo, edição, releitura da rota e exclusão autenticados', async () => {
    TestBed.inject(SessionService).startSession({
      accessToken: 'synthetic-investment-crud-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const shell = fixture.nativeElement as HTMLElement;
    const router = TestBed.inject(Router);
    const http = TestBed.inject(HttpTestingController);
    await router.navigateByUrl('/area/investimentos');
    await fixture.whenStable();
    fixture.detectChanges();

    // A primeira leitura é protegida e publica somente o estado vazio devolvido pelo serviço.
    const firstRead = http.expectOne(`${API_BASE}/investments`);
    expect(firstRead.request.method).toBe('GET');
    expect(firstRead.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-investment-crud-token',
    );
    firstRead.flush(emptyInvestmentPortfolioResponse());
    await fixture.whenStable();
    fixture.detectChanges();
    expect(shell.textContent).toContain('A carteira está vazia');
    expect(shell.textContent).toContain('R$\u00a00,00');

    // O formulário envia os valores decimais exatamente como texto e a resposta 201 exige releitura.
    const name = shell.querySelector<HTMLInputElement>('#investment-name')!;
    const investmentClass = shell.querySelector<HTMLSelectElement>('#investment-class')!;
    const appliedAmount = shell.querySelector<HTMLInputElement>('#investment-applied-amount')!;
    const currentAmount = shell.querySelector<HTMLInputElement>('#investment-current-amount')!;
    name.value = 'Reserva da jornada';
    name.dispatchEvent(new Event('input', { bubbles: true }));
    investmentClass.value = 'fixed_income';
    investmentClass.dispatchEvent(new Event('change', { bubbles: true }));
    appliedAmount.value = '120.50';
    appliedAmount.dispatchEvent(new Event('input', { bubbles: true }));
    currentAmount.value = '125.75';
    currentAmount.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    shell.querySelector<HTMLFormElement>('form')!.requestSubmit();
    fixture.detectChanges();

    const createRequest = http.expectOne(`${API_BASE}/investments`);
    expect(createRequest.request.method).toBe('POST');
    expect(createRequest.request.body).toEqual({
      name: 'Reserva da jornada',
      class: 'fixed_income',
      appliedAmount: '120.50',
      currentAmount: '125.75',
    });
    expect(createRequest.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-investment-crud-token',
    );
    createRequest.flush(investmentItemResponse(), { status: 201, statusText: 'Created' });
    const afterCreateRead = http.expectOne(`${API_BASE}/investments`);
    expect(afterCreateRead.request.method).toBe('GET');
    afterCreateRead.flush(
      investmentPortfolioResponse(investmentItemResponse(), {
        totalAppliedAmount: '120.50',
        totalCurrentAmount: '125.75',
        difference: '5.25',
        fixedIncome: '125.75',
        variableIncome: '0.00',
      }),
    );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(shell.textContent).toContain('Reserva da jornada');
    expect(shell.textContent).toContain('R$\u00a0120,50');
    expect(shell.textContent).toContain('R$\u00a0125,75');
    expect(shell.textContent).toContain('R$\u00a05,25');

    // Edição mantém o ID na rota, altera a classe e só atualiza a tela após PUT e novo GET.
    shell.querySelector<HTMLButtonElement>('[aria-label="Editar Reserva da jornada"]')!.click();
    fixture.detectChanges();
    expect(shell.querySelector<HTMLInputElement>('#investment-applied-amount')?.value).toBe(
      '120.50',
    );
    expect(shell.querySelector<HTMLInputElement>('#investment-current-amount')?.value).toBe(
      '125.75',
    );
    const revisedName = shell.querySelector<HTMLInputElement>('#investment-name')!;
    revisedName.value = 'Reserva revisada';
    revisedName.dispatchEvent(new Event('input', { bubbles: true }));
    const revisedClass = shell.querySelector<HTMLSelectElement>('#investment-class')!;
    revisedClass.value = 'variable_income';
    revisedClass.dispatchEvent(new Event('change', { bubbles: true }));
    const revisedCurrentAmount = shell.querySelector<HTMLInputElement>(
      '#investment-current-amount',
    )!;
    revisedCurrentAmount.value = '150.00';
    revisedCurrentAmount.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    shell.querySelector<HTMLFormElement>('form')!.requestSubmit();
    fixture.detectChanges();

    const updateRequest = http.expectOne(`${API_BASE}/investments/${SYNTHETIC_INVESTMENT_ID}`);
    expect(updateRequest.request.method).toBe('PUT');
    expect(updateRequest.request.body).toEqual({
      name: 'Reserva revisada',
      class: 'variable_income',
      appliedAmount: '120.50',
      currentAmount: '150.00',
    });
    updateRequest.flush(
      investmentItemResponse({
        name: 'Reserva revisada',
        class: 'variable_income',
        currentAmount: '150.00',
      }),
    );
    const afterUpdateRead = http.expectOne(`${API_BASE}/investments`);
    afterUpdateRead.flush(
      investmentPortfolioResponse(
        investmentItemResponse({
          name: 'Reserva revisada',
          class: 'variable_income',
          currentAmount: '150.00',
        }),
        {
          totalAppliedAmount: '120.50',
          totalCurrentAmount: '150.00',
          difference: '29.50',
          fixedIncome: '0.00',
          variableIncome: '150.00',
        },
      ),
    );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(shell.textContent).toContain('Reserva revisada');
    expect(shell.textContent).toContain('R$\u00a0150,00');
    expect(shell.textContent).toContain('R$\u00a029,50');
    expect(shell.textContent).not.toContain('Reserva da jornada');

    // Sair e reentrar na rota força outra leitura dentro da mesma sessão sintética ainda válida.
    await router.navigateByUrl('/');
    await fixture.whenStable();
    fixture.detectChanges();
    await router.navigateByUrl('/area/investimentos');
    await fixture.whenStable();
    fixture.detectChanges();
    const reloadRead = http.expectOne(`${API_BASE}/investments`);
    expect(reloadRead.request.method).toBe('GET');
    expect(reloadRead.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-investment-crud-token',
    );
    reloadRead.flush(
      investmentPortfolioResponse(
        investmentItemResponse({
          name: 'Reserva revisada',
          class: 'variable_income',
          currentAmount: '150.00',
        }),
        {
          totalAppliedAmount: '120.50',
          totalCurrentAmount: '150.00',
          difference: '29.50',
          fixedIncome: '0.00',
          variableIncome: '150.00',
        },
      ),
    );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(shell.textContent).toContain('Reserva revisada');
    expect(shell.textContent).toContain('R$\u00a0150,00');

    // Exclusão depende da confirmação explícita e só publica carteira vazia após DELETE 204 + GET.
    shell.querySelector<HTMLButtonElement>('[aria-label="Excluir Reserva revisada"]')!.click();
    fixture.detectChanges();
    shell
      .querySelector<HTMLButtonElement>('[aria-label="Confirmar exclusão de Reserva revisada"]')!
      .click();
    fixture.detectChanges();
    const deleteRequest = http.expectOne(`${API_BASE}/investments/${SYNTHETIC_INVESTMENT_ID}`);
    expect(deleteRequest.request.method).toBe('DELETE');
    expect(deleteRequest.request.headers.get('Authorization')).toBe(
      'Bearer synthetic-investment-crud-token',
    );
    deleteRequest.flush(null, { status: 204, statusText: 'No Content' });
    const afterDeleteRead = http.expectOne(`${API_BASE}/investments`);
    expect(afterDeleteRead.request.method).toBe('GET');
    afterDeleteRead.flush(emptyInvestmentPortfolioResponse());
    await fixture.whenStable();
    fixture.detectChanges();
    expect(shell.textContent).toContain('A carteira está vazia');
    expect(shell.textContent).toContain('R$\u00a00,00');
    expect(shell.querySelector('tbody tr')).toBeNull();
    http.expectNone(() => true);
    fixture.destroy();
  });

  // Exercita login, leituras protegidas de cartões/conexão e logout sem expor token no shell.
  it('percorre login, área de cartões e conexão e logout confirmado', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/login');
    await fixture.whenStable();
    const outlet = fixture.debugElement
      .query(By.directive(RouterOutlet))
      .injector.get(RouterOutlet);
    const login = outlet.component as LoginPage;
    login.emailControl.setValue('member@example.test');
    login.passwordControl.setValue('synthetic-flow-password');
    login.submit();
    TestBed.inject(HttpTestingController)
      .expectOne('http://127.0.0.1:5080/api/auth/login')
      .flush({
        accessToken: 'synthetic-flow-token',
        tokenType: 'Bearer',
        expiresIn: 1800,
        expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
      });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/area');
    expect(fixture.nativeElement.textContent).toContain('Cartões e conexão');
    expect(fixture.nativeElement.querySelectorAll('nav a[href="/area/movimentos"]')).toHaveLength(
      2,
    );
    expect(fixture.nativeElement.querySelectorAll('nav a[href="/area/visao"]')).toHaveLength(2);
    expect(
      fixture.nativeElement.querySelectorAll('nav a[href="/area/investimentos"]'),
    ).toHaveLength(2);
    expect(fixture.nativeElement.innerHTML).not.toContain('synthetic-flow-token');
    flushAuthenticatedAreaStartup(TestBed.inject(HttpTestingController));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(
      'Não há cartões disponíveis nos dados persistidos',
    );

    const exit = fixture.nativeElement.querySelector('nav.fixed button') as HTMLButtonElement;
    exit.click();
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('http://127.0.0.1:5080/api/auth/logout')
      .flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/login');
    expect(fixture.nativeElement.querySelectorAll('nav a[href="/area/movimentos"]')).toHaveLength(
      0,
    );
    expect(fixture.nativeElement.querySelectorAll('nav a[href="/area/visao"]')).toHaveLength(0);
    expect(
      fixture.nativeElement.querySelectorAll('nav a[href="/area/investimentos"]'),
    ).toHaveLength(0);
    expect(fixture.nativeElement.textContent).toContain('Saída confirmada pelo servidor');
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent).toContain(
      'Saída confirmada',
    );
    expect(TestBed.inject(SessionService).currentSession()).toBeNull();
    expect(router.url).not.toContain('synthetic-flow-token');
    fixture.destroy();
  });

  // Uma falha no logout limpa a área e comunica incerteza sem renderizar payload remoto.
  it('apresenta saída incerta após falha remota de logout', async () => {
    TestBed.inject(SessionService).startSession({
      accessToken: 'synthetic-uncertain-logout-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/area');
    fixture.detectChanges();
    flushAuthenticatedAreaStartup(TestBed.inject(HttpTestingController));
    (fixture.nativeElement.querySelector('nav.fixed button') as HTMLButtonElement).click();
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('http://127.0.0.1:5080/api/auth/logout')
      .flush(
        { detail: 'synthetic-uncertain-server-detail' },
        { status: 503, statusText: 'Service Unavailable' },
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/login');
    expect(TestBed.inject(SessionService).currentSession()).toBeNull();
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent).toContain(
      'não foi possível confirmar a saída no servidor',
    );
    expect(fixture.nativeElement.textContent).not.toContain('synthetic-uncertain-server-detail');
    fixture.destroy();
  });

  // Um 401 protegido encerra a geração e encaminha para login com aviso sem conteúdo remoto.
  it('redireciona após 401 protegido e apresenta orientação sanitizada', async () => {
    TestBed.inject(SessionService).startSession({
      accessToken: 'synthetic-401-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/area');
    fixture.detectChanges();
    flushAuthenticatedAreaStartup(TestBed.inject(HttpTestingController));
    TestBed.inject(HttpClient)
      .get('http://127.0.0.1:5080/api/private/resource')
      .subscribe({
        error: () => undefined,
      });
    TestBed.inject(HttpTestingController)
      .expectOne('http://127.0.0.1:5080/api/private/resource')
      .flush(
        { detail: 'synthetic-secret-server-detail' },
        { status: 401, statusText: 'Unauthorized' },
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/login');
    expect(TestBed.inject(SessionService).currentSession()).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('A sessão não está mais válida');
    expect(fixture.nativeElement.textContent).not.toContain('synthetic-secret-server-detail');
    fixture.destroy();
  });

  // Um 403 informa falta de permissão sem limpar sessão nem disparar login em repetição.
  it('preserva área e sessão em 403 protegido com aviso seguro', async () => {
    TestBed.inject(SessionService).startSession({
      accessToken: 'synthetic-403-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/area');
    fixture.detectChanges();
    flushAuthenticatedAreaStartup(TestBed.inject(HttpTestingController));
    TestBed.inject(HttpClient)
      .get('http://127.0.0.1:5080/api/private/resource')
      .subscribe({
        error: () => undefined,
      });
    TestBed.inject(HttpTestingController)
      .expectOne('http://127.0.0.1:5080/api/private/resource')
      .flush(
        { detail: 'synthetic-secret-server-detail' },
        { status: 403, statusText: 'Forbidden' },
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/area');
    expect(TestBed.inject(SessionService).currentSession()?.accessToken).toBe(
      'synthetic-403-token',
    );
    expect(fixture.nativeElement.textContent).toContain('não tem permissão');
    expect(fixture.nativeElement.textContent).not.toContain('synthetic-secret-server-detail');
    expect(fixture.nativeElement.querySelector('app-authenticated-area')).not.toBeNull();
    fixture.destroy();
  });

  // Expiração passiva também encerra a área e comunica a necessidade de entrar novamente.
  it('redireciona após expiração passiva e mostra aviso acessível', async () => {
    const initialTime = Date.parse('2026-11-25T12:00:00.000Z');
    const clock = vi.spyOn(Date, 'now').mockReturnValue(initialTime);
    const session = TestBed.inject(SessionService);
    session.startSession({
      accessToken: 'synthetic-expiring-token',
      tokenType: 'Bearer',
      expiresIn: 1800,
      expiresAtUtc: new Date(Date.now() + 1000).toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/area');
    fixture.detectChanges();
    flushAuthenticatedAreaStartup(TestBed.inject(HttpTestingController));
    clock.mockReturnValue(initialTime + 1001);
    session.currentSession();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/login');
    expect(session.currentSession()).toBeNull();
    expect(
      fixture.nativeElement.querySelectorAll('nav a[href="/area/investimentos"]'),
    ).toHaveLength(0);
    expect(fixture.nativeElement.textContent).toContain('Sua sessão expirou');
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'Entre novamente',
    );
    fixture.destroy();
  });
});

// Confirma a composição de infraestrutura HTTP e origem pública sem consumir a API.
describe('Composição pública da API', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [...(appConfig.providers ?? []), provideHttpClientTesting()],
    });
  });

  // A instância do cliente e a URL de desenvolvimento ficam disponíveis sem iniciar requisições.
  it('disponibiliza HttpClient e a origem local sem enviar chamadas', () => {
    expect(TestBed.inject(HttpClient)).toBeInstanceOf(HttpClient);
    expect(TestBed.inject(API_BASE_URL)).toBe('http://127.0.0.1:5080');
  });

  // Garante que o bootstrap e a navegação pública não consultem login ou endpoints financeiros.
  it('não inicia requests ao montar o shell público', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    TestBed.inject(HttpTestingController).expectNone(() => true);
    fixture.destroy();
  });
});
