import { Component, DestroyRef, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PortalAuth, portalError } from './portal-auth';
import { PortalNoteMemory } from './portal-note-memory';
import { PortalReloadSafety } from './portal-reload-safety';

@Component({
  selector: 'app-portal-login', imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './portal-login.html', styleUrl: './portal-auth.scss',
})
export class PortalLogin {
  readonly auth = inject(PortalAuth);
  readonly notes = inject(PortalNoteMemory);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly showPassword = signal(false);
  readonly passwordSet = this.route.snapshot.queryParamMap.get('passwordSet') === '1';
  readonly unavailable = this.route.snapshot.queryParamMap.has('unavailable');
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.maxLength(128)]],
  });
  constructor() { inject(PortalReloadSafety).protect(() => this.busy() || this.form.dirty, inject(DestroyRef), this.form.events); }

  async submit() {
    if (this.form.invalid || this.busy()) { this.form.markAllAsTouched(); return; }
    this.busy.set(true); this.error.set('');
    try {
      const values = this.form.getRawValue();
      const session = await this.auth.login(values.email.trim(), values.password);
      this.form.controls.password.reset();
      await this.router.navigateByUrl(this.notes.resumeUrl(session.id, session.roles) ?? this.auth.destination(session));
    } catch (error) { this.error.set(portalError(error)); }
    finally { this.busy.set(false); }
  }
}
