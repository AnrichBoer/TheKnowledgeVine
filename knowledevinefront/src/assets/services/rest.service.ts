import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';


@Injectable({ providedIn: 'root' })
export class RestService {
  lab: boolean = true;
  hostName: string = '';
  url: string = '';
  httpData: Object = {};
  isDevelopment: boolean = true;

  constructor(private http: HttpClient) {
    // localStorage.setItem('targetHostName', 'localhost');
    // this.hostName = localStorage.getItem('targetHostName') || 'localhost';

    if (this.isDevelopment) {
      this.url = `https://localhost:7064`;
    } else {
      this.url = `https://theknowledgevinemontessori/api`; 
    }
  }
  getHttpOptions(isMultipart: boolean = false) {
    const token = localStorage.getItem('jwt');

    const headersConfig: any = {
      'Access-Control-Allow-Origin': '*'
    };

    if (!isMultipart) {
      headersConfig['Content-Type'] = 'application/json';
    }

    if (token) {
      headersConfig['Authorization'] = `Bearer ${token}`;
    }

    return {
      headers: new HttpHeaders(headersConfig)
    };
  }


  // GET Request
  sendGetRequest(target: string, methodName: string,) {
    return this.http.get(`${this.url}/${target}/${methodName}`, this.getHttpOptions());
  }
  sendPostRequest(
    target: string,
    methodName: string,
    payload: any,
    isMultipart: boolean = false
  ) {
    const options = this.getHttpOptions(isMultipart);

    return this.http.post(
      `${this.url}/${target}/${methodName}`,
      payload,
      options
    );
  }
}
