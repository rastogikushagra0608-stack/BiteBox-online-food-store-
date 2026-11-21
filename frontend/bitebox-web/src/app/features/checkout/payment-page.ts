import { ChangeDetectionStrategy, Component, inject, input, signal, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { OrdersApi } from '../../core/api/api';
import { OrderDetail } from '../../core/api/models';
import { ToastService } from '../../core/state/toast';
import { Icon } from '../../shared/icon';
import { InrPipe } from '../../shared/ui';

/**
 * Simulated payment step. BiteBox never collects card numbers: a real integration (Razorpay/Stripe)
 * would open the provider's hosted checkout here and confirm via the backend gateway adapter.
 */
@Component({
  selector: 'bb-payment',
  imports: [RouterLink, Icon, InrPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container page">
      <div class="pay card">
        @if (order(); as o) {
          <div class="head">
            <span class="eyebrow">BiteBox Pay · Test mode</span>
            <h1>{{ o.total | inr: true }}</h1>
            <p class="muted">Order {{ o.orderNumber }} · {{ o.items.length }} item(s)</p>
          </div>
          @if (o.status === 'AwaitingPayment') {
            <div class="notice"><bb-icon name="shield" /> <span>This is a <strong>simulated</strong> payment gateway for demonstration. No card details are requested and no money is charged.</span></div>
            <div class="actions">
              <button class="btn btn-primary btn-block" (click)="pay(true)" [disabled]="busy()">{{ busy() ? 'Processing…' : 'Simulate successful payment' }}</button>
              <button class="btn btn-danger btn-block" (click)="pay(false)" [disabled]="busy()">Simulate a failed payment</button>
            </div>
            <p class="small muted center">Changed your mind? <a [routerLink]="['/account/orders', o.orderNumber]">View order</a> to cancel it.</p>
          } @else {
            <p class="center">This order is <strong>{{ o.status }}</strong>. <a [routerLink]="['/account/orders', o.orderNumber]">View order</a></p>
          }
        } @else { <div class="skeleton" style="height:240px"></div> }
      </div>
    </div>`,
  styles: `
    .pay { max-width: 480px; margin: var(--s6) auto; padding: var(--s6); display: flex; flex-direction: column; gap: var(--s4); }
    .head { text-align: center; }
    .head h1 { font-size: 2.6rem; margin: 6px 0 0; }
    .notice { display: flex; gap: 10px; padding: 12px 14px; border-radius: 12px; background: var(--bb-info-soft); color: var(--bb-info); font-size: .88rem; }
    .actions { display: flex; flex-direction: column; gap: var(--s3); }
    .center { text-align: center; }
    a { color: var(--bb-primary-ink); font-weight: 700; }`,
})
export class PaymentPage implements OnInit {
  /** Bound from the route parameter (withComponentInputBinding). */
  readonly orderNumber = input.required<string>();
  private readonly api = inject(OrdersApi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  protected readonly order = signal<OrderDetail | null>(null);
  protected readonly busy = signal(false);

  ngOnInit(): void {
    this.api.get(this.orderNumber()).subscribe({ next: o => this.order.set(o), error: e => this.toast.error(e) });
  }

  protected pay(success: boolean): void {
    this.busy.set(true);
    this.api.pay(this.orderNumber(), success).subscribe({
      next: o => {
        if (o.status === 'PaymentFailed') this.toast.error('Payment failed. Your order was not placed and nothing was charged.');
        else this.toast.success('Payment successful! Your order is placed.');
        void this.router.navigate(['/account/orders', o.orderNumber], { queryParams: o.status === 'Placed' ? { placed: 1 } : {} });
      },
      error: e => { this.busy.set(false); this.toast.error(e); },
    });
  }
}
