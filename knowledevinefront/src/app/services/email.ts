import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root',
})
export class EmailService {

  // private apiUrl = 'https://localhost:7207/Email/SendEmail';
  private apiUrl = 'https://knowledgevinemontessori.co.za/api/Email/SendEmail';

  constructor(private http: HttpClient) {}

  sendEmail(data: any) {
    return this.http.post(this.apiUrl, data);
  }
}
