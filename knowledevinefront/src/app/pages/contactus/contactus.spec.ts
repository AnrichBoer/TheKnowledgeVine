import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Contactus } from './contactus';

describe('Contactus', () => {
  let component: Contactus;
  let fixture: ComponentFixture<Contactus>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Contactus],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Contactus);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('requires valid contact details and privacy consent', () => {
    component.submitForm();
    fixture.detectChanges();

    expect(component.contactForm.invalid).toBeTrue();
    expect(component.submissionStatus).toBe('error');
    expect(component.statusMessage).toContain('highlighted fields');
  });

  it('uses useful mobile input types and autocomplete attributes', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('input[type="tel"][autocomplete="tel"]')).toBeTruthy();
    expect(compiled.querySelector('input[type="email"][autocomplete="email"]')).toBeTruthy();
  });
});
