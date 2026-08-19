import { HttpErrorResponse } from '@angular/common/http';
import { Component } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ContactPayload, EmailService } from '../../services/email';

type SubmissionStatus = 'idle' | 'sending' | 'success' | 'error';

@Component({
  selector: 'app-contactus',
  imports: [
    MatIconModule,
    ReactiveFormsModule,
    MatInputModule,
    MatFormFieldModule,
    MatButtonModule,
    MatCheckboxModule,
    RouterLink,
  ],
  templateUrl: './contactus.html',
  styleUrl: './contactus.scss',
})
export class Contactus {
  readonly contactForm;
  submissionStatus: SubmissionStatus = 'idle';
  statusMessage = '';

  constructor(
    private readonly fb: FormBuilder,
    private readonly emailService: EmailService,
  ) {
    this.contactForm = this.fb.nonNullable.group({
      name: ['', [Validators.required, Validators.maxLength(100)]],
      contactNumber: ['', [Validators.required, Validators.maxLength(30)]],
      email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
      message: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(2000)]],
      privacyAccepted: [false, Validators.requiredTrue],
      website: [''],
    });
  }

  submitForm(): void {
    this.submissionStatus = 'idle';
    this.statusMessage = '';

    if (this.contactForm.invalid) {
      this.contactForm.markAllAsTouched();
      this.statusMessage = 'Please check the highlighted fields before sending your message.';
      this.submissionStatus = 'error';
      return;
    }

    this.submissionStatus = 'sending';
    const payload: ContactPayload = this.contactForm.getRawValue();

    this.emailService
      .sendEmail(payload)
      .pipe(
        finalize(() => {
          if (this.submissionStatus === 'sending') this.submissionStatus = 'idle';
        }),
      )
      .subscribe({
        next: (response) => {
          this.statusMessage = response.message || 'Thank you. Your enquiry has been sent.';
          this.submissionStatus = 'success';
          this.contactForm.reset({
            name: '',
            contactNumber: '',
            email: '',
            message: '',
            privacyAccepted: false,
            website: '',
          });
        },
        error: (error: HttpErrorResponse) => {
          if (error.status === 429) {
            this.statusMessage = 'Too many messages were sent from this connection. Please wait a few minutes or contact us by phone.';
          } else {
            this.statusMessage = 'We could not send your message right now. Please call, email or WhatsApp us instead.';
          }
          this.submissionStatus = 'error';
        },
      });
  }
}
