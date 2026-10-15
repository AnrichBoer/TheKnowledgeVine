import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface EmailRequest {
  name: string;
  contactNumber: string;
  email: string;
  message: string;
}
export type EmailResponse = boolean | { success: boolean; message?: string };

@Injectable({ providedIn: 'root' })
export class EmailService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = '/api/Email/SendEmail';
  sendEmail(data: EmailRequest) {
    return this.http.post<EmailResponse>(this.apiUrl, data);
  }
}
