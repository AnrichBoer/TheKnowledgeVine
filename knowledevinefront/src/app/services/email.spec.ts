import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ContactPayload, EmailService } from './email';

describe('EmailService', () => {
  let service: EmailService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(EmailService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('posts enquiries to the same-origin API route', () => {
    const payload: ContactPayload = {
      name: 'Test Parent',
      contactNumber: '0123456789',
      email: 'parent@example.com',
      message: 'I would like to arrange a visit.',
      privacyAccepted: true,
      website: '',
    };

    service.sendEmail(payload).subscribe((response) => expect(response.message).toContain('sent'));

    const request = http.expectOne('/api/Email/SendEmail');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(payload);
    request.flush({ message: 'Your enquiry has been sent.' });
  });
});
