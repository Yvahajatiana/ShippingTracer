import {
  lucideBell,
  lucideBuilding2,
  lucideLayoutDashboard,
  lucideReceipt,
  lucideTags,
  lucideTruck,
  lucideUsers,
  lucideWarehouse,
} from '@ng-icons/lucide';
import { NavItem } from '@parcelflow/shared';

/** Icons referenced by the navigation; registered at application level in app.config. */
export const NAVIGATION_ICONS = {
  lucideBell,
  lucideBuilding2,
  lucideLayoutDashboard,
  lucideReceipt,
  lucideTags,
  lucideTruck,
  lucideUsers,
  lucideWarehouse,
};

export const NAVIGATION: NavItem[] = [
  {
    labelKey: 'nav.overview',
    icon: 'lucideLayoutDashboard',
    route: '/',
    exact: true,
    roles: ['admin'],
  },
  { labelKey: 'nav.companies', icon: 'lucideBuilding2', route: '/companies', roles: ['admin'] },
  { labelKey: 'nav.customers', icon: 'lucideUsers', route: '/customers', roles: ['admin'] },
  { labelKey: 'nav.tariffs', icon: 'lucideTags', route: '/tariffs', roles: ['admin'] },
  { labelKey: 'nav.warehouses', icon: 'lucideWarehouse', route: '/warehouses', roles: ['admin'] },
  { labelKey: 'nav.drivers', icon: 'lucideTruck', route: '/drivers', roles: ['admin'] },
  { labelKey: 'nav.invoices', icon: 'lucideReceipt', route: '/billing/invoices', roles: ['admin'] },
  { labelKey: 'nav.notifications', icon: 'lucideBell', route: '/notifications', roles: ['admin'] },
];
