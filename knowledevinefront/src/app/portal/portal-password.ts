import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PortalAuth, portalError } from './portal-auth';
import { PortalReloadSafety } from './portal-reload-safety';

@Component({ selector: 'app-portal-password', imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './portal-password.html', styleUrl: './portal-auth.scss' })
export class PortalPassword {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(PortalAuth);
  private readonly document = inject(DOCUMENT);
  readonly recovery = this.route.snapshot.data['mode'] === 'recovery';
  readonly initial = this.route.snapshot.data['mode'] === 'setup';
  readonly busy = signal(false); readonly error = signal(''); readonly sent = signal('');
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(128), Validators.pattern(/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).+$/)]],
    confirmation: ['', Validators.required],
  });
  private readonly fragment = new URLSearchParams(this.route.snapshot.fragment ?? '');
  private readonly userId = this.fragment.get('userId') ?? '';
  private readonly token = this.fragment.get('token') ?? '';
  readonly validLink = !!this.userId && !!this.token;

  constructor() {
    inject(PortalReloadSafety).protect(() => this.busy() || this.form.dirty || (!this.recovery && this.validLink), inject(DestroyRef), this.form.events);
    if (!this.recovery) {
      const window = this.document.defaultView;
      // Keep the link only in this screen's memory, never in storage/history/referrers.
      window?.history.replaceState(window.history.state, '', window.location.pathname);
    }
  }

  async submit() {
    const controls = this.form.controls;
    if (this.recovery ? controls.email.invalid : controls.password.invalid || controls.confirmation.invalid) {
      this.form.markAllAsTouched(); return;
    }
    if (!this.recovery && controls.password.value !== controls.confirmation.value) {
      this.error.set('The passwords do not match. Please try again.'); return;
    }
    if (this.busy()) return;
    this.error.set(''); this.busy.set(true);
    try {
      if (this.recovery) this.sent.set(await this.auth.recover(controls.email.value.trim()));
      else {
        await this.auth.setPassword(this.userId, this.token, controls.password.value, this.initial);
        controls.password.reset(); controls.confirmation.reset();
        await this.router.navigate(['/portal/login'], { queryParams: { passwordSet: 1 } });
      }
    } catch (error) { this.error.set(portalError(error)); }
    finally { this.busy.set(false); }
  }
}
