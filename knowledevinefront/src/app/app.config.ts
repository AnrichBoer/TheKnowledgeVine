import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
  isDevMode,
} from '@angular/core';
import { provideRouter, TitleStrategy, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideServiceWorker } from '@angular/service-worker';
import { portalNetworkInterceptor } from './portal/portal-connection';

import { routes } from './app.routes';
import { SchoolTitleStrategy } from './services/page-title';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(
      routes,
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' }),
    ),
    { provide: TitleStrategy, useClass: SchoolTitleStrategy },
    provideHttpClient(withInterceptors([portalNetworkInterceptor])),
    provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode(), scope: '/portal/', registrationStrategy: 'registerWhenStable:30000' }),
  ],
};
