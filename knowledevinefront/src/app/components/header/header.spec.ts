import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Header } from './header';

describe('Header', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Header],
      providers: [provideZonelessChangeDetection(), provideRouter([])],
    }).compileComponents();
  });
  it('opens the mobile navigation and closes it when About Us is selected', () => {
    const fixture = TestBed.createComponent(Header);
    fixture.detectChanges();
    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    const toggle = fixture.nativeElement.querySelector('.menu-toggle');
    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    const about = fixture.nativeElement.querySelector('nav a[href="/about"]');
    about.click();
    fixture.detectChanges();
    expect(navigate).toHaveBeenCalled();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
  });
});
