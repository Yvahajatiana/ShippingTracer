import { lucideHouse } from '@ng-icons/lucide';
import { NavItem } from '@parcelflow/shared';

/** Icons referenced by the navigation; registered at application level in app.config. */
export const NAVIGATION_ICONS = { lucideHouse };

export const NAVIGATION: NavItem[] = [
  { labelKey: 'nav.home', icon: 'lucideHouse', route: '/', exact: true },
];
