import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';
import { defer, finalize, tap, throwError } from 'rxjs';
import { PortalReloadSafety } from './portal-reload-safety';

@Injectable({ providedIn: 'root' })
export class PortalConnection {
  private readonly document = inject(DOCUMENT);
  readonly origin = this.document.location.origin;
  readonly online = signal(this.document.defaultView?.navigator.onLine !== false);
  readonly unavailable = signal(false);
  readonly disconnected = computed(() => !this.online() || this.unavailable());
  constructor() {
    const offline = () => this.online.set(false);
    const online = () => { this.online.set(true); this.unavailable.set(false); };
    this.document.defaultView?.addEventListener('offline', offline);
    this.document.defaultView?.addEventListener('online', online);
    inject(DestroyRef).onDestroy(() => {
      this.document.defaultView?.removeEventListener('offline', offline);
      this.document.defaultView?.removeEventListener('online', online);
    });
  }
}

export const portalNetworkInterceptor: HttpInterceptorFn = (request, next) => {
  const connection = inject(PortalConnection);
  const safety = inject(PortalReloadSafety);
  const url = new URL(request.url, connection.origin);
  if (url.origin !== connection.origin || !url.pathname.startsWith('/api/')) return next(request);
  // Explicit bypass remains effective even if a future cache rule becomes broader.
  const live = request.clone({ setHeaders: { 'ngsw-bypass': 'true' } });
  if (!connection.online()) return throwError(() => new HttpErrorResponse({ status: 0, statusText: 'Offline', url: request.url }));
  return defer(() => {
    const mutation = !['GET', 'HEAD', 'OPTIONS'].includes(request.method);
    if (mutation) safety.writes.update(n => n + 1);
    return next(live).pipe(tap({
      next: () => connection.unavailable.set(false),
      error: (error: unknown) => connection.unavailable.set(error instanceof HttpErrorResponse && [0, 502, 503, 504].includes(error.status)),
    }), finalize(() => { if (mutation) safety.writes.update(n => n - 1); }));
  });
};
