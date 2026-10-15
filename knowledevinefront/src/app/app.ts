import { afterNextRender, Component, inject, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { NavigationEnd, NavigationStart, Router, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Header } from './components/header/header';
import { Footer } from './components/footer/footer';
import { OpenDayService } from './services/open-day';
import { PortalTools } from './portal/portal-tools';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Header, Footer, PortalTools],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  readonly openDay = inject(OpenDayService);
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);
  readonly portal = signal(this.document.defaultView?.location.pathname.startsWith('/portal') ?? this.router.url.startsWith('/portal'));
  constructor() {
    this.router.events.pipe(takeUntilDestroyed()).subscribe(event => {
      if (event instanceof NavigationStart && event.url.startsWith('/portal')) {
        this.portal.set(true); this.openDay.setSuppressed(true);
      }
      if (event instanceof NavigationEnd) {
        this.portal.set(event.urlAfterRedirects.startsWith('/portal'));
        this.openDay.setSuppressed(this.portal());
        let robots = this.document.querySelector<HTMLMetaElement>('meta[name="robots"]');
        if (this.portal()) {
          if (!robots) { robots = this.document.createElement('meta'); robots.name = 'robots'; this.document.head.appendChild(robots); }
          robots.content = 'noindex, nofollow';
        } else robots?.remove();
      }
    });
    afterNextRender(() => { this.openDay.setSuppressed(this.portal()); this.openDay.schedule(); });
  }
  skipToContent(event: Event) {
    event.preventDefault();
    const main = this.document.getElementById('main-content');
    main?.focus();
    main?.scrollIntoView({ block: 'start' });
  }
}
