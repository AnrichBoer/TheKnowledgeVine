import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Header } from './header';

describe('Header', () => {
  let component: Header;
  let fixture: ComponentFixture<Header>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Header],
      providers: [provideZonelessChangeDetection(), provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(Header);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('contains semantic navigation links', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    const labels = Array.from(compiled.querySelectorAll('.desktop-nav a')).map((link) =>
      link.textContent?.trim(),
    );

    expect(labels).toEqual(['Home', 'About Us', 'Contact Us']);
  });

  it('opens and closes the mobile navigation', () => {
    component.toggleMenu();
    expect(component.menuOpen).toBeTrue();
    component.closeMenu();
    expect(component.menuOpen).toBeFalse();
  });
});
