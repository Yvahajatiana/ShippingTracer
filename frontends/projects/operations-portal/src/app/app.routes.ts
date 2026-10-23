import { Routes } from '@angular/router';
import { ForbiddenPage, NotFoundPage, roleGuard } from '@parcelflow/shared';

/** Every operations page requires the operator or admin role. */
const staffOnly = [roleGuard('operator', 'admin')];

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: staffOnly,
    loadComponent: () => import('./dashboard/dashboard.page').then((m) => m.DashboardPage),
  },
  {
    path: 'shipments',
    canActivate: staffOnly,
    loadComponent: () => import('./shipments/shipment-list.page').then((m) => m.ShipmentListPage),
  },
  {
    path: 'shipments/:id',
    canActivate: staffOnly,
    loadComponent: () =>
      import('./shipments/shipment-detail.page').then((m) => m.ShipmentDetailPage),
  },
  {
    path: 'customers',
    canActivate: staffOnly,
    loadComponent: () => import('./customers/customer-list.page').then((m) => m.CustomerListPage),
  },
  {
    path: 'customers/:id',
    canActivate: staffOnly,
    loadComponent: () =>
      import('./customers/customer-detail.page').then((m) => m.CustomerDetailPage),
  },
  {
    path: 'companies',
    canActivate: staffOnly,
    loadComponent: () => import('./customers/company-list.page').then((m) => m.CompanyListPage),
  },
  {
    path: 'warehouses',
    canActivate: staffOnly,
    loadComponent: () =>
      import('./warehouses/warehouse-list.page').then((m) => m.WarehouseListPage),
  },
  {
    path: 'warehouses/:id',
    canActivate: staffOnly,
    loadComponent: () =>
      import('./warehouses/warehouse-detail.page').then((m) => m.WarehouseDetailPage),
  },
  { path: 'delivery', pathMatch: 'full', redirectTo: 'delivery/routes' },
  {
    path: 'delivery/routes',
    canActivate: staffOnly,
    loadComponent: () => import('./delivery/route-list.page').then((m) => m.RouteListPage),
  },
  {
    path: 'delivery/routes/:id',
    canActivate: staffOnly,
    loadComponent: () => import('./delivery/route-detail.page').then((m) => m.RouteDetailPage),
  },
  {
    path: 'delivery/drivers',
    canActivate: staffOnly,
    loadComponent: () => import('./delivery/driver-list.page').then((m) => m.DriverListPage),
  },
  { path: 'forbidden', component: ForbiddenPage },
  { path: '**', component: NotFoundPage },
];
