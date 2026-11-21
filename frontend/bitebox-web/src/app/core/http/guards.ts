import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CanActivateFn, Router } from '@angular/router';
import { filter, map, take } from 'rxjs';
import { Role } from '../api/models';
import { AuthStore } from '../state/auth.store';

/** Waits until the session check has finished, then decides. */
function whenReady<T>(decide: (auth: AuthStore) => T) {
  const auth = inject(AuthStore);
  return toObservable(auth.user).pipe(filter(u => u !== undefined), take(1), map(() => decide(auth)));
}

/** Signed-in users only. Guests are sent to /login and returned afterwards. */
export const authGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  return whenReady(auth => auth.isLoggedIn() || router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } }));
};

/** Login/register pages: already signed-in users go home. */
export const guestGuard: CanActivateFn = () => {
  const router = inject(Router);
  return whenReady(auth => !auth.isLoggedIn() || router.createUrlTree([auth.homeFor(auth.user()!)]));
};

/**
 * Role-based guard for the admin area. This only improves UX — the API enforces the same rules,
 * so bypassing this guard in the browser gains nothing.
 */
export const roleGuard = (...roles: Role[]): CanActivateFn => (_route, state) => {
  const router = inject(Router);
  return whenReady(auth => {
    if (!auth.isLoggedIn()) return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    return roles.some(r => auth.hasRole(r)) || router.createUrlTree(['/']);
  });
};
