import { Routes } from '@angular/router';
import { AuthenticatedAreaPage } from './auth/area/authenticated-area';
import { authenticatedSessionGuard } from './auth/authenticated-session.guard';
import { TransactionListPage } from './auth/area/transaction-list-page';
import { DashboardOverviewPage } from './auth/area/dashboard-overview-page';
import { LoginPage } from './auth/login/login';
import { HomePage } from './home/home';
import { NotFoundPage } from './not-found/not-found';

// Declara rotas públicas e destinos protegidos já entregues, mantendo um 404 explícito.
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    component: HomePage,
    title: 'Finora — início',
  },
  {
    path: 'login',
    component: LoginPage,
    title: 'Entrar — Finora',
  },
  {
    path: 'area',
    component: AuthenticatedAreaPage,
    canActivate: [authenticatedSessionGuard],
    title: 'Cartões e conexão — Finora',
  },
  {
    path: 'area/movimentos',
    component: TransactionListPage,
    canActivate: [authenticatedSessionGuard],
    title: 'Movimentos — Finora',
  },
  {
    path: 'area/visao',
    component: DashboardOverviewPage,
    canActivate: [authenticatedSessionGuard],
    title: 'Visão descritiva — Finora',
  },
  // A carteira reutiliza o guard local e carrega apenas sua API manual, sem fluxo de cartão.
  {
    path: 'area/investimentos',
    loadComponent: () =>
      import('./auth/area/investment-portfolio-page').then(
        (module) => module.InvestmentPortfolioPage,
      ),
    canActivate: [authenticatedSessionGuard],
    title: 'Investimentos — Finora',
  },
  {
    path: '**',
    component: NotFoundPage,
    title: 'Página não encontrada — Finora',
  },
];
