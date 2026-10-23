import { Routes } from '@angular/router';
import { ForbiddenPage, NotFoundPage, roleGuard } from '@parcelflow/shared';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [roleGuard('customer')],
    loadComponent: () => import('./home/home.page').then((m) => m.HomePage),
  },
  { path: 'forbidden', component: ForbiddenPage },
  { path: '**', component: NotFoundPage },
];
