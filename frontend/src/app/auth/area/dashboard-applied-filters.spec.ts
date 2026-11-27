import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DashboardAppliedFiltersComponent } from './dashboard-applied-filters';

// Renderiza o componente real com filtros sintéticos sem depender de API ou opções financeiras.
function createComponent(filters: Record<string, string>): ComponentFixture<DashboardAppliedFiltersComponent> {
  TestBed.configureTestingModule({ imports: [DashboardAppliedFiltersComponent] });
  const fixture = TestBed.createComponent(DashboardAppliedFiltersComponent);
  fixture.componentRef.setInput('filters', filters);
  fixture.detectChanges();
  return fixture;
}

// O resumo mostra contexto aplicado e nunca adiciona formulário ou IDs internos à navegação.
describe('Apresentação dos filtros aplicados da visão', () => {
  // IDs permanecem internos; limites e busca aparecem em texto e a edição segue na lista existente.
  it('exibe filtros aplicados, distingue o rascunho e não expõe identificadores internos', () => {
    const cardId = '2a990192-cb59-43eb-8520-cfef6a0e3e40';
    const categoryId = '6e209084-7488-4d97-930f-10f52df4b116';
    const fixture = createComponent({
      cardId,
      categoryId,
      situation: 'Pending',
      from: '2026-11-01',
      toExclusive: '2026-12-01',
      search: 'café 20%_ literal',
    });
    const rendered = fixture.nativeElement as HTMLElement;

    expect(rendered.textContent).toContain('Filtros aplicados');
    expect(rendered.querySelector('h2')?.textContent).toContain('Filtros aplicados');
    expect(Array.from(rendered.querySelectorAll('dt')).map((label) => label.textContent?.trim())).toEqual([
      'Cartão',
      'Categoria',
      'Situação',
      'Data inicial',
      'Data final',
      'Busca literal',
    ]);
    expect(rendered.textContent).toContain('Filtro de cartão aplicado');
    expect(rendered.textContent).toContain('Filtro de categoria aplicado');
    expect(rendered.textContent).toContain('Pendente');
    expect(rendered.textContent).toContain('2026-11-01 (inclusiva)');
    expect(rendered.textContent).toContain('2026-12-01 (exclusiva)');
    expect(rendered.textContent).toContain('café 20%_ literal');
    expect(rendered.textContent).toContain('rascunho ainda não enviado');
    expect(rendered.textContent).not.toContain(cardId);
    expect(rendered.textContent).not.toContain(categoryId);
    expect(rendered.querySelector('form')).toBeNull();
    expect(rendered.querySelector('input, select, textarea')).toBeNull();
  });

  // A ausência de filtros informa o escopo completo sem confundir resumo vazio com filtros não aplicados.
  it('informa quando nenhum filtro foi aplicado', () => {
    const fixture = createComponent({});

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Nenhum filtro aplicado; o resumo considera o conjunto de movimentos persistidos.',
    );
    expect((fixture.nativeElement as HTMLElement).querySelector('form')).toBeNull();
  });
});
