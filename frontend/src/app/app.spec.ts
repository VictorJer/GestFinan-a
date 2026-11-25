import { HttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { App } from './app';
import { appConfig } from './app.config';
import { API_BASE_URL } from './api-base-url.token';
import { routes } from './app.routes';
import { HomePage } from './home/home';
import { NotFoundPage } from './not-found/not-found';

// Exercita a rota inicial sem provedores HTTP, sessão ou dados demonstrativos.
describe('Página inicial pública', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes)],
    });
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

  // Restringe destinos, elimina ações futuras e garante foco visível na navegação pública.
  it('vincula a navegação pública somente ao início', async () => {
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
      '/login',
      '/dashboard',
      '/card',
      '/transactions',
      '/investments',
      '/simulator',
      '/sync',
    ];

    expect(routes.map((route) => route.path)).toEqual(['', '**']);
    expect(navigation.map((link) => link.getAttribute('href'))).toEqual(['/', '/']);
    expect(unavailablePaths.some((path) => linkTargets.includes(path))).toBe(false);
    expect(navigation.every((link) => link.classList.contains('focus-visible:outline-solid'))).toBe(
      true,
    );
    expect(navigation.every((link) => !link.classList.contains('outline-none'))).toBe(true);
    expect(navigation[0]?.classList.contains('outline-finora-focus')).toBe(true);
    expect(navigation[1]?.classList.contains('outline-finora-brand')).toBe(true);
    expect(shell.querySelectorAll('button, form, input, select, textarea')).toHaveLength(0);
    expect(shell.querySelector('header')).not.toBeNull();
    expect(shell.querySelector('main#conteudo[tabindex="-1"]')).not.toBeNull();
    expect(shell.querySelector('a[href="#conteudo"]')?.textContent).toContain(
      'Pular para o conteúdo',
    );
    expect(shell.querySelectorAll('nav a[aria-current="page"]')).toHaveLength(2);
  });
});

// Confirma a composição de infraestrutura HTTP e origem pública sem consumir a API.
describe('Composição pública da API', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [...(appConfig.providers ?? [])] });
  });

  // A instância do cliente e a URL de desenvolvimento ficam disponíveis sem iniciar requisições.
  it('disponibiliza HttpClient e a origem local sem enviar chamadas', () => {
    expect(TestBed.inject(HttpClient)).toBeInstanceOf(HttpClient);
    expect(TestBed.inject(API_BASE_URL)).toBe('http://127.0.0.1:5080');
  });
});
