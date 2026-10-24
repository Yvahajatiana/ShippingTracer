import {
  lucideBuilding2,
  lucideLayoutDashboard,
  lucidePackage,
  lucideRoute,
  lucideTruck,
  lucideUsers,
  lucideWarehouse,
} from '@ng-icons/lucide';
import { NavItem, Role } from '@parcelflow/shared';

/** Icons referenced by the navigation; registered at application level in app.config. */
export const NAVIGATION_ICONS = {
  lucideBuilding2,
  lucideLayoutDashboard,
  lucidePackage,
  lucideRoute,
  lucideTruck,
  lucideUsers,
  lucideWarehouse,
};

const STAFF: Role[] = ['operator', 'admin'];

export const NAVIGATION: NavItem[] = [
  {
    labelKey: 'nav.dashboard',
    icon: 'lucideLayoutDashboard',
    route: '/',
    exact: true,
    roles: STAFF,
  },
  { labelKey: 'nav.shipments', icon: 'lucidePackage', route: '/shipments', roles: STAFF },
  { labelKey: 'nav.customers', icon: 'lucideUsers', route: '/customers', roles: STAFF },
  { labelKey: 'nav.companies', icon: 'lucideBuilding2', route: '/companies', roles: STAFF },
  { labelKey: 'nav.warehouses', icon: 'lucideWarehouse', route: '/warehouses', roles: STAFF },
  { labelKey: 'nav.routes', icon: 'lucideRoute', route: '/delivery/routes', roles: STAFF },
  { labelKey: 'nav.drivers', icon: 'lucideTruck', route: '/delivery/drivers', roles: STAFF },
];
