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

const MONITORING = 'nav.groups.monitoring';
const REFERENCE_DATA = 'nav.groups.referenceData';
const FINANCE = 'nav.groups.finance';

export const NAVIGATION: NavItem[] = [
  {
    labelKey: 'nav.overview',
    icon: 'lucideLayoutDashboard',
    route: '/',
    exact: true,
    roles: ['admin'],
    groupKey: MONITORING,
  },
  {
    labelKey: 'nav.companies',
    icon: 'lucideBuilding2',
    route: '/companies',
    roles: ['admin'],
    groupKey: REFERENCE_DATA,
  },
  {
    labelKey: 'nav.customers',
    icon: 'lucideUsers',
    route: '/customers',
    roles: ['admin'],
    groupKey: REFERENCE_DATA,
  },
  {
    labelKey: 'nav.tariffs',
    icon: 'lucideTags',
    route: '/tariffs',
    roles: ['admin'],
    groupKey: REFERENCE_DATA,
  },
  {
    labelKey: 'nav.warehouses',
    icon: 'lucideWarehouse',
    route: '/warehouses',
    roles: ['admin'],
    groupKey: REFERENCE_DATA,
  },
  {
    labelKey: 'nav.drivers',
    icon: 'lucideTruck',
    route: '/drivers',
    roles: ['admin'],
    groupKey: REFERENCE_DATA,
  },
  {
    labelKey: 'nav.invoices',
    icon: 'lucideReceipt',
    route: '/billing/invoices',
    roles: ['admin'],
    groupKey: FINANCE,
  },
  {
    labelKey: 'nav.notifications',
    icon: 'lucideBell',
    route: '/notifications',
    roles: ['admin'],
    groupKey: FINANCE,
  },
];
