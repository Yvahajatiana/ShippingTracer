import { Routes } from '@angular/router';
import { ForbiddenPage, NotFoundPage, roleGuard } from '@parcelflow/shared';

const adminOnly = [roleGuard('admin')];

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: adminOnly,
    loadComponent: () => import('./overview/overview.page').then((m) => m.OverviewPage),
  },
  {
    path: 'companies',
    canActivate: adminOnly,
    loadComponent: () => import('./companies/companies.page').then((m) => m.CompaniesPage),
  },
  {
    path: 'customers',
    canActivate: adminOnly,
    loadComponent: () => import('./customers/customers.page').then((m) => m.CustomersPage),
  },
  {
    path: 'customers/:id',
    canActivate: adminOnly,
    loadComponent: () =>
      import('./customers/customer-detail.page').then((m) => m.CustomerDetailPage),
  },
  {
    path: 'tariffs',
    canActivate: adminOnly,
    loadComponent: () => import('./tariffs/tariffs.page').then((m) => m.TariffsPage),
  },
  {
    path: 'warehouses',
    canActivate: adminOnly,
    loadComponent: () => import('./warehouses/warehouses.page').then((m) => m.WarehousesPage),
  },
  {
    path: 'drivers',
    canActivate: adminOnly,
    loadComponent: () => import('./drivers/drivers.page').then((m) => m.DriversPage),
  },
  { path: 'billing', pathMatch: 'full', redirectTo: 'billing/invoices' },
  {
    path: 'billing/invoices',
    canActivate: adminOnly,
    loadComponent: () => import('./billing/invoices.page').then((m) => m.InvoicesPage),
  },
  {
    path: 'billing/invoices/:id',
    canActivate: adminOnly,
    loadComponent: () => import('./billing/invoice-detail.page').then((m) => m.InvoiceDetailPage),
  },
  {
    path: 'notifications',
    canActivate: adminOnly,
    loadComponent: () =>
      import('./notifications/notifications.page').then((m) => m.NotificationsPage),
  },
  { path: 'forbidden', component: ForbiddenPage },
  { path: '**', component: NotFoundPage },
];
