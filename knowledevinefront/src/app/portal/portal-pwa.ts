import { DOCUMENT } from '@angular/common';
import { DestroyRef, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SwUpdate } from '@angular/service-worker';
import { PortalConnection } from './portal-connection';
import { PortalReloadSafety } from './portal-reload-safety';

export const PORTAL_RELOAD = new InjectionToken<() => void>('Reload the portal', {
  providedIn: 'root', factory: () => { const document = inject(DOCUMENT); return () => document.defaultView?.location.reload(); },
});
interface InstallPrompt extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

@Injectable({ providedIn: 'root' })
export class PortalPwa {
  private readonly document = inject(DOCUMENT);
  private readonly updates = inject(SwUpdate, { optional: true });
  private readonly destroy = inject(DestroyRef);
  private readonly reload = inject(PORTAL_RELOAD);
  readonly connection = inject(PortalConnection);
  readonly safety = inject(PortalReloadSafety);
  readonly helpOpen = signal(false);
  readonly installAvailable = signal(false);
  readonly installing = signal(false);
  readonly updateReady = signal(false);
  readonly updateFailed = signal(false);
  readonly installNotice = signal('');
  readonly standalone = signal(false);
  readonly preview = ['localhost', '127.0.0.1', '[::1]'].includes(this.document.location.hostname);
  readonly secure = this.document.defaultView?.isSecureContext ?? false;
  private prompt?: InstallPrompt;
  private lastCheck = 0;

  constructor() {
    const window = this.document.defaultView;
    const display = window?.matchMedia('(display-mode: standalone)');
    const mode = () => this.standalone.set(display?.matches === true || (window?.navigator as Navigator & { standalone?: boolean })?.standalone === true);
    mode(); display?.addEventListener('change', mode);
    const beforeInstall = (event: Event) => { event.preventDefault(); this.prompt = event as InstallPrompt; this.installAvailable.set(true); };
    const installed = () => { this.prompt = undefined; this.installAvailable.set(false); this.standalone.set(true); this.helpOpen.set(false); };
    const foreground = () => {
      if (this.document.visibilityState === 'visible' && this.document.location.pathname.startsWith('/portal') && this.connection.online()) void this.checkForUpdate();
    };
    window?.addEventListener('beforeinstallprompt', beforeInstall);
    window?.addEventListener('appinstalled', installed);
    this.document.addEventListener('visibilitychange', foreground);
    if (this.updates?.isEnabled) {
      this.updates.versionUpdates.pipe(takeUntilDestroyed(this.destroy)).subscribe(event => {
        if (event.type === 'VERSION_READY') { this.updateReady.set(true); this.updateFailed.set(false); }
        if (event.type === 'VERSION_INSTALLATION_FAILED') this.updateFailed.set(true);
      });
      this.updates.unrecoverable.pipe(takeUntilDestroyed(this.destroy)).subscribe(() => this.updateReady.set(true));
    }
    this.destroy.onDestroy(() => {
      display?.removeEventListener('change', mode);
      window?.removeEventListener('beforeinstallprompt', beforeInstall);
      window?.removeEventListener('appinstalled', installed);
      this.document.removeEventListener('visibilitychange', foreground);
      this.prompt = undefined;
    });
  }
  async install() {
    const prompt = this.prompt;
    if (!prompt || this.installing() || !this.connection.online()) return;
    this.prompt = undefined; this.installAvailable.set(false); this.installing.set(true); this.installNotice.set('');
    try {
      await prompt.prompt();
      const result = await prompt.userChoice;
      this.installNotice.set(result.outcome === 'accepted' ? 'Follow your browser’s prompts to finish adding the app.' : 'You can keep using the portal in your browser and add it later.');
    } catch { this.installNotice.set('Use your browser’s menu to add the app, or keep using the portal here.'); }
    finally { this.installing.set(false); }
  }
  async checkForUpdate() {
    if (!this.updates?.isEnabled || !this.connection.online() || Date.now() - this.lastCheck < 15 * 60_000) return;
    this.lastCheck = Date.now();
    try { await this.updates.checkForUpdate(); }
    catch { this.updateFailed.set(true); }
  }
  applyUpdate() {
    if (!this.updateReady() || this.connection.disconnected() || !this.safety.canReload()) return;
    // Reload into a complete version; do not activate a new worker under old JS.
    this.reload();
  }
}
