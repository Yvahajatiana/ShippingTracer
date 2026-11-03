import { ApplicationConfig } from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideIcons } from '@ng-icons/core';
import { AppConfig } from '@parcelflow/shared';
import { routes } from './app.routes';
import { NAVIGATION_ICONS } from './navigation';

export const appConfig = (_config: AppConfig): ApplicationConfig => ({
  providers: [
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top' }),
    ),
    provideIcons(NAVIGATION_ICONS),
  ],
});
