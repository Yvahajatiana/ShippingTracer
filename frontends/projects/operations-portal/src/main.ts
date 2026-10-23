import { bootstrapPortal } from '@parcelflow/shared';
import { App } from './app/app';
import { appConfig } from './app/app.config';

void bootstrapPortal(
  App,
  { portal: 'operations', keycloakOnLoad: 'login-required', mockDefaultSubject: 'operator-chloe' },
  appConfig,
);
