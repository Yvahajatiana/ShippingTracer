import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLogIn, lucideLogOut, lucideMenu, lucidePackage } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmToasterImports } from '@spartan-ng/helm/sonner';
import { AuthService, Role } from '../auth/auth.service';
import { MockAuthService } from '../auth/mock-auth.service';
import { LANGUAGES, LanguageService, isLanguage } from '../i18n/language.service';
import { ConfirmDialogComponent } from '../ui/confirm.service';

export interface NavItem {
  labelKey: string;
  /** Lucide icon name, e.g. `lucidePackage`; the portal must register it with provideIcons. */
  icon: string;
  route: string;
  /** Roles allowed to see the entry; omitted means visible to everyone, including anonymous visitors. */
  roles?: Role[];
  exact?: boolean;
}

/** Application frame shared by the three portals. */
@Component({
  selector: 'pf-shell',
  imports: [
    NgTemplateOutlet,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmLabelImports,
    HlmNativeSelectImports,
    HlmSheetImports,
    HlmToasterImports,
    ConfirmDialogComponent,
  ],
  // Navigation icons are registered by each portal at application level (provideIcons in app.config).
  providers: [provideIcons({ lucideLogIn, lucideLogOut, lucideMenu, lucidePackage })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.component.html',
})
export class ShellComponent {
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);
  protected readonly language = inject(LanguageService);

  readonly titleKey = input.required<string>();
  readonly navigation = input.required<NavItem[]>();

  protected readonly languages = LANGUAGES;
  protected readonly mockAuth = this.auth instanceof MockAuthService ? this.auth : null;
  protected readonly mockIdentities = this.mockAuth?.identities ?? [];

  protected readonly visibleNavigation = computed(() => {
    const roles = this.auth.roles();
    return this.navigation().filter(
      (item) => !item.roles || item.roles.some((role) => roles.includes(role)),
    );
  });

  protected changeLanguage(value: string | null | undefined): void {
    if (isLanguage(value)) {
      void this.language.use(value);
    }
  }

  protected switchIdentity(subject: string | null | undefined): void {
    if (!this.mockAuth || !subject) {
      return;
    }
    this.mockAuth.signInAs(subject);
    // Reload the current route so guards and stores pick up the new identity.
    const url = this.router.url;
    void this.router
      .navigateByUrl('/', { skipLocationChange: true })
      .then(() => this.router.navigateByUrl(url));
  }

  protected login(): void {
    void this.auth.login(this.router.url);
  }

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/');
  }
}
