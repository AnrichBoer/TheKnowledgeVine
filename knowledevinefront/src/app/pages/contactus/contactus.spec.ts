import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Subject } from 'rxjs';
import { Contactus } from './contactus';
import { EmailResponse, EmailService } from '../../services/email';
import { OPEN_DAY } from '../../open-day.config';

describe('Contactus', () => {
  let fixture: ComponentFixture<Contactus>;
  let component: Contactus;
  let sender: jasmine.SpyObj<EmailService>;
  let response: Subject<EmailResponse>;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  const enquiry = {
    name: 'Parent',
    contactNumber: '0613041993',
    email: 'parent@example.com',
    message: 'Can we arrange a visit?',
  };
  beforeEach(async () => {
    sender = jasmine.createSpyObj<EmailService>('EmailService', ['sendEmail']);
    response = new Subject<EmailResponse>();
    sender.sendEmail.and.returnValue(response);
    params = new BehaviorSubject(convertToParamMap({}));
    await TestBed.configureTestingModule({
      imports: [Contactus],
      providers: [
        provideZonelessChangeDetection(),
        { provide: EmailService, useValue: sender },
        { provide: ActivatedRoute, useValue: { queryParamMap: params } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(Contactus);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });
  it('shows validation errors instead of sending an empty enquiry', () => {
    component.submitForm();
    fixture.detectChanges();
    expect(sender.sendEmail).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelectorAll('mat-error').length).toBe(4);
  });
  it('rejects whitespace-only names and messages', () => {
    component.contactForm.setValue({ ...enquiry, name: '   ', message: '   ' });
    component.submitForm();
    expect(sender.sendEmail).not.toHaveBeenCalled();
  });
  it('prevents duplicate submissions while sending', () => {
    component.contactForm.setValue(enquiry);
    component.submitForm();
    component.submitForm();
    fixture.detectChanges();
    expect(sender.sendEmail).toHaveBeenCalledTimes(1);
    expect(component.sending()).toBeTrue();
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBeTrue();
  });
  it('retains the message and shows failure when the existing API returns false', () => {
    component.contactForm.setValue(enquiry);
    component.submitForm();
    response.next(false);
    response.complete();
    fixture.detectChanges();
    expect(component.status()).toBe('error');
    expect(component.contactForm.getRawValue()).toEqual(enquiry);
    expect(component.sending()).toBeFalse();
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain(
      'could not send',
    );
  });
  it('retains the message after an HTTP failure and permits retry', () => {
    component.contactForm.setValue(enquiry);
    component.submitForm();
    response.error(new Error('503'));
    expect(component.status()).toBe('error');
    expect(component.contactForm.getRawValue()).toEqual(enquiry);
    expect(component.sending()).toBeFalse();
    response = new Subject<EmailResponse>();
    sender.sendEmail.and.returnValue(response);
    component.submitForm();
    expect(sender.sendEmail).toHaveBeenCalledTimes(2);
  });
  it('clears the form only after confirmed success', () => {
    component.contactForm.setValue(enquiry);
    component.submitForm();
    response.next({ success: true });
    response.complete();
    fixture.detectChanges();
    expect(component.status()).toBe('success');
    expect(component.contactForm.controls.message.value).toBe('');
    expect(fixture.nativeElement.querySelectorAll('mat-error').length).toBe(0);
  });
  it('recognises a successful response from the existing API', () => {
    component.contactForm.setValue(enquiry);
    component.submitForm();
    response.next(true);
    response.complete();
    expect(component.status()).toBe('success');
  });
  it('prefills an open day enquiry without sending it', () => {
    spyOn(Date, 'now').and.returnValue(Date.parse('2026-10-09T12:00:00+02:00'));
    params.next(convertToParamMap({ enquiry: 'open-day' }));
    expect(component.contactForm.controls.message.value).toBe(OPEN_DAY.enquiryMessage);
    expect(component.openDayEnquiry()).toBeTrue();
    expect(sender.sendEmail).not.toHaveBeenCalled();
  });
  it('preserves an existing draft when the open day link is used', () => {
    spyOn(Date, 'now').and.returnValue(Date.parse('2026-10-09T12:00:00+02:00'));
    component.contactForm.controls.message.setValue('My own enquiry');
    params.next(convertToParamMap({ enquiry: 'open-day' }));
    expect(component.contactForm.controls.message.value).toBe('My own enquiry');
  });
  it('does not prefill a promotion after it has expired', () => {
    spyOn(Date, 'now').and.returnValue(Date.parse(OPEN_DAY.endsAt));
    params.next(convertToParamMap({ enquiry: 'open-day' }));
    expect(component.openDayEnquiry()).toBeFalse();
    expect(component.contactForm.controls.message.value).toBe('');
  });
});
