import { Routes } from '@angular/router';
import { HomePage } from './home/home';
import { NotFoundPage } from './not-found/not-found';

// Declara somente a página entregue e direciona caminhos desconhecidos a um 404 explícito.
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    component: HomePage,
    title: 'Finora — início',
  },
  {
    path: '**',
    component: NotFoundPage,
    title: 'Página não encontrada — Finora',
  },
];
