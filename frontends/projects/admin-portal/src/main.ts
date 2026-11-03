import { bootstrapPortal } from '@parcelflow/shared';
import { App } from './app/app';
import { appConfig } from './app/app.config';

void bootstrapPortal(
  App,
  { portal: 'admin', keycloakOnLoad: 'login-required', mockDefaultSubject: 'admin-david' },
  appConfig,
);
