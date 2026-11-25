import { Routes } from '@angular/router';
import { AuthenticatedAreaPage } from './auth/area/authenticated-area';
import { authenticatedSessionGuard } from './auth/authenticated-session.guard';
import { TransactionListPage } from './auth/area/transaction-list-page';
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
    path: '**',
    component: NotFoundPage,
    title: 'Página não encontrada — Finora',
  },
];
