import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { OpenDayService } from './services/open-day';
import { OPEN_DAY } from './open-day.config';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        {
          provide: OpenDayService,
          useValue: { event: OPEN_DAY, active: signal(true), schedule: () => {}, open: () => {}, setSuppressed: () => {} },
        },
      ],
    }).compileComponents();
  });
  it('renders the school navigation, promotion, main content, and footer', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('nav[aria-label="Main navigation"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.open-day-banner').textContent).toContain(
      '31 October 2026',
    );
    expect(fixture.nativeElement.querySelector('main')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('footer').textContent).toContain(
      'Knowledge Vine Montessori',
    );
  });
});
