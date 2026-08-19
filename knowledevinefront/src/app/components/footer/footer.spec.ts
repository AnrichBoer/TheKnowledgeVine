import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Footer } from './footer';

describe('Footer', () => {
  let fixture: ComponentFixture<Footer>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Footer],
      providers: [provideZonelessChangeDetection(), provideRouter([])],
    }).compileComponents();
    fixture = TestBed.createComponent(Footer);
    fixture.detectChanges();
  });

  it('offers direct contact and privacy links', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('a[href^="tel:"]')).toBeTruthy();
    expect(compiled.querySelector('a[href^="mailto:"]')).toBeTruthy();
    expect(compiled.textContent).toContain('Privacy Notice');
  });
});
