import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthApi } from '../api/api';
import { AuthStore } from '../state/auth.store';

const isAuthCall = (req: HttpRequest<unknown>) => req.url.includes('/api/v1/auth/');
const isUnsafe = (req: HttpRequest<unknown>) => !['GET', 'HEAD', 'OPTIONS'].includes(req.method);

/**
 * 1. If the anti-forgery token went stale (e.g. cookie expired), fetch a fresh one and retry the request once.
 * 2. A 401 on a protected call means the session ended → reset auth state and go to sign-in.
 * Angular's built-in XSRF support (withXsrfConfiguration) adds the X-XSRF-TOKEN header itself.
 */
export const apiErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthStore);
  const authApi = inject(AuthApi);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) return throwError(() => error);

      const antiforgeryFailure = error.status === 400 && isUnsafe(req) && !req.headers.has('x-bb-retried') && (error.error as { code?: string } | null)?.code === 'csrf_invalid';
      if (antiforgeryFailure) {
        return authApi.csrf().pipe(switchMap(() => next(req.clone({ setHeaders: { 'x-bb-retried': '1' } }))));
      }

      if (error.status === 401 && !isAuthCall(req)) auth.sessionExpired();
      return throwError(() => error);
    }),
  );
};
