import { bootstrapPortal } from '@parcelflow/shared';
import { App } from './app/app';
import { appConfig } from './app/app.config';

void bootstrapPortal(
  App,
  { portal: 'customer', keycloakOnLoad: 'check-sso', mockDefaultSubject: 'customer-alice' },
  appConfig,
);
