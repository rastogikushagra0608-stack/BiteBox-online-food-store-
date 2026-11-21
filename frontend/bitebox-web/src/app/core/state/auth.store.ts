import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthApi } from '../api/api';
import { Me, Role } from '../api/models';

/**
 * Holds the signed-in user as a signal. The session itself is an HttpOnly cookie the browser manages;
 * Angular never sees or stores a token. `undefined` means "not checked yet".
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private api = inject(AuthApi);
  private router = inject(Router);

  readonly user = signal<Me | null | undefined>(undefined);
  readonly ready = computed(() => this.user() !== undefined);
  readonly isLoggedIn = computed(() => !!this.user());
  readonly isAdmin = computed(() => this.hasRole('Admin'));
  readonly isStaff = computed(() => this.hasRole('Admin') || this.hasRole('Kitchen') || this.hasRole('Delivery'));
  readonly firstName = computed(() => this.user()?.fullName.split(' ')[0] ?? '');

  hasRole(role: Role): boolean { return this.user()?.roles.includes(role) ?? false; }

  /** Runs once at app start (provideAppInitializer): obtain a CSRF token, then restore the session if any. */
  async init(): Promise<void> {
    try { await firstValueFrom(this.api.csrf()); } catch { /* API offline: pages show their own errors */ }
    await this.refreshUser();
  }

  async refreshUser(): Promise<void> {
    try {
      this.user.set(await firstValueFrom(this.api.me()));
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.status !== 401) console.warn('Could not load session', e.status);
      this.user.set(null);
    }
  }

  async login(email: string, password: string, rememberMe: boolean): Promise<Me> {
    const me = await firstValueFrom(this.api.login({ email, password, rememberMe }));
    await firstValueFrom(this.api.csrf()); // tokens are bound to the identity → refresh after sign-in
    this.user.set(me);
    return me;
  }

  async register(body: { fullName: string; email: string; phone: string; password: string }): Promise<Me> {
    const me = await firstValueFrom(this.api.register(body));
    await firstValueFrom(this.api.csrf());
    this.user.set(me);
    return me;
  }

  async logout(): Promise<void> {
    try { await firstValueFrom(this.api.logout()); } finally {
      await firstValueFrom(this.api.csrf()).catch(() => undefined);
      this.user.set(null);
      await this.router.navigateByUrl('/');
    }
  }

  /** Called by the HTTP interceptor when the API says the session is gone. */
  sessionExpired(): void {
    if (!this.user()) return;
    this.user.set(null);
    void this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url, expired: 1 } });
  }

  /** Where to send a user after sign-in. */
  homeFor(me: Me): string {
    if (me.roles.includes('Admin')) return '/admin';
    if (me.roles.includes('Kitchen') || me.roles.includes('Delivery')) return '/admin/orders';
    return '/';
  }
}
