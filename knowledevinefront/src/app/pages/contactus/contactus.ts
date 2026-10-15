import { Component, DestroyRef, inject, signal, ViewChild } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import {
  FormBuilder,
  FormGroupDirective,
  ReactiveFormsModule,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { EmailService } from '../../services/email';
import { OPEN_DAY, isOpenDayActive } from '../../open-day.config';

const notBlank: ValidatorFn = (control) =>
  typeof control.value === 'string' && !control.value.trim() ? { required: true } : null;

@Component({
  selector: 'app-contactus',
  imports: [MatIconModule, ReactiveFormsModule, MatInputModule, MatFormFieldModule],
  templateUrl: './contactus.html',
  styleUrl: './contactus.scss',
})
export class Contactus {
  private readonly fb = inject(FormBuilder);
  private readonly emailService = inject(EmailService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  @ViewChild(FormGroupDirective) private formDirective?: FormGroupDirective;
  readonly sending = signal(false);
  readonly status = signal<'idle' | 'success' | 'error'>('idle');
  readonly feedback = signal('');
  readonly openDayEnquiry = signal(false);
  readonly contactForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, notBlank, Validators.maxLength(100)]],
    contactNumber: [
      '',
      [Validators.required, notBlank, Validators.minLength(7), Validators.maxLength(30)],
    ],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    message: ['', [Validators.required, notBlank, Validators.maxLength(4000)]],
  });

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const openDay = params.get('enquiry') === 'open-day' && isOpenDayActive();
      this.openDayEnquiry.set(openDay);
      if (openDay && !this.contactForm.controls.message.value.trim()) {
        this.contactForm.controls.message.setValue(OPEN_DAY.enquiryMessage);
      }
    });
  }

  submitForm() {
    if (this.sending()) return;
    this.status.set('idle');
    this.feedback.set('');
    this.contactForm.markAllAsTouched();
    if (this.contactForm.invalid) return;
    const values = this.contactForm.getRawValue();
    const payload = {
      name: values.name.trim(),
      contactNumber: values.contactNumber.trim(),
      email: values.email.trim(),
      message: values.message.trim(),
    };
    this.sending.set(true);
    this.emailService
      .sendEmail(payload)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.sending.set(false)),
      )
      .subscribe({
        next: (result) => {
          // Support the existing API during rollout, but never treat a false result as success.
          if (result !== true && (typeof result !== 'object' || result?.success !== true)) {
            this.showFailure();
            return;
          }
          this.status.set('success');
          this.feedback.set(
            'Thank you! Your enquiry has been sent. We look forward to speaking with you.',
          );
          this.contactForm.reset();
          this.formDirective?.resetForm(this.contactForm.getRawValue());
        },
        error: () => this.showFailure(),
      });
  }

  private showFailure() {
    this.status.set('error');
    this.feedback.set(
      'We could not send your enquiry. Your message is still here — please try again, or call us on 061 304 1993.',
    );
  }
}
