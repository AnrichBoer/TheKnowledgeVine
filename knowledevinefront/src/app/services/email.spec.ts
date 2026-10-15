import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EmailService } from './email';

describe('EmailService', () => {
  let service: EmailService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(EmailService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('posts the enquiry and preserves a false API result', () => {
    const payload = {
      name: 'Parent',
      contactNumber: '0613041993',
      email: 'parent@example.com',
      message: 'A visit please.',
    };
    service.sendEmail(payload).subscribe((response) => expect(response).toBeFalse());
    const request = http.expectOne('/api/Email/SendEmail');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(payload);
    request.flush(false);
  });
  it('propagates an SMTP failure response', () => {
    service
      .sendEmail({
        name: 'Parent',
        contactNumber: '0613041993',
        email: 'parent@example.com',
        message: 'Hello.',
      })
      .subscribe({
        next: () => fail('Expected a failed request'),
        error: (error) => expect(error.status).toBe(503),
      });
    http
      .expectOne('/api/Email/SendEmail')
      .flush({ success: false }, { status: 503, statusText: 'Service Unavailable' });
  });
});
