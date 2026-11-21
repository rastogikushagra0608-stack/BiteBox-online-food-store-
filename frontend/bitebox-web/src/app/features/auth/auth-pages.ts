import { ChangeDetectionStrategy, Component, inject, isDevMode, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthStore } from '../../core/state/auth.store';
import { fieldErrors, problemMessage } from '../../core/state/toast';
import { Logo } from '../../layout/brand';

/** Mirrors the server's password rule so users get instant feedback (the server still enforces it). */
export function strongPassword(control: AbstractControl): ValidationErrors | null {
  const v = (control.value as string) ?? '';
  if (!v) return null;
  return v.length >= 8 && /[A-Z]/.test(v) && /[a-z]/.test(v) && /\d/.test(v) ? null : { weakPassword: true };
}

export const INDIAN_MOBILE = /^[6-9]\d{9}$/;

/** Only allow internal redirects after sign-in (prevents open-redirect abuse via returnUrl). */
export function safeReturnUrl(url: string | null): string | null {
  return url && url.startsWith('/') && !url.startsWith('//') ? url : null;
}

const SHELL_STYLES = `
  .auth { min-height: calc(100vh - var(--bb-header-h)); display: grid; grid-template-columns: 1fr 1fr; }
  .art { background: linear-gradient(160deg, rgba(31,27,22,.15), rgba(31,27,22,.75)), url('https://images.unsplash.com/photo-1555939594-58d7cb561ad1?auto=format&fit=crop&w=1200&q=70') center/cover; color: #fff; padding: var(--s7); display: flex; flex-direction: column; justify-content: flex-end; }
  .art h2 { color: #fff; font-size: 2.2rem; max-width: 420px; }
  .pane { display: flex; align-items: center; justify-content: center; padding: var(--s6) var(--s5); }
  form { width: min(420px, 100%); display: flex; flex-direction: column; gap: var(--s4); }
  form h1 { font-size: 2rem; margin: var(--s4) 0 0; }
  .banner { padding: 10px 14px; border-radius: 10px; background: var(--bb-danger-soft); color: var(--bb-danger); font-weight: 550; font-size: .9rem; }
  .banner.info { background: var(--bb-info-soft); color: var(--bb-info); }
  .demo { font-size: .82rem; background: var(--bb-surface-2); border-radius: 10px; padding: 10px 12px; color: var(--bb-ink-2); }
  .demo button { border: 0; background: none; color: var(--bb-primary-ink); font-weight: 700; cursor: pointer; padding: 0 2px; text-decoration: underline; }
  @media (max-width: 860px) { .auth { grid-template-columns: 1fr; } .art { display: none; } }`;

@Component({
  selector: 'bb-login',
  imports: [ReactiveFormsModule, RouterLink, Logo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="auth">
      <div class="art"><h2>Good food is one sign-in away.</h2><p>Track orders, save addresses and reorder favourites in a tap.</p></div>
      <div class="pane">
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <bb-logo />
          <h1>Welcome back</h1>
          @if (expired) { <div class="banner info">Your session ended. Please sign in again.</div> }
          @if (error()) { <div class="banner" role="alert">{{ error() }}</div> }
          <div class="field">
            <label for="email">Email</label>
            <input id="email" class="input" type="email" formControlName="email" autocomplete="email" />
            @if (form.controls.email.touched && form.controls.email.invalid) { <span class="field-error">Enter a valid email address.</span> }
          </div>
          <div class="field">
            <label for="password">Password</label>
            <input id="password" class="input" type="password" formControlName="password" autocomplete="current-password" />
            @if (form.controls.password.touched && form.controls.password.invalid) { <span class="field-error">Enter your password.</span> }
          </div>
          <label class="check"><input type="checkbox" formControlName="rememberMe" /> Keep me signed in</label>
          <button class="btn btn-primary btn-block" type="submit" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
          <p class="muted small">New to BiteBox? <a routerLink="/register" [queryParams]="{ returnUrl }" style="color:var(--bb-primary-ink);font-weight:700">Create an account</a></p>
          @if (showDemo) {
          <div class="demo">
            <strong>Demo accounts</strong> (password <code>BiteBox&#64;2026</code>):
            <button type="button" (click)="fill('priya@example.com')">customer</button>,
            <button type="button" (click)="fill('admin@bitebox.dev')">admin</button>,
            <button type="button" (click)="fill('kitchen@bitebox.dev')">kitchen</button>,
            <button type="button" (click)="fill('delivery@bitebox.dev')">delivery</button>
          </div>
          }
        </form>
      </div>
    </div>`,
  styles: SHELL_STYLES,
})
export class LoginPage {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
    rememberMe: [true],
  });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly returnUrl = safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
  protected readonly expired = this.route.snapshot.queryParamMap.has('expired');
  /** Demo shortcuts appear only in development builds, never in production. */
  protected readonly showDemo = isDevMode();

  protected fill(email: string): void {
    this.form.patchValue({ email, password: 'BiteBox@2026' });
  }

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const { email, password, rememberMe } = this.form.getRawValue();
      const me = await this.auth.login(email, password, rememberMe);
      await this.router.navigateByUrl(this.returnUrl ?? this.auth.homeFor(me));
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}

@Component({
  selector: 'bb-register',
  imports: [ReactiveFormsModule, RouterLink, Logo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="auth">
      <div class="art"><h2>Join BiteBox.</h2><p>Use code <strong>WELCOME50</strong> for ₹50 off your first order above ₹299.</p></div>
      <div class="pane">
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <bb-logo />
          <h1>Create your account</h1>
          @if (error()) { <div class="banner" role="alert">{{ error() }}</div> }
          <div class="field">
            <label for="fullName">Full name</label>
            <input id="fullName" class="input" formControlName="fullName" autocomplete="name" />
            @if (show('fullName')) { <span class="field-error">{{ serverErrors()['fullName'] ?? 'Please enter your name (at least 2 characters).' }}</span> }
          </div>
          <div class="field">
            <label for="email">Email</label>
            <input id="email" class="input" type="email" formControlName="email" autocomplete="email" />
            @if (show('email')) { <span class="field-error">{{ serverErrors()['email'] ?? 'Enter a valid email address.' }}</span> }
          </div>
          <div class="field">
            <label for="phone">Mobile number</label>
            <input id="phone" class="input" formControlName="phone" inputmode="numeric" maxlength="10" autocomplete="tel-national" placeholder="10-digit number" />
            @if (show('phone')) { <span class="field-error">{{ serverErrors()['phone'] ?? 'Enter a valid 10-digit Indian mobile number.' }}</span> }
          </div>
          <div class="field">
            <label for="password">Password</label>
            <input id="password" class="input" type="password" formControlName="password" autocomplete="new-password" />
            <span class="small" [class.field-error]="show('password')" [class.muted]="!show('password')">At least 8 characters with an uppercase letter, a lowercase letter and a digit.</span>
          </div>
          <button class="btn btn-primary btn-block" type="submit" [disabled]="busy()">{{ busy() ? 'Creating account…' : 'Create account' }}</button>
          <p class="muted small">Already have an account? <a routerLink="/login" [queryParams]="{ returnUrl }" style="color:var(--bb-primary-ink);font-weight:700">Sign in</a></p>
        </form>
      </div>
    </div>`,
  styles: SHELL_STYLES,
})
export class RegisterPage {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  protected readonly returnUrl = safeReturnUrl(inject(ActivatedRoute).snapshot.queryParamMap.get('returnUrl'));
  protected readonly form = inject(FormBuilder).nonNullable.group({
    fullName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(80)]],
    email: ['', [Validators.required, Validators.email]],
    phone: ['', [Validators.required, Validators.pattern(INDIAN_MOBILE)]],
    password: ['', [Validators.required, strongPassword]],
  });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string>>({});

  protected show(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    return (c.touched && c.invalid) || !!this.serverErrors()[name];
  }

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    this.serverErrors.set({});
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.register(this.form.getRawValue());
      await this.router.navigateByUrl(this.returnUrl ?? '/');
    } catch (e) {
      this.serverErrors.set(fieldErrors(e));
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
