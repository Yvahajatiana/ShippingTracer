import {
  lucideBell,
  lucideHouse,
  lucidePackage,
  lucidePackagePlus,
  lucideReceipt,
  lucideUser,
} from '@ng-icons/lucide';
import { NavItem } from '@parcelflow/shared';

/** Icons referenced by the navigation; registered at application level in app.config. */
export const NAVIGATION_ICONS = {
  lucideBell,
  lucideHouse,
  lucidePackage,
  lucidePackagePlus,
  lucideReceipt,
  lucideUser,
};

export const NAVIGATION: NavItem[] = [
  { labelKey: 'nav.home', icon: 'lucideHouse', route: '/', exact: true },
  {
    labelKey: 'nav.shipments',
    icon: 'lucidePackage',
    route: '/shipments',
    roles: ['customer'],
    exact: true,
  },
  {
    labelKey: 'nav.newShipment',
    icon: 'lucidePackagePlus',
    route: '/shipments/new',
    roles: ['customer'],
  },
  { labelKey: 'nav.invoices', icon: 'lucideReceipt', route: '/invoices', roles: ['customer'] },
  {
    labelKey: 'nav.notifications',
    icon: 'lucideBell',
    route: '/notifications',
    roles: ['customer'],
  },
  { labelKey: 'nav.profile', icon: 'lucideUser', route: '/profile', roles: ['customer'] },
];
