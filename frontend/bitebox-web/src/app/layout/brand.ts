import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { ToastService } from '../core/state/toast';
import { Icon } from '../shared/icon';

/** BiteBox logo: a takeaway box with a bite taken out of the corner. */
@Component({
  selector: 'bb-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="logo" [class.light]="light()">
      <svg viewBox="0 0 64 64" width="34" height="34" aria-hidden="true">
        <defs><mask id="bb-bite"><rect width="64" height="64" fill="#fff"/><circle cx="54" cy="10" r="13" fill="#000"/></mask></defs>
        <rect x="4" y="6" width="56" height="54" rx="14" fill="#E8572A" mask="url(#bb-bite)"/>
        <path d="M18 30h28l-3 20H21z" fill="#FFF8F1"/>
        <path d="M22 30c0-6 4-10 10-10s10 4 10 10" fill="none" stroke="#FFF8F1" stroke-width="4" stroke-linecap="round"/>
      </svg>
      <span class="word">Bite<b>Box</b>@if (suffix()) {<small>{{ suffix() }}</small>}</span>
    </span>`,
  styles: `
    .logo { display: inline-flex; align-items: center; gap: 10px; }
    .word { font-family: var(--bb-font-display); font-size: 1.45rem; font-weight: 700; letter-spacing: -0.02em; color: var(--bb-ink); }
    .word b { color: var(--bb-primary); font-weight: 700; }
    .word small { font-family: var(--bb-font); font-size: .68rem; font-weight: 700; letter-spacing: .12em; text-transform: uppercase; margin-left: 8px; padding: 3px 8px; border-radius: 999px; background: rgba(255,255,255,.12); vertical-align: 4px; }
    .light .word { color: #fff; }`,
})
export class Logo {
  readonly light = input(false);
  readonly suffix = input<string>();
}

@Component({
  selector: 'bb-toasts',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toasts" aria-live="polite">
      @for (t of toast.toasts(); track t.id) {
        <div class="toast" [class]="'toast ' + t.kind" role="status">
          <bb-icon [name]="t.kind === 'error' ? 'alert' : 'check'" [size]="18" />
          <span>{{ t.message }}</span>
          <button type="button" (click)="toast.dismiss(t.id)" aria-label="Dismiss"><bb-icon name="close" [size]="16" /></button>
        </div>
      }
    </div>`,
  styles: `
    .toasts { position: fixed; bottom: 20px; left: 50%; transform: translateX(-50%); z-index: 1000; display: flex; flex-direction: column; gap: 8px; width: min(440px, calc(100vw - 32px)); }
    .toast { display: flex; align-items: center; gap: 10px; padding: 12px 14px; border-radius: 12px; background: var(--bb-ink); color: #fff; box-shadow: var(--bb-shadow-lg); animation: bb-fade-up .25s ease both; font-weight: 500; font-size: .92rem; }
    .toast.success bb-icon:first-child { color: #7ee2a8; }
    .toast.error { background: #5c1612; }
    .toast span { flex: 1; }
    button { border: 0; background: none; color: inherit; opacity: .7; cursor: pointer; display: grid; }`,
})
export class Toasts {
  protected readonly toast = inject(ToastService);
}
