import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCompass, lucideHouse, lucideLock } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';

@Component({
  selector: 'pf-forbidden-page',
  imports: [RouterLink, TranslocoPipe, NgIcon, HlmButtonImports, HlmEmptyImports],
  providers: [provideIcons({ lucideLock, lucideHouse })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div hlmEmpty class="mt-12">
      <div hlmEmptyHeader>
        <div hlmEmptyMedia variant="icon"><ng-icon name="lucideLock" /></div>
        <h1 hlmEmptyTitle>{{ 'pages.forbidden.title' | transloco }}</h1>
        <p hlmEmptyDescription>{{ 'pages.forbidden.message' | transloco }}</p>
      </div>
      <div hlmEmptyContent>
        <a hlmBtn routerLink="/"><ng-icon name="lucideHouse" />{{ 'pages.home' | transloco }}</a>
      </div>
    </div>
  `,
})
export class ForbiddenPage {}

@Component({
  selector: 'pf-not-found-page',
  imports: [RouterLink, TranslocoPipe, NgIcon, HlmButtonImports, HlmEmptyImports],
  providers: [provideIcons({ lucideCompass, lucideHouse })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div hlmEmpty class="mt-12">
      <div hlmEmptyHeader>
        <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCompass" /></div>
        <h1 hlmEmptyTitle>{{ 'pages.notFound.title' | transloco }}</h1>
        <p hlmEmptyDescription>{{ 'pages.notFound.message' | transloco }}</p>
      </div>
      <div hlmEmptyContent>
        <a hlmBtn routerLink="/"><ng-icon name="lucideHouse" />{{ 'pages.home' | transloco }}</a>
      </div>
    </div>
  `,
})
export class NotFoundPage {}
