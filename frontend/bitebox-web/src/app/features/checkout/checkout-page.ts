import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AccountApi, OrdersApi } from '../../core/api/api';
import { Address, FulfillmentType, PaymentMethod, Quote } from '../../core/api/models';
import { CartStore } from '../../core/state/cart.store';
import { ToastService, problemMessage } from '../../core/state/toast';
import { AddressForm } from '../../shared/address-form';
import { Icon } from '../../shared/icon';
import { EmptyState, InrPipe } from '../../shared/ui';

/**
 * Checkout. The page never computes the payable amount itself: every change (fulfilment type, coupon)
 * asks the API for a fresh quote, and the API recalculates again when the order is placed.
 */
@Component({
  selector: 'bb-checkout',
  imports: [FormsModule, RouterLink, Icon, AddressForm, EmptyState, InrPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container page">
      <div class="page-title"><h1>Checkout</h1><a routerLink="/cart" class="btn btn-ghost btn-sm"><bb-icon name="chevronLeft" [size]="16" /> Back to cart</a></div>

      @if (quote() && !quote()!.lines.length) {
        <bb-empty icon="bag" title="Your cart is empty" actionLabel="Browse the menu" actionLink="/menu" />
      } @else {
      <div class="layout">
        <div class="stack-lg">
          <section class="card card-pad">
            <h3><span class="step">1</span> How do you want it?</h3>
            <div class="options">
              <button type="button" class="option" [class.active]="fulfillment() === 'Delivery'" (click)="setFulfillment('Delivery')">
                <bb-icon name="bike" /><span><strong>Home delivery</strong><small class="muted">~30–40 min</small></span>
              </button>
              <button type="button" class="option" [class.active]="fulfillment() === 'Pickup'" (click)="setFulfillment('Pickup')">
                <bb-icon name="store" /><span><strong>Pickup</strong><small class="muted">Ready in ~20 min · Indiranagar</small></span>
              </button>
            </div>
          </section>

          @if (fulfillment() === 'Delivery') {
            <section class="card card-pad">
              <h3><span class="step">2</span> Delivery address</h3>
              @if (addingAddress()) {
                <bb-address-form (saved)="onAddressSaved($event)" (cancelled)="addingAddress.set(false)" />
              } @else {
                <div class="addresses">
                  @for (a of addresses(); track a.id) {
                    <label class="address" [class.active]="addressId() === a.id">
                      <input type="radio" name="address" [checked]="addressId() === a.id" (change)="addressId.set(a.id)" />
                      <span><strong>{{ a.label }}</strong>@if (a.isDefault) {<span class="badge" style="margin-left:6px">Default</span>}<br />
                        <span class="small">{{ a.recipientName }}, {{ a.line1 }}@if (a.line2) {, {{ a.line2 }}}, {{ a.city }} {{ a.pincode }}</span><br />
                        <span class="small muted">{{ a.phone }}</span></span>
                    </label>
                  } @empty { <p class="muted">You haven't saved an address yet.</p> }
                </div>
                <button class="btn btn-sm" type="button" (click)="addingAddress.set(true)"><bb-icon name="plus" [size]="16" /> Add new address</button>
              }
            </section>
          }

          <section class="card card-pad">
            <h3><span class="step">{{ fulfillment() === 'Delivery' ? 3 : 2 }}</span> Payment</h3>
            <div class="options">
              <button type="button" class="option" [class.active]="payment() === 'CashOnDelivery'" (click)="payment.set('CashOnDelivery')">
                <bb-icon name="cash" /><span><strong>{{ fulfillment() === 'Delivery' ? 'Cash on delivery' : 'Pay at counter' }}</strong><small class="muted">Cash or UPI at handover</small></span>
              </button>
              <button type="button" class="option" [class.active]="payment() === 'OnlineSimulated'" (click)="payment.set('OnlineSimulated')">
                <bb-icon name="card" /><span><strong>Pay online</strong><small class="muted">Simulated gateway · no real money</small></span>
              </button>
            </div>
            <div class="field" style="margin-top:var(--s4)">
              <label for="notes">Instructions for the kitchen (optional)</label>
              <textarea id="notes" class="input" [(ngModel)]="notes" maxlength="300" placeholder="e.g. less oil, no onions, ring the bell twice"></textarea>
            </div>
          </section>
        </div>

        <aside class="card card-pad summary">
          <h3>Order summary</h3>
          @if (quote(); as q) {
            <div class="lines">
              @for (l of q.lines; track l.productId) {
                <div class="row small"><span>{{ l.quantity }} × {{ l.name }}</span><span class="spacer"></span><span>{{ l.lineTotal | inr }}</span></div>
              }
            </div>
            <form class="coupon" (submit)="$event.preventDefault(); applyCoupon()">
              <input class="input" name="coupon" [(ngModel)]="couponInput" placeholder="Coupon code" aria-label="Coupon code" [disabled]="!!q.appliedCouponCode" />
              @if (q.appliedCouponCode) { <button class="btn btn-sm" type="button" (click)="removeCoupon()">Remove</button> }
              @else { <button class="btn btn-sm" type="submit" [disabled]="!couponInput.trim()">Apply</button> }
            </form>
            @if (q.couponMessage) { <p class="small" [style.color]="q.couponValid ? 'var(--bb-success)' : 'var(--bb-danger)'">{{ q.couponMessage }}</p> }
            <div class="totals">
              <div class="row"><span>Item total</span><span class="spacer"></span><span>{{ q.subtotal | inr: true }}</span></div>
              @if (q.discount) { <div class="row discount"><span>Discount</span><span class="spacer"></span><span>− {{ q.discount | inr: true }}</span></div> }
              <div class="row"><span>Delivery fee</span><span class="spacer"></span><span>{{ q.deliveryFee ? (q.deliveryFee | inr: true) : 'Free' }}</span></div>
              <div class="row"><span>GST ({{ q.taxRate * 100 }}%)</span><span class="spacer"></span><span>{{ q.tax | inr: true }}</span></div>
              <hr class="divider" />
              <div class="row grand"><span>To pay</span><span class="spacer"></span><span>{{ q.total | inr: true }}</span></div>
            </div>
            @for (p of q.problems; track p) { <p class="field-error">{{ p }}</p> }
            <button class="btn btn-primary btn-block" type="button" (click)="place()" [disabled]="!canPlace() || placing()">
              {{ placing() ? 'Placing order…' : (payment() === 'OnlineSimulated' ? 'Continue to payment' : 'Place order') }}
            </button>
            @if (fulfillment() === 'Delivery' && !addressId()) { <p class="small muted">Choose a delivery address to continue.</p> }
          } @else {
            <div class="skeleton" style="height:220px"></div>
          }
        </aside>
      </div>
      }
    </div>`,
  styles: `
    .layout { display: grid; grid-template-columns: 1fr 380px; gap: var(--s5); align-items: start; }
    .stack-lg { display: flex; flex-direction: column; gap: var(--s5); }
    h3 { display: flex; align-items: center; gap: 10px; margin-bottom: var(--s4); }
    .step { width: 28px; height: 28px; border-radius: 50%; background: var(--bb-ink); color: #fff; display: grid; place-items: center; font-family: var(--bb-font); font-size: .85rem; }
    .options { display: grid; grid-template-columns: 1fr 1fr; gap: var(--s3); }
    .option { display: flex; align-items: center; gap: 12px; text-align: left; padding: 14px 16px; border-radius: var(--bb-radius); border: 1.5px solid var(--bb-border-strong); background: var(--bb-surface); cursor: pointer; }
    .option span { display: flex; flex-direction: column; }
    .option.active { border-color: var(--bb-primary); background: var(--bb-primary-soft); box-shadow: 0 0 0 3px var(--bb-primary-soft); }
    .option bb-icon { color: var(--bb-primary); }
    .addresses { display: grid; gap: var(--s3); margin-bottom: var(--s4); }
    .address { display: flex; gap: 12px; align-items: flex-start; padding: 14px; border: 1.5px solid var(--bb-border); border-radius: var(--bb-radius); cursor: pointer; }
    .address.active { border-color: var(--bb-primary); background: #fffaf6; }
    .address input { margin-top: 4px; accent-color: var(--bb-primary); }
    .summary { position: sticky; top: calc(var(--bb-header-h) + 16px); display: flex; flex-direction: column; gap: var(--s3); }
    .lines { display: flex; flex-direction: column; gap: 6px; max-height: 200px; overflow: auto; }
    .coupon { display: flex; gap: 8px; }
    .coupon .input { text-transform: uppercase; min-height: 38px; }
    .totals { display: flex; flex-direction: column; gap: 6px; font-size: .92rem; }
    .discount { color: var(--bb-success); font-weight: 600; }
    .grand { font-size: 1.15rem; font-weight: 800; }
    @media (max-width: 900px) { .layout { grid-template-columns: 1fr; } .options { grid-template-columns: 1fr; } .summary { position: static; } }`,
})
export class CheckoutPage {
  private readonly ordersApi = inject(OrdersApi);
  private readonly accountApi = inject(AccountApi);
  private readonly cart = inject(CartStore);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  protected readonly fulfillment = signal<FulfillmentType>('Delivery');
  protected readonly payment = signal<PaymentMethod>('CashOnDelivery');
  protected readonly addresses = signal<Address[]>([]);
  protected readonly addressId = signal<number | null>(null);
  protected readonly addingAddress = signal(false);
  protected readonly quote = signal<Quote | null>(null);
  protected readonly placing = signal(false);
  private readonly couponCode = signal<string | null>(null);
  protected couponInput = '';
  protected notes = '';
  /** One key per checkout visit: retries/double-clicks can never create two orders. */
  private readonly idempotencyKey = crypto.randomUUID().replaceAll('-', '');

  protected readonly canPlace = computed(() => {
    const q = this.quote();
    return !!q && q.lines.length > 0 && q.problems.length === 0 && (this.fulfillment() === 'Pickup' || !!this.addressId());
  });

  constructor() {
    this.accountApi.addresses().subscribe({
      next: list => {
        this.addresses.set(list);
        this.addressId.set(list.find(a => a.isDefault)?.id ?? list[0]?.id ?? null);
        if (!list.length) this.addingAddress.set(true);
      },
      error: e => this.toast.error(e),
    });
    this.refreshQuote();
  }

  protected setFulfillment(type: FulfillmentType): void {
    this.fulfillment.set(type);
    this.refreshQuote();
  }

  protected onAddressSaved(a: Address): void {
    this.addresses.update(list => [...list.map(x => (a.isDefault ? { ...x, isDefault: false } : x)), a]);
    this.addressId.set(a.id);
    this.addingAddress.set(false);
  }

  protected applyCoupon(): void {
    this.couponCode.set(this.couponInput.trim().toUpperCase() || null);
    this.refreshQuote();
  }

  protected removeCoupon(): void {
    this.couponInput = '';
    this.couponCode.set(null);
    this.refreshQuote();
  }

  protected place(): void {
    if (!this.canPlace()) return;
    this.placing.set(true);
    const q = this.quote()!;
    this.ordersApi.place({
      fulfillmentType: this.fulfillment(),
      addressId: this.fulfillment() === 'Delivery' ? this.addressId() : null,
      paymentMethod: this.payment(),
      couponCode: q.couponValid ? q.appliedCouponCode : null,
      notes: this.notes.trim() || null,
      idempotencyKey: this.idempotencyKey,
    }).subscribe({
      next: order => {
        void this.cart.reload();
        if (order.status === 'AwaitingPayment') void this.router.navigate(['/pay', order.orderNumber]);
        else void this.router.navigate(['/account/orders', order.orderNumber], { queryParams: { placed: 1 } });
      },
      error: e => {
        this.placing.set(false);
        this.toast.error(problemMessage(e));
        this.refreshQuote(); // stock or prices may have changed
      },
    });
  }

  private refreshQuote(): void {
    this.ordersApi.quote(this.fulfillment(), this.couponCode()).subscribe({
      next: q => {
        this.quote.set(q);
        if (q.appliedCouponCode) this.couponInput = q.appliedCouponCode;
      },
      error: e => this.toast.error(e),
    });
  }
}
