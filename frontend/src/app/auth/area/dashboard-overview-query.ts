import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { SessionScopedResponse } from './card-connection-api';
import { DashboardApi, DashboardFilters, DashboardSummary } from './dashboard-api';
import { TransactionListFilters } from './transaction-list-api';
import { TransactionListStateService } from './transaction-list-state';

// Liga a visão à única fonte dos filtros aplicados e deixa rascunhos fora da consulta do resumo.
@Injectable({ providedIn: 'root' })
export class DashboardOverviewQueryService {
  private readonly dashboardApi = inject(DashboardApi);
  private readonly transactionListState = inject(TransactionListStateService);

  // Projeta filtros aplicados sem alterar a lista ou consultar o rascunho ainda não submetido.
  getAppliedFilters(): DashboardFilters {
    return projectDashboardFilters(this.transactionListState.view().filters);
  }

  // Captura o filtro confirmado no momento da navegação e delega a leitura protegida ao cliente HTTP.
  getSummaryForAppliedFilters(): Observable<SessionScopedResponse<DashboardSummary>> {
    return this.getSummaryForFilters(this.getAppliedFilters());
  }

  // Mantém os filtros capturados junto da requisição mesmo se outro estado mudar depois da leitura.
  getSummaryForFilters(
    filters: DashboardFilters,
  ): Observable<SessionScopedResponse<DashboardSummary>> {
    return this.dashboardApi.getDashboard(filters);
  }
}

// Copia apenas os seis filtros da lista; página, tamanho e qualquer campo futuro não atravessam a fronteira.
function projectDashboardFilters(filters: TransactionListFilters): DashboardFilters {
  const projected: {
    cardId?: string;
    categoryId?: string;
    situation?: string;
    from?: string;
    toExclusive?: string;
    search?: string;
  } = {};

  for (const name of [
    'cardId',
    'categoryId',
    'situation',
    'from',
    'toExclusive',
    'search',
  ] as const) {
    const value = filters[name];
    if (value !== undefined) {
      projected[name] = value;
    }
  }

  return projected;
}
