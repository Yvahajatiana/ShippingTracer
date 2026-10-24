import { Routes } from '@angular/router';
import { ForbiddenPage, NotFoundPage, roleGuard } from '@parcelflow/shared';

const customerOnly = [roleGuard('customer')];

export const routes: Routes = [
  // Public pages (Keycloak check-sso: visitors may be anonymous).
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./home/home.page').then((m) => m.HomePage),
  },
  {
    path: 'track/:trackingNumber',
    loadComponent: () => import('./tracking/tracking.page').then((m) => m.TrackingPage),
  },

  // Customer area.
  {
    path: 'profile',
    canActivate: customerOnly,
    loadComponent: () => import('./profile/profile.page').then((m) => m.ProfilePage),
  },
  {
    path: 'shipments',
    canActivate: customerOnly,
    children: [
      {
        path: '',
        loadComponent: () => import('./shipments/shipments.page').then((m) => m.ShipmentsPage),
      },
      {
        path: 'new',
        loadComponent: () => import('./shipments/new-shipment.page').then((m) => m.NewShipmentPage),
      },
      {
        path: ':id',
        loadComponent: () =>
          import('./shipments/shipment-detail.page').then((m) => m.ShipmentDetailPage),
      },
    ],
  },
  {
    path: 'invoices',
    canActivate: customerOnly,
    children: [
      {
        path: '',
        loadComponent: () => import('./invoices/invoices.page').then((m) => m.InvoicesPage),
      },
      {
        path: ':id',
        loadComponent: () =>
          import('./invoices/invoice-detail.page').then((m) => m.InvoiceDetailPage),
      },
    ],
  },
  {
    path: 'notifications',
    canActivate: customerOnly,
    loadComponent: () =>
      import('./notifications/notifications.page').then((m) => m.NotificationsPage),
  },

  { path: 'forbidden', component: ForbiddenPage },
  { path: '**', component: NotFoundPage },
];
