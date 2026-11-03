import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { provideParcelFlowTesting, subjectFor } from '../../testing';
import { AuthService } from './auth.service';
import { roleGuard } from './role.guard';

describe('roleGuard', () => {
  function run(subject: string | null, ...roles: Parameters<typeof roleGuard>) {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), ...provideParcelFlowTesting({ subject })],
    });
    const guard = roleGuard(...roles);
    return TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url: '/target' } as RouterStateSnapshot),
    ) as Promise<boolean | UrlTree>;
  }

  it('allows a user holding one of the roles', async () => {
    await expect(run(subjectFor('operator'), 'operator', 'admin')).resolves.toBe(true);
  });

  it('redirects users without the role to /forbidden', async () => {
    const result = await run(subjectFor('customer'), 'admin');
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/forbidden');
  });

  it('signs anonymous visitors in with the default mock identity', async () => {
    const result = await run(null, 'customer');
    expect(result).toBe(true);
    expect(TestBed.inject(AuthService).authenticated()).toBe(true);
  });
});
