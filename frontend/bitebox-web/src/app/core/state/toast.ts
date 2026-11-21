import { Injectable, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Problem } from '../api/models';

export type ToastKind = 'success' | 'error' | 'info';
export interface Toast { id: number; kind: ToastKind; message: string; }

/** App-wide toast notifications held in a signal (no third-party UI library needed). */
@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private nextId = 1;

  success(message: string): void { this.push('success', message); }
  info(message: string): void { this.push('info', message); }
  error(messageOrError: unknown): void { this.push('error', typeof messageOrError === 'string' ? messageOrError : problemMessage(messageOrError)); }

  dismiss(id: number): void { this.toasts.update(list => list.filter(t => t.id !== id)); }

  private push(kind: ToastKind, message: string): void {
    const id = this.nextId++;
    this.toasts.update(list => [...list.slice(-3), { id, kind, message }]);
    setTimeout(() => this.dismiss(id), kind === 'error' ? 6000 : 3500);
  }
}

/** Turns any error (ProblemDetails, network failure, …) into a human sentence. */
export function problemMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'Cannot reach BiteBox right now. Please check your connection.';
    if (error.status === 429) return 'Too many requests. Please wait a moment and try again.';
    const body = error.error as Problem | null;
    if (body?.errors) {
      const first = Object.values(body.errors)[0]?.[0];
      if (first) return first;
    }
    if (body?.detail) return body.detail;
    if (error.status >= 500) return 'Something went wrong on our side. Please try again.';
  }
  return 'Something went wrong. Please try again.';
}

/** Field errors from a 400 ProblemDetails, keyed by camelCase property name. */
export function fieldErrors(error: unknown): Record<string, string> {
  if (error instanceof HttpErrorResponse && (error.error as Problem | null)?.errors) {
    return Object.fromEntries(Object.entries((error.error as Problem).errors!).map(([k, v]) => [k, v[0]]));
  }
  return {};
}
