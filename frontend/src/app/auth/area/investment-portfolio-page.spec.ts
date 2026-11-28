import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { InvestmentInput, InvestmentItem, InvestmentPortfolio } from './investment-portfolio-api';
import { InvestmentPortfolioPage } from './investment-portfolio-page';
import {
  InvestmentPortfolioReadState,
  InvestmentPortfolioStateService,
} from './investment-portfolio-state';

const INVESTMENT_ID = '87b65749-c927-431f-8d51-35fdf3106336';
const MAXIMUM_ITEM_AMOUNT = '99999999999999999.99';

// A fixture mantém uma posição allowlisted e valores textuais iguais aos recebidos do backend.
function investmentItem(overrides: Partial<InvestmentItem> = {}): InvestmentItem {
  return {
    id: INVESTMENT_ID,
    name: 'Reserva sintética',
    class: 'fixed_income',
    appliedAmount: '120.50',
    currentAmount: '125.75',
    ...overrides,
  };
}

// O resumo é sempre fornecido pelo serviço e não recalculado a partir dos itens na apresentação.
function portfolio(
  items: readonly InvestmentItem[] = [investmentItem()],
  overrides: Partial<InvestmentPortfolio['summary']> = {},
): InvestmentPortfolio {
  const empty = items.length === 0;
  return {
    items,
    summary: {
      totalAppliedAmount: empty ? '0.00' : '120.50',
      totalCurrentAmount: empty ? '0.00' : '125.75',
      difference: empty ? '0.00' : '5.25',
      currentAmountsByClass: [
        { class: 'fixed_income', label: 'Renda fixa', currentAmount: empty ? '0.00' : '125.75' },
        { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
        { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
      ],
      ...overrides,
    },
  };
}

// Constrói estados explícitos para exibir sucesso, vazio, carregamento ou falha sem HTTP real.
function readState(
  status: InvestmentPortfolioReadState['status'],
  snapshot: InvestmentPortfolio | null,
  failure: InvestmentPortfolioReadState['failure'] = null,
): InvestmentPortfolioReadState {
  return {
    status,
    generation: 3,
    snapshot,
    failure,
    mutationStatus: 'idle',
    mutationFailure: null,
  };
}

// Fornece um estado substituto para testar a página sem sessão, rede ou dados de pessoas.
function createPage(initialState = readState('idle', null)): {
  readonly fixture: ComponentFixture<InvestmentPortfolioPage>;
  readonly page: InvestmentPortfolioPage;
  readonly view: ReturnType<typeof signal<InvestmentPortfolioReadState>>;
  readonly state: {
    readonly load: ReturnType<typeof vi.fn>;
    readonly retry: ReturnType<typeof vi.fn>;
    readonly cancelPendingRead: ReturnType<typeof vi.fn>;
    readonly createInvestment: ReturnType<typeof vi.fn>;
    readonly updateInvestment: ReturnType<typeof vi.fn>;
    readonly deleteInvestment: ReturnType<typeof vi.fn>;
  };
} {
  const view = signal(initialState);
  const state = {
    view,
    load: vi.fn(),
    retry: vi.fn(),
    cancelPendingRead: vi.fn(),
    createInvestment: vi.fn().mockReturnValue(of(investmentItem())),
    updateInvestment: vi.fn().mockReturnValue(of(investmentItem())),
    deleteInvestment: vi.fn().mockReturnValue(of(null)),
  };

  TestBed.configureTestingModule({
    imports: [InvestmentPortfolioPage],
    providers: [provideRouter([]), { provide: InvestmentPortfolioStateService, useValue: state }],
  });
  const fixture = TestBed.createComponent(InvestmentPortfolioPage);
  fixture.detectChanges();
  return { fixture, page: fixture.componentInstance, view, state };
}

// A página apresenta os mesmos dados em telas responsivas e conserva acessibilidade de formulário.
describe('Página da carteira de investimentos', () => {
  afterEach(() => TestBed.resetTestingModule());

  // Valores máximos e diferenças negativas são formatados diretamente da string decimal recebida.
  it('apresenta resumo BRL exato, classe e aviso de diferença simples', () => {
    const maximumItem = investmentItem({
      appliedAmount: MAXIMUM_ITEM_AMOUNT,
      currentAmount: '0.00',
    });
    const fixture = createPage(
      readState(
        'loaded',
        portfolio([maximumItem], {
          totalAppliedAmount: MAXIMUM_ITEM_AMOUNT,
          totalCurrentAmount: '0.00',
          difference: `-${MAXIMUM_ITEM_AMOUNT}`,
          currentAmountsByClass: [
            { class: 'fixed_income', label: 'Renda fixa', currentAmount: '0.00' },
            { class: 'variable_income', label: 'Renda variável', currentAmount: '0.00' },
            { class: 'real_estate_funds', label: 'Fundos imobiliários', currentAmount: '0.00' },
          ],
        }),
      ),
    );
    const root = fixture.fixture.nativeElement as HTMLElement;
    const text = root.textContent ?? '';

    expect(fixture.state.load).toHaveBeenCalledOnce();
    expect(root.querySelector('h1')?.textContent).toContain('Carteira de investimentos');
    expect(text).toContain('R$\u00a099.999.999.999.999.999,99');
    expect(text).toContain('-R$\u00a099.999.999.999.999.999,99');
    expect(text).toContain('Renda fixa');
    expect(text).toContain(
      'Não representa rentabilidade ajustada a aportes, datas, resgates, taxas ou impostos.',
    );
    expect(root.querySelector('table')?.parentElement?.className).toContain('overflow-x-auto');
    fixture.fixture.destroy();
    expect(fixture.state.cancelPendingRead).toHaveBeenCalledOnce();
  });

  // Zeros BRL continuam valores reais da resposta e não são substituídos por placeholders.
  it('apresenta carteira vazia e totais zero em formato brasileiro', () => {
    const fixture = createPage(readState('empty', portfolio([])));
    const root = fixture.fixture.nativeElement as HTMLElement;
    const text = root.textContent ?? '';

    expect(text).toContain('A carteira está vazia.');
    expect(text).toContain('R$\u00a00,00');
    expect(root.querySelectorAll('tbody tr')).toHaveLength(0);
  });

  // O formulário nativo envia exatamente as strings digitadas, inclusive a faixa máxima persistível.
  it('cadastra com validação do formulário e preserva dinheiro como string', () => {
    const fixture = createPage();
    const root = fixture.fixture.nativeElement as HTMLElement;
    const name = root.querySelector<HTMLInputElement>('#investment-name')!;
    const classSelect = root.querySelector<HTMLSelectElement>('#investment-class')!;
    const appliedAmount = root.querySelector<HTMLInputElement>('#investment-applied-amount')!;
    const currentAmount = root.querySelector<HTMLInputElement>('#investment-current-amount')!;

    name.value = 'Reserva longa';
    name.dispatchEvent(new Event('input', { bubbles: true }));
    classSelect.value = 'variable_income';
    classSelect.dispatchEvent(new Event('change', { bubbles: true }));
    appliedAmount.value = MAXIMUM_ITEM_AMOUNT;
    appliedAmount.dispatchEvent(new Event('input', { bubbles: true }));
    currentAmount.value = '0.01';
    currentAmount.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.fixture.detectChanges();
    root.querySelector<HTMLFormElement>('form')!.requestSubmit();
    fixture.fixture.detectChanges();

    const expected: InvestmentInput = {
      name: 'Reserva longa',
      class: 'variable_income',
      appliedAmount: MAXIMUM_ITEM_AMOUNT,
      currentAmount: '0.01',
    };
    expect(fixture.state.createInvestment).toHaveBeenCalledWith(expected);
    expect(typeof fixture.state.createInvestment.mock.calls[0][0].appliedAmount).toBe('string');
    expect(fixture.page.draft()).toEqual({
      name: '',
      class: 'fixed_income',
      appliedAmount: '',
      currentAmount: '',
    });
    expect(root.textContent).toContain('Cadastro confirmado pelo servidor.');
  });

  // Campos inválidos são bloqueados pelo padrão nativo antes que a API de criação seja chamada.
  it('recusa valor com vírgula ou escala variável sem coerção silenciosa', () => {
    const fixture = createPage();
    const root = fixture.fixture.nativeElement as HTMLElement;
    const name = root.querySelector<HTMLInputElement>('#investment-name')!;
    const appliedAmount = root.querySelector<HTMLInputElement>('#investment-applied-amount')!;
    const currentAmount = root.querySelector<HTMLInputElement>('#investment-current-amount')!;
    name.value = 'Reserva';
    name.dispatchEvent(new Event('input', { bubbles: true }));
    appliedAmount.value = '1,25';
    appliedAmount.dispatchEvent(new Event('input', { bubbles: true }));
    currentAmount.value = '1.00';
    currentAmount.dispatchEvent(new Event('input', { bubbles: true }));

    root.querySelector<HTMLFormElement>('form')!.requestSubmit();

    expect(fixture.state.createInvestment).not.toHaveBeenCalled();
    expect(fixture.page.draft().appliedAmount).toBe('1,25');
  });

  // Edição reusa as strings do snapshot e preserva cada centavo ao enviar PUT.
  it('carrega strings monetárias existentes no formulário e atualiza a posição escolhida', () => {
    const item = investmentItem({ appliedAmount: MAXIMUM_ITEM_AMOUNT, currentAmount: '0.01' });
    const fixture = createPage(readState('loaded', portfolio([item])));
    const root = fixture.fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('[aria-label="Editar Reserva sintética"]')!.click();
    fixture.fixture.detectChanges();

    expect(root.querySelector<HTMLInputElement>('#investment-applied-amount')?.value).toBe(
      MAXIMUM_ITEM_AMOUNT,
    );
    expect(root.querySelector<HTMLInputElement>('#investment-current-amount')?.value).toBe('0.01');

    const currentAmount = root.querySelector<HTMLInputElement>('#investment-current-amount')!;
    currentAmount.value = '10.10';
    currentAmount.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.fixture.detectChanges();
    root.querySelector<HTMLFormElement>('form')!.requestSubmit();
    fixture.fixture.detectChanges();

    expect(fixture.state.updateInvestment).toHaveBeenCalledWith(INVESTMENT_ID, {
      name: 'Reserva sintética',
      class: 'fixed_income',
      appliedAmount: MAXIMUM_ITEM_AMOUNT,
      currentAmount: '10.10',
    });
  });

  // Confirmação inline mantém foco no acionador, permite cancelar e só exclui após confirmação.
  it('preserva o acionador focado e só exclui após confirmação explícita', () => {
    const fixture = createPage(readState('loaded', portfolio()));
    const root = fixture.fixture.nativeElement as HTMLElement;
    const trigger = root.querySelector<HTMLButtonElement>(
      '[aria-label="Excluir Reserva sintética"]',
    )!;
    trigger.focus();
    trigger.click();
    fixture.fixture.detectChanges();

    const confirmation = root.querySelector('[role="group"]');
    expect(confirmation?.getAttribute('aria-label')).toContain('Reserva sintética');
    expect(document.activeElement).toBe(trigger);
    expect(fixture.state.deleteInvestment).not.toHaveBeenCalled();

    root.querySelector<HTMLDivElement>('[role="group"]')?.querySelectorAll('button')[1]?.click();
    fixture.fixture.detectChanges();
    expect(fixture.state.deleteInvestment).not.toHaveBeenCalled();
    expect(root.querySelector('[role="group"]')).toBeNull();

    trigger.click();
    fixture.fixture.detectChanges();
    root
      .querySelector<HTMLButtonElement>('[aria-label="Confirmar exclusão de Reserva sintética"]')
      ?.click();
    fixture.fixture.detectChanges();

    expect(fixture.state.deleteInvestment).toHaveBeenCalledWith(INVESTMENT_ID);
    expect(root.textContent).toContain('Exclusão confirmada pelo servidor.');
    expect(root.querySelector('[role="group"]')).toBeNull();
  });

  // A falha fica anunciada por texto seguro e o botão repete somente a consulta da carteira.
  it('mostra falha sanitizada e disponibiliza retry da leitura', () => {
    const fixture = createPage(readState('error', null, 'unavailable'));
    const root = fixture.fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'A API está temporariamente indisponível',
    );
    root.querySelector<HTMLButtonElement>('button')!.click();
    expect(fixture.state.retry).toHaveBeenCalledOnce();
  });

  // Carregamento inicial é anunciado sem inventar carteira vazia nem dados de exemplo.
  it('anuncia carregamento e não apresenta posição antes da primeira resposta', () => {
    const fixture = createPage(readState('loading', null));
    const root = fixture.fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[role="status"]')?.textContent).toContain('Carregando carteira');
    expect(root.querySelector('tbody tr')).toBeNull();
    expect(root.textContent).not.toContain('R$\u00a00,00');
  });

  // Salvamento desativa campos até confirmação e falha mostra somente uma mensagem local fixa.
  it('representa gravação pendente e falha sem anunciar sucesso prematuro', () => {
    const fixture = createPage();
    const root = fixture.fixture.nativeElement as HTMLElement;
    fixture.view.update((state) => ({ ...state, mutationStatus: 'saving' }));
    fixture.fixture.detectChanges();

    const submit = root.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(submit.disabled).toBe(true);
    expect(submit.textContent).toContain('Salvando');
    expect(root.querySelector<HTMLInputElement>('#investment-name')?.disabled).toBe(true);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Salvando');
    expect(root.querySelector('[role="status"]')?.textContent).not.toContain('confirmado');

    fixture.view.update((state) => ({
      ...state,
      mutationStatus: 'error',
      mutationFailure: 'request-failed',
    }));
    fixture.fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'Não foi possível confirmar a alteração da carteira.',
    );
    expect(root.textContent).not.toContain('Cadastro confirmado pelo servidor.');
  });

  // Controles mantêm label, nome acessível, foco visível e sem tabindex que altere a ordem natural.
  it('mantém labels associados e ordem de foco nativa para teclado e leitor de tela', () => {
    const fixture = createPage(readState('loaded', portfolio()));
    const root = fixture.fixture.nativeElement as HTMLElement;
    const fields = [
      ['investment-name', 'Nome'],
      ['investment-class', 'Classe'],
      ['investment-applied-amount', 'Valor aplicado (BRL)'],
      ['investment-current-amount', 'Valor atual (BRL)'],
    ];

    for (const [id, labelText] of fields) {
      const field = root.querySelector<HTMLElement>(`#${id}`)!;
      const label = root.querySelector<HTMLLabelElement>(`label[for="${id}"]`);
      expect(label?.textContent?.trim()).toBe(labelText);
      expect(field.hasAttribute('tabindex')).toBe(false);
      expect(field.className).toContain('focus-visible:outline');
    }

    const appliedAmount = root.querySelector<HTMLInputElement>('#investment-applied-amount')!;
    const currentAmount = root.querySelector<HTMLInputElement>('#investment-current-amount')!;
    expect(appliedAmount.type).toBe('text');
    expect(currentAmount.type).toBe('text');
    expect(appliedAmount.inputMode).toBe('decimal');
    expect(root.querySelector('button[type="submit"]')).not.toBeNull();
    expect(root.querySelector('button[tabindex]')).toBeNull();
  });
});
