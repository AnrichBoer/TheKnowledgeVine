import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Aboutus } from './aboutus';

describe('Aboutus', () => {
  let component: Aboutus;
  let fixture: ComponentFixture<Aboutus>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Aboutus],
      providers: [provideZonelessChangeDetection(), provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(Aboutus);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
