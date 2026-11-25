import { Component, computed, input } from '@angular/core';
import { DashboardFilters } from './dashboard-api';

interface AppliedFilterLabel {
  readonly name: string;
  readonly value: string;
}

// Apresenta somente filtros efetivamente aplicados, sem criar uma segunda edição ou expor IDs internos.
@Component({
  selector: 'app-dashboard-applied-filters',
  standalone: true,
  templateUrl: './dashboard-applied-filters.html',
})
export class DashboardAppliedFiltersComponent {
  readonly filters = input.required<DashboardFilters>();
  readonly labels = computed(() => describeAppliedFilters(this.filters()));
}

// Converte os filtros para rótulos legíveis, preservando busca literal e limites temporais explícitos.
function describeAppliedFilters(filters: DashboardFilters): readonly AppliedFilterLabel[] {
  const labels: AppliedFilterLabel[] = [];
  if (filters.cardId !== undefined) {
    labels.push({ name: 'Cartão', value: 'Filtro de cartão aplicado; consulte a lista para identificá-lo.' });
  }
  if (filters.categoryId !== undefined) {
    labels.push({
      name: 'Categoria',
      value: 'Filtro de categoria aplicado; consulte a lista para identificá-la.',
    });
  }
  if (filters.situation !== undefined) {
    labels.push({ name: 'Situação', value: describeSituation(filters.situation) });
  }
  if (filters.from !== undefined) {
    labels.push({ name: 'Data inicial', value: `${filters.from} (inclusiva)` });
  }
  if (filters.toExclusive !== undefined) {
    labels.push({ name: 'Data final', value: `${filters.toExclusive} (exclusiva)` });
  }
  if (filters.search !== undefined) {
    labels.push({ name: 'Busca literal', value: filters.search });
  }
  return labels;
}

// Traduz situação por códigos conhecidos e não reproduz texto inesperado vindo de estado inválido.
function describeSituation(value: string): string {
  switch (value) {
    case 'Effective':
      return 'Efetivado';
    case 'Pending':
      return 'Pendente';
    case 'FutureInstallment':
      return 'Parcela futura';
    case 'Unknown':
      return 'Desconhecida';
    default:
      return 'Situação não reconhecida';
  }
}
