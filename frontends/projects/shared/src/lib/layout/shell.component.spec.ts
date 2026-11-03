import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideParcelFlowTesting, subjectFor } from '../../testing';
import { NavItem, ShellComponent, ShellLayout } from './shell.component';

@Component({ template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class BlankPage {}

const NAVIGATION: NavItem[] = [
  { labelKey: 'Overview', icon: 'lucidePackage', route: '/', exact: true, groupKey: 'Monitoring' },
  { labelKey: 'Invoices', icon: 'lucidePackage', route: '/invoices', groupKey: 'Finance' },
  {
    labelKey: 'Reports',
    icon: 'lucidePackage',
    route: '/reports',
    roles: ['operator'],
    groupKey: 'Finance',
  },
  { labelKey: 'Settings', icon: 'lucidePackage', route: '/settings', groupKey: 'Finance' },
];

describe('ShellComponent', () => {
  async function render(layout: ShellLayout, url = '/') {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: '', component: BlankPage },
          { path: 'invoices', component: BlankPage },
          { path: 'invoices/:id', component: BlankPage },
          { path: 'settings', component: BlankPage },
        ]),
        ...provideParcelFlowTesting({ subject: subjectFor('admin') }),
      ],
    });
    const fixture = TestBed.createComponent(ShellComponent);
    fixture.componentRef.setInput('titleKey', 'Administration');
    fixture.componentRef.setInput('navigation', NAVIGATION);
    fixture.componentRef.setInput('layout', layout);
    await TestBed.inject(Router).navigateByUrl(url);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim();

  it('groups consecutive entries under their section label and hides unauthorised ones', async () => {
    const root = await render('sidebar');
    const groups = root.querySelectorAll('aside [role="group"]');

    expect(Array.from(groups, (group) => group.getAttribute('aria-label'))).toEqual([
      'Monitoring',
      'Finance',
    ]);
    expect(Array.from(groups[1].querySelectorAll('a'), text)).toEqual(['Invoices', 'Settings']);
  });

  it('shows the active section in the breadcrumb, including on nested routes', async () => {
    const root = await render('sidebar', '/invoices/42');
    const current = root.querySelector('header [aria-current="page"]');

    expect(text(current)).toBe('Invoices');
  });

  it('shows the signed-in user with initials and role in the sidebar', async () => {
    const root = await render('sidebar');
    const aside = text(root.querySelector('aside'));

    expect(aside).toContain('enum.Role.admin');
    expect(root.querySelector('aside button[aria-label="layout.logout"]')).not.toBeNull();
  });

  it('keeps the top bar layout, without sidebar nor breadcrumb, for the header layout', async () => {
    const root = await render('header');

    expect(root.querySelector('aside')).toBeNull();
    expect(root.querySelector('[aria-label="layout.breadcrumb"]')).toBeNull();
    expect(root.querySelectorAll('nav a')).toHaveLength(3);
  });

  it('renders ungrouped navigation without group semantics', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), ...provideParcelFlowTesting({ subject: subjectFor('admin') })],
    });
    const fixture = TestBed.createComponent(ShellComponent);
    fixture.componentRef.setInput('titleKey', 'Portal');
    fixture.componentRef.setInput(
      'navigation',
      NAVIGATION.map(({ groupKey: _groupKey, ...item }) => item),
    );
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('[role="group"]')).toHaveLength(
      0,
    );
  });
});
