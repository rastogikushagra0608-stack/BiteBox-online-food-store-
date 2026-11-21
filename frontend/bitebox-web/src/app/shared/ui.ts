import { ChangeDetectionStrategy, Component, Pipe, PipeTransform, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { OrderStatus, PaymentStatus, SpiceLevel } from '../core/api/models';
import { Icon } from './icon';

/** ₹1,234 / ₹1,234.50 in Indian digit grouping. */
@Pipe({ name: 'inr' })
export class InrPipe implements PipeTransform {
  private static readonly whole = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 });
  private static readonly exact = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', minimumFractionDigits: 2, maximumFractionDigits: 2 });
  transform(value: number | null | undefined, exact = false): string {
    if (value === null || value === undefined) return '';
    return (exact || !Number.isInteger(value) ? InrPipe.exact : InrPipe.whole).format(value);
  }
}

const STATUS: Record<OrderStatus, { label: string; tone: string }> = {
  AwaitingPayment: { label: 'Awaiting payment', tone: 'warning' },
  Placed: { label: 'Placed', tone: 'info' },
  Confirmed: { label: 'Confirmed', tone: 'info' },
  Preparing: { label: 'Preparing', tone: 'brand' },
  Ready: { label: 'Ready', tone: 'brand' },
  OutForDelivery: { label: 'Out for delivery', tone: 'brand' },
  Delivered: { label: 'Delivered', tone: 'success' },
  PickedUp: { label: 'Picked up', tone: 'success' },
  Cancelled: { label: 'Cancelled', tone: 'danger' },
  PaymentFailed: { label: 'Payment failed', tone: 'danger' },
};

export const statusLabel = (s: OrderStatus) => STATUS[s]?.label ?? s;

@Pipe({ name: 'statusLabel' })
export class StatusLabelPipe implements PipeTransform {
  transform(value: OrderStatus): string { return statusLabel(value); }
}

const PAYMENT: Record<PaymentStatus, { label: string; tone: string }> = {
  Pending: { label: 'Unpaid', tone: '' },
  Paid: { label: 'Paid', tone: 'success' },
  Failed: { label: 'Failed', tone: 'danger' },
  RefundPending: { label: 'Refund pending', tone: 'warning' },
  Refunded: { label: 'Refunded', tone: 'info' },
};

@Component({
  selector: 'bb-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [class]="'badge badge-' + tone()">{{ label() }}</span>`,
})
export class StatusBadge {
  readonly status = input<OrderStatus>();
  readonly payment = input<PaymentStatus>();
  protected readonly tone = computed(() => (this.status() ? STATUS[this.status()!].tone : PAYMENT[this.payment()!]?.tone) || 'neutral');
  protected readonly label = computed(() => (this.status() ? STATUS[this.status()!].label : PAYMENT[this.payment()!]?.label));
}

/** The Indian green-square / red-triangle food marker. */
@Component({
  selector: 'bb-veg',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="mark" [class.nonveg]="!veg()" [attr.title]="veg() ? 'Vegetarian' : 'Non-vegetarian'" role="img"
                  [attr.aria-label]="veg() ? 'Vegetarian' : 'Non-vegetarian'"><i></i></span>`,
  styles: `
    .mark { display: inline-grid; place-items: center; width: 16px; height: 16px; border: 1.6px solid var(--bb-veg); border-radius: 3px; background: #fff; }
    .mark i { width: 8px; height: 8px; border-radius: 50%; background: var(--bb-veg); }
    .mark.nonveg { border-color: var(--bb-nonveg); }
    .mark.nonveg i { border-radius: 0; width: 0; height: 0; background: none; border-left: 4.5px solid transparent; border-right: 4.5px solid transparent; border-bottom: 8px solid var(--bb-nonveg); }`,
})
export class VegMark {
  readonly veg = input.required<boolean>();
}

@Component({
  selector: 'bb-spice',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (level() !== 'None') {
    <span class="spice" [attr.title]="level() + ' spice'">@for (i of flames(); track $index) {<bb-icon name="flame" [size]="13" [filled]="true" />}</span>
  }`,
  styles: `.spice { display: inline-flex; color: var(--bb-primary); }`,
})
export class SpiceMeter {
  readonly level = input.required<SpiceLevel>();
  protected readonly flames = computed(() => Array.from({ length: { None: 0, Mild: 1, Medium: 2, Hot: 3 }[this.level()] }));
}

@Component({
  selector: 'bb-rating',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (count() > 0) {
      <span class="rating"><bb-icon name="star" [size]="14" [filled]="true" /> {{ value().toFixed(1) }}
        @if (showCount()) {<span class="muted">({{ count() }})</span>}</span>
    } @else { <span class="muted small">New</span> }`,
  styles: `.rating { display: inline-flex; align-items: center; gap: 3px; font-weight: 700; font-size: 0.85rem; } bb-icon { color: var(--bb-star); }`,
})
export class Rating {
  readonly value = input(0);
  readonly count = input(0);
  readonly showCount = input(true);
}

@Component({
  selector: 'bb-stepper',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stepper" [class.sm]="small()">
      <button type="button" (click)="changed.emit(value() - 1)" [disabled]="disabled() || value() <= min()" aria-label="Decrease quantity"><bb-icon name="minus" [size]="16" /></button>
      <span aria-live="polite">{{ value() }}</span>
      <button type="button" (click)="changed.emit(value() + 1)" [disabled]="disabled() || value() >= max()" aria-label="Increase quantity"><bb-icon name="plus" [size]="16" /></button>
    </div>`,
  styles: `
    .stepper { display: inline-flex; align-items: center; border: 1.5px solid var(--bb-primary); border-radius: 999px; background: var(--bb-primary-soft); color: var(--bb-primary-ink); height: 40px; }
    .stepper.sm { height: 34px; }
    button { border: 0; background: none; width: 38px; height: 100%; display: grid; place-items: center; cursor: pointer; color: inherit; border-radius: 999px; }
    button:disabled { opacity: 0.4; cursor: not-allowed; }
    span { min-width: 22px; text-align: center; font-weight: 700; font-variant-numeric: tabular-nums; }`,
})
export class Stepper {
  readonly value = input.required<number>();
  readonly min = input(0);
  readonly max = input(20);
  readonly disabled = input(false);
  readonly small = input(false);
  readonly changed = output<number>();
}

@Component({
  selector: 'bb-empty',
  imports: [Icon, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="empty">
      <div class="art"><bb-icon [name]="icon()" [size]="34" /></div>
      <h3>{{ title() }}</h3>
      @if (text()) {<p class="muted">{{ text() }}</p>}
      @if (actionLabel() && actionLink()) {<a class="btn btn-primary" [routerLink]="actionLink()">{{ actionLabel() }}</a>}
      <ng-content />
    </div>`,
  styles: `
    .empty { text-align: center; padding: var(--s7) var(--s5); display: flex; flex-direction: column; align-items: center; gap: var(--s2); }
    .art { width: 76px; height: 76px; border-radius: 50%; background: var(--bb-primary-soft); color: var(--bb-primary); display: grid; place-items: center; margin-bottom: var(--s2); }
    h3 { margin: 0; } p { max-width: 420px; }`,
})
export class EmptyState {
  readonly icon = input('box');
  readonly title = input.required<string>();
  readonly text = input<string>();
  readonly actionLabel = input<string>();
  readonly actionLink = input<string>();
}

@Component({
  selector: 'bb-error',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="err card card-pad"><bb-icon name="alert" /> <span>{{ message() }}</span>
    <button class="btn btn-sm" type="button" (click)="retry.emit()">Try again</button></div>`,
  styles: `.err { display: flex; align-items: center; gap: var(--s3); color: var(--bb-danger); background: var(--bb-danger-soft); border-color: transparent; } span { flex: 1; }`,
})
export class ErrorState {
  readonly message = input('Could not load this section.');
  readonly retry = output<void>();
}

/** Product image with a graceful branded fallback when a photo fails to load. */
@Component({
  selector: 'bb-img',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (src() && !failed) {
      <img [src]="src()" [alt]="alt()" [attr.loading]="eager() ? 'eager' : 'lazy'" (error)="failed = true" />
    } @else { <div class="fallback" role="img" [attr.aria-label]="alt()">🍽️</div> }`,
  styles: `
    :host { display: block; overflow: hidden; background: var(--bb-surface-2); }
    img { width: 100%; height: 100%; object-fit: cover; }
    .fallback { width: 100%; height: 100%; display: grid; place-items: center; font-size: 2rem; background: linear-gradient(135deg, var(--bb-primary-soft), var(--bb-surface-2)); }`,
})
export class SmartImage {
  readonly src = input<string | null>();
  readonly alt = input('');
  readonly eager = input(false);
  protected failed = false;
}

@Pipe({ name: 'timeAgo' })
export class TimeAgoPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) return '';
    const seconds = Math.round((Date.now() - new Date(value).getTime()) / 1000);
    if (seconds < 60) return 'just now';
    const minutes = Math.round(seconds / 60);
    if (minutes < 60) return `${minutes} min ago`;
    const hours = Math.round(minutes / 60);
    if (hours < 24) return `${hours} h ago`;
    const days = Math.round(hours / 24);
    return days < 30 ? `${days} d ago` : new Date(value).toLocaleDateString('en-IN', { day: 'numeric', month: 'short' });
  }
}

/** Backend timestamps are UTC; this normalises them for the date pipe. */
export const utc = (value: string) => (value.endsWith('Z') ? value : `${value}Z`);
