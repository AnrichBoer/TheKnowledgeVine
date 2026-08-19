import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface ContactPayload {
  name: string;
  contactNumber: string;
  email: string;
  message: string;
  privacyAccepted: boolean;
  website: string;
}

export interface ContactResponse {
  message: string;
}

@Injectable({ providedIn: 'root' })
export class EmailService {
  private readonly apiUrl = '/api/Email/SendEmail';

  constructor(private readonly http: HttpClient) {}

  sendEmail(data: ContactPayload): Observable<ContactResponse> {
    return this.http.post<ContactResponse>(this.apiUrl, data);
  }
}
