import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal, OnInit } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { interval, startWith, switchMap, takeWhile } from 'rxjs';
import { AccountApi, AuthApi, OrdersApi } from '../../core/api/api';
import { Address, OrderDetail, OrderStatus, OrderSummary, PagedResult, ProductCard } from '../../core/api/models';
import { AuthStore } from '../../core/state/auth.store';
import { CartStore } from '../../core/state/cart.store';
import { ToastService } from '../../core/state/toast';
import { AddressForm } from '../../shared/address-form';
import { Icon } from '../../shared/icon';
import { ProductCardComponent } from '../../shared/product-card';
import { EmptyState, InrPipe, SmartImage, StatusBadge, utc } from '../../shared/ui';
import { INDIAN_MOBILE, strongPassword } from '../auth/auth-pages';

@Component({
  selector: 'bb-account-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container page">
      <div class="head"><span class="eyebrow">My account</span><h1>Hi, {{ auth.firstName() }}</h1></div>
      <div class="layout">
        <nav class="tabs" aria-label="Account">
          <a routerLink="orders" routerLinkActive="active"><bb-icon name="receipt" [size]="18" /> Orders</a>
          <a routerLink="favorites" routerLinkActive="active"><bb-icon name="heart" [size]="18" /> Favourites</a>
          <a routerLink="addresses" routerLinkActive="active"><bb-icon name="pin" [size]="18" /> Addresses</a>
          <a routerLink="profile" routerLinkActive="active"><bb-icon name="user" [size]="18" /> Profile</a>
        </nav>
        <section><router-outlet /></section>
      </div>
    </div>`,
  styles: `
    .head h1 { margin: 4px 0 var(--s5); }
    .layout { display: grid; grid-template-columns: 220px 1fr; gap: var(--s6); align-items: start; }
    .tabs { display: flex; flex-direction: column; gap: 4px; position: sticky; top: calc(var(--bb-header-h) + 16px); }
    .tabs a { display: flex; align-items: center; gap: 10px; padding: 11px 14px; border-radius: 12px; font-weight: 600; color: var(--bb-ink-2); }
    .tabs a:hover { background: var(--bb-surface-2); }
    .tabs a.active { background: var(--bb-ink); color: #fff; }
    @media (max-width: 760px) { .layout { grid-template-columns: 1fr; } .tabs { flex-direction: row; overflow-x: auto; position: static; } .tabs a { white-space: nowrap; } }`,
})
export class AccountLayout {
  protected readonly auth = inject(AuthStore);
}

@Component({
  selector: 'bb-my-orders',
  imports: [RouterLink, DatePipe, EmptyState, InrPipe, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Your orders</h2>
    @if (page(); as p) {
      @for (o of p.items; track o.id) {
        <a class="order card" [routerLink]="[o.orderNumber]">
          <div class="top"><strong>{{ o.orderNumber }}</strong><bb-status [status]="o.status" /></div>
          <p class="items">{{ o.itemsPreview }}</p>
          <div class="bottom muted small">
            <span>{{ utc(o.placedAt) | date: 'd MMM y, h:mm a' }}</span>·<span>{{ o.itemCount }} item(s)</span>·<span>{{ o.fulfillmentType === 'Pickup' ? 'Pickup' : 'Delivery' }}</span>
            <span class="spacer"></span><strong class="total">{{ o.total | inr: true }}</strong>
          </div>
        </a>
      } @empty {
        <bb-empty icon="receipt" title="No orders yet" text="Your order history will appear here once you place your first order." actionLabel="Start ordering" actionLink="/menu" />
      }
      @if (p.totalPages > 1) {
        <div class="pager">
          <button class="btn btn-sm" [disabled]="p.page <= 1" (click)="load(p.page - 1)">Previous</button>
          <span class="muted small">Page {{ p.page }} of {{ p.totalPages }}</span>
          <button class="btn btn-sm" [disabled]="p.page >= p.totalPages" (click)="load(p.page + 1)">Next</button>
        </div>
      }
    } @else { @for (i of [1,2,3]; track i) { <div class="skeleton" style="height:110px;margin-bottom:12px"></div> } }`,
  styles: `
    .order { display: block; padding: var(--s4) var(--s5); margin-bottom: var(--s3); transition: box-shadow .15s; }
    .order:hover { box-shadow: var(--bb-shadow); }
    .top { display: flex; justify-content: space-between; align-items: center; gap: 8px; }
    .items { margin: 6px 0; color: var(--bb-ink-2); }
    .bottom { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .total { color: var(--bb-ink); font-size: 1rem; }
    .pager { display: flex; align-items: center; justify-content: center; gap: var(--s4); margin-top: var(--s4); }`,
})
export class MyOrdersPage {
  private readonly api = inject(OrdersApi);
  private readonly toast = inject(ToastService);
  protected readonly page = signal<PagedResult<OrderSummary> | null>(null);
  protected readonly utc = utc;
  constructor() { this.load(1); }
  protected load(n: number): void {
    this.api.mine(n, 10).subscribe({ next: p => this.page.set(p), error: e => this.toast.error(e) });
  }
}

const DELIVERY_STEPS: OrderStatus[] = ['Placed', 'Confirmed', 'Preparing', 'Ready', 'OutForDelivery', 'Delivered'];
const PICKUP_STEPS: OrderStatus[] = ['Placed', 'Confirmed', 'Preparing', 'Ready', 'PickedUp'];
const STEP_LABEL: Partial<Record<OrderStatus, string>> = {
  Placed: 'Order placed', Confirmed: 'Confirmed', Preparing: 'Cooking', Ready: 'Packed', OutForDelivery: 'On the way', Delivered: 'Delivered', PickedUp: 'Picked up',
};

@Component({
  selector: 'bb-order-detail',
  imports: [RouterLink, DatePipe, FormsModule, Icon, InrPipe, SmartImage, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (order(); as o) {
      @if (justPlaced && o.status !== 'PaymentFailed') {
        <div class="success fade-up"><bb-icon name="check" [size]="28" /><div><h3>Order placed!</h3><p>Thanks {{ auth.firstName() }}! We've sent order {{ o.orderNumber }} to the kitchen.</p></div></div>
      }
      <div class="head">
        <div><a routerLink="/account/orders" class="muted small">← All orders</a><h2>Order {{ o.orderNumber }}</h2>
          <span class="muted small">Placed {{ utc(o.placedAt) | date: 'd MMM y, h:mm a' }}</span></div>
        <bb-status [status]="o.status" />
      </div>

      @if (!terminal()) {
        <div class="card card-pad tracker">
          <ol>
            @for (s of steps(); track s; let i = $index) {
              <li [class.done]="i <= currentStep()" [class.current]="i === currentStep()">
                <span class="dot">@if (i < currentStep()) {<bb-icon name="check" [size]="14" [stroke]="3" />}</span>
                <span class="label">{{ stepLabel(s) }}</span>
              </li>
            }
          </ol>
          @if (o.status === 'AwaitingPayment') { <p class="small">Waiting for payment. <a [routerLink]="['/pay', o.orderNumber]">Complete payment</a></p> }
          <p class="muted small">This page refreshes automatically.</p>
        </div>
      } @else if (o.status === 'Cancelled' || o.status === 'PaymentFailed') {
        <div class="card card-pad cancelled"><bb-icon name="alert" /> <span>{{ o.status === 'Cancelled' ? 'This order was cancelled: ' + (o.cancellationReason ?? '') : 'The payment for this order failed, so it was not placed.' }}</span></div>
      }

      <div class="grid2">
        <div class="card">
          @for (i of o.items; track i.productId) {
            <div class="line">
              <bb-img class="img" [src]="i.imageUrl" [alt]="i.productName" />
              <div class="info"><strong>{{ i.productName }}</strong><span class="muted small">{{ i.quantity }} × {{ i.unitPrice | inr }}</span></div>
              <strong>{{ i.lineTotal | inr }}</strong>
              @if (o.canReview && i.slug) { <a class="btn btn-sm" [routerLink]="['/menu', i.slug]" fragment="reviews">Review</a> }
            </div>
          }
          <div class="totals">
            <div class="row"><span>Item total</span><span class="spacer"></span><span>{{ o.subtotal | inr: true }}</span></div>
            @if (o.discount) { <div class="row" style="color:var(--bb-success)"><span>Discount ({{ o.couponCode }})</span><span class="spacer"></span><span>− {{ o.discount | inr: true }}</span></div> }
            <div class="row"><span>Delivery</span><span class="spacer"></span><span>{{ o.deliveryFee ? (o.deliveryFee | inr: true) : 'Free' }}</span></div>
            <div class="row"><span>GST</span><span class="spacer"></span><span>{{ o.tax | inr: true }}</span></div>
            <div class="row grand"><span>Total</span><span class="spacer"></span><span>{{ o.total | inr: true }}</span></div>
          </div>
        </div>
        <div class="stack">
          <div class="card card-pad">
            <h4>{{ o.fulfillmentType === 'Delivery' ? 'Delivering to' : 'Pickup from' }}</h4>
            @if (o.deliveryAddress; as a) {
              <p class="small">{{ a.recipientName }}<br />{{ a.line1 }}@if (a.line2) {, {{ a.line2 }}}<br />{{ a.city }} {{ a.pincode }}<br />{{ a.phone }}</p>
            } @else { <p class="small">BiteBox Kitchen, 100 Feet Road, Indiranagar, Bengaluru</p> }
            <h4>Payment</h4>
            <p class="small">{{ o.payment.method === 'CashOnDelivery' ? 'Cash on delivery' : 'Online (simulated)' }} · <bb-status [payment]="o.payment.status" /></p>
            @if (o.notes) { <h4>Your note</h4><p class="small">{{ o.notes }}</p> }
          </div>
          <div class="actions">
            <button class="btn btn-block" (click)="reorder(o.orderNumber)" [disabled]="busy()"><bb-icon name="refresh" [size]="16" /> Order again</button>
            @if (o.canCancel) {
              @if (confirmCancel()) {
                <div class="card card-pad stack">
                  <label class="field"><span>Reason (optional)</span><input class="input" [(ngModel)]="cancelReason" maxlength="200" /></label>
                  <div class="row"><button class="btn btn-danger btn-sm" (click)="cancel(o.orderNumber)" [disabled]="busy()">Yes, cancel order</button>
                    <button class="btn btn-ghost btn-sm" (click)="confirmCancel.set(false)">Keep order</button></div>
                </div>
              } @else { <button class="btn btn-danger btn-block" (click)="confirmCancel.set(true)">Cancel order</button> }
            }
          </div>
        </div>
      </div>

      <details class="history"><summary>Status history</summary>
        <ul>@for (t of o.timeline; track $index) {<li><strong>{{ stepLabel(t.to) }}</strong> <span class="muted small">{{ utc(t.at) | date: 'd MMM, h:mm a' }}</span>@if (t.note) { · <span class="small">{{ t.note }}</span>}</li>}</ul>
      </details>
    } @else { <div class="skeleton" style="height:420px"></div> }`,
  styles: `
    .success { display: flex; gap: var(--s4); align-items: center; padding: var(--s5); border-radius: var(--bb-radius-lg); background: var(--bb-success-soft); color: var(--bb-success); margin-bottom: var(--s5); }
    .success h3 { margin: 0; } .success p { margin: 0; color: var(--bb-ink-2); }
    .head { display: flex; justify-content: space-between; align-items: flex-start; gap: var(--s4); margin-bottom: var(--s4); }
    .head h2 { margin: 6px 0 2px; }
    .tracker { margin-bottom: var(--s5); }
    ol { list-style: none; padding: 0; margin: 0 0 var(--s3); display: flex; }
    li { flex: 1; display: flex; flex-direction: column; align-items: center; gap: 6px; position: relative; text-align: center; font-size: .8rem; color: var(--bb-muted); }
    li::before { content: ''; position: absolute; top: 13px; left: -50%; right: 50%; height: 3px; background: var(--bb-border); }
    li:first-child::before { display: none; }
    li.done::before { background: var(--bb-primary); }
    .dot { width: 28px; height: 28px; border-radius: 50%; border: 3px solid var(--bb-border); background: var(--bb-surface); display: grid; place-items: center; z-index: 1; color: #fff; }
    li.done .dot { background: var(--bb-primary); border-color: var(--bb-primary); }
    li.current .dot { box-shadow: 0 0 0 6px var(--bb-primary-soft); animation: pulse 1.6s infinite; }
    li.done .label { color: var(--bb-ink); font-weight: 650; }
    @keyframes pulse { 50% { box-shadow: 0 0 0 10px transparent; } }
    .cancelled { display: flex; gap: 10px; color: var(--bb-danger); background: var(--bb-danger-soft); border: 0; margin-bottom: var(--s5); }
    .grid2 { display: grid; grid-template-columns: 1fr 300px; gap: var(--s5); align-items: start; }
    .line { display: grid; grid-template-columns: 52px 1fr auto auto; gap: var(--s3); align-items: center; padding: var(--s3) var(--s5); border-bottom: 1px solid var(--bb-border); }
    .img { width: 52px; height: 52px; border-radius: 12px; }
    .info { display: flex; flex-direction: column; }
    .totals { padding: var(--s4) var(--s5); display: flex; flex-direction: column; gap: 6px; font-size: .92rem; }
    .grand { font-weight: 800; font-size: 1.1rem; padding-top: 6px; border-top: 1px dashed var(--bb-border); }
    .actions { display: flex; flex-direction: column; gap: var(--s3); }
    .history { margin-top: var(--s5); } .history summary { cursor: pointer; font-weight: 650; }
    a { color: var(--bb-primary-ink); }
    @media (max-width: 860px) { .grid2 { grid-template-columns: 1fr; } ol { overflow-x: auto; } li { min-width: 72px; } }`,
})
export class OrderDetailPage implements OnInit {
  readonly orderNumber = input.required<string>();
  private readonly api = inject(OrdersApi);
  private readonly cart = inject(CartStore);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthStore);
  protected readonly utc = utc;
  protected readonly justPlaced = inject(ActivatedRoute).snapshot.queryParamMap.has('placed');

  protected readonly order = signal<OrderDetail | null>(null);
  protected readonly busy = signal(false);
  protected readonly confirmCancel = signal(false);
  protected cancelReason = '';

  protected readonly terminal = computed(() => ['Delivered', 'PickedUp', 'Cancelled', 'PaymentFailed'].includes(this.order()?.status ?? ''));
  protected readonly steps = computed(() => (this.order()?.fulfillmentType === 'Pickup' ? PICKUP_STEPS : DELIVERY_STEPS));
  protected readonly currentStep = computed(() => {
    const s = this.order()?.status;
    return s === 'AwaitingPayment' ? -1 : this.steps().indexOf(s as OrderStatus);
  });

  ngOnInit(): void {
    // Poll while the order is in progress so the tracker updates as the kitchen moves it along.
    interval(20_000).pipe(
      startWith(0),
      switchMap(() => this.api.get(this.orderNumber())),
      takeWhile(o => !['Delivered', 'PickedUp', 'Cancelled', 'PaymentFailed'].includes(o.status), true),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({ next: o => this.order.set(o), error: e => this.toast.error(e) });
  }

  protected stepLabel(s: OrderStatus): string { return STEP_LABEL[s] ?? s; }

  protected cancel(orderNumber: string): void {
    this.busy.set(true);
    this.api.cancel(orderNumber, this.cancelReason.trim() || null).subscribe({
      next: o => { this.order.set(o); this.busy.set(false); this.confirmCancel.set(false); this.toast.success('Your order was cancelled.'); },
      error: e => { this.busy.set(false); this.toast.error(e); },
    });
  }

  protected reorder(orderNumber: string): void {
    this.busy.set(true);
    this.api.reorder(orderNumber).subscribe({
      next: async r => {
        await this.cart.reload();
        this.busy.set(false);
        if (r.added) { this.toast.success(`${r.added} item(s) added to your cart`); void this.router.navigateByUrl('/cart'); }
        else this.toast.info('None of those dishes are available right now.');
      },
      error: e => { this.busy.set(false); this.toast.error(e); },
    });
  }
}

@Component({
  selector: 'bb-addresses',
  imports: [AddressForm, Icon, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="row"><h2>Saved addresses</h2><span class="spacer"></span>
      @if (!editing()) { <button class="btn btn-sm" (click)="editing.set('new')"><bb-icon name="plus" [size]="16" /> Add address</button> }</div>
    @if (editing()) {
      <div class="card card-pad" style="margin-bottom:var(--s4)">
        <bb-address-form [address]="editing() === 'new' ? null : $any(editing())" (saved)="saved()" (cancelled)="editing.set(null)" />
      </div>
    }
    <div class="list">
      @for (a of addresses(); track a.id) {
        <div class="card card-pad addr">
          <div><strong>{{ a.label }}</strong>@if (a.isDefault) {<span class="badge badge-brand" style="margin-left:8px">Default</span>}
            <p class="small">{{ a.recipientName }} · {{ a.phone }}<br />{{ a.line1 }}@if (a.line2) {, {{ a.line2 }}}, {{ a.city }} {{ a.pincode }}</p></div>
          <div class="row"><button class="btn btn-sm" (click)="editing.set(a)">Edit</button><button class="btn btn-sm btn-danger" (click)="remove(a)">Delete</button></div>
        </div>
      } @empty { @if (!editing()) { <bb-empty icon="pin" title="No saved addresses" text="Add one now to check out faster." /> } }
    </div>`,
  styles: `.list { display: grid; gap: var(--s3); } .addr { display: flex; justify-content: space-between; gap: var(--s4); align-items: flex-start; } .addr p { margin: 6px 0 0; }`,
})
export class AddressesPage {
  private readonly api = inject(AccountApi);
  private readonly toast = inject(ToastService);
  protected readonly addresses = signal<Address[]>([]);
  protected readonly editing = signal<Address | 'new' | null>(null);
  constructor() { this.load(); }
  protected load(): void { this.api.addresses().subscribe({ next: a => this.addresses.set(a), error: e => this.toast.error(e) }); }
  protected saved(): void { this.editing.set(null); this.load(); }
  protected remove(a: Address): void {
    this.api.deleteAddress(a.id).subscribe({ next: () => { this.toast.success('Address removed'); this.load(); }, error: e => this.toast.error(e) });
  }
}

@Component({
  selector: 'bb-favorites',
  imports: [ProductCardComponent, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Favourites</h2>
    @if (items(); as list) {
      <div class="grid">@for (p of list; track p.id) { <bb-product-card [product]="p" /> }</div>
      @if (!list.length) { <bb-empty icon="heart" title="No favourites yet" text="Tap the heart on any dish to save it here." actionLabel="Explore the menu" actionLink="/menu" /> }
    } @else { <div class="skeleton" style="height:320px"></div> }`,
  styles: `.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: var(--s5); }`,
})
export class FavoritesPage {
  protected readonly items = signal<ProductCard[] | null>(null);
  constructor() { inject(AccountApi).favorites().subscribe({ next: f => this.items.set(f), error: () => this.items.set([]) }); }
}

@Component({
  selector: 'bb-profile',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Profile</h2>
    <form class="card card-pad form-grid" [formGroup]="profile" (ngSubmit)="saveProfile()">
      <div class="field"><label for="p-name">Full name</label><input id="p-name" class="input" formControlName="fullName" /></div>
      <div class="field"><label for="p-phone">Mobile</label><input id="p-phone" class="input" formControlName="phone" maxlength="10" />
        @if (profile.controls.phone.touched && profile.controls.phone.invalid) { <span class="field-error">Enter a valid 10-digit mobile number.</span> }</div>
      <div class="field full"><label>Email</label><input class="input" [value]="auth.user()?.email" disabled /></div>
      <div class="full"><button class="btn btn-primary" [disabled]="profile.invalid || busy()">Save profile</button></div>
    </form>

    <h3 style="margin-top:var(--s6)">Change password</h3>
    <form class="card card-pad form-grid" [formGroup]="password" (ngSubmit)="changePassword()">
      <div class="field"><label for="p-cur">Current password</label><input id="p-cur" class="input" type="password" formControlName="currentPassword" autocomplete="current-password" /></div>
      <div class="field"><label for="p-new">New password</label><input id="p-new" class="input" type="password" formControlName="newPassword" autocomplete="new-password" />
        <span class="small muted">8+ characters with upper, lower case and a digit.</span></div>
      <div class="full"><button class="btn btn-dark" [disabled]="password.invalid || busy()">Update password</button></div>
    </form>`,
})
export class ProfilePage {
  protected readonly auth = inject(AuthStore);
  private readonly api = inject(AuthApi);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly busy = signal(false);
  protected readonly profile = this.fb.group({
    fullName: [this.auth.user()?.fullName ?? '', [Validators.required, Validators.minLength(2)]],
    phone: [this.auth.user()?.phone ?? '', [Validators.required, Validators.pattern(INDIAN_MOBILE)]],
  });
  protected readonly password = this.fb.group({
    currentPassword: ['', Validators.required],
    newPassword: ['', [Validators.required, strongPassword]],
  });

  protected saveProfile(): void {
    this.busy.set(true);
    this.api.updateProfile(this.profile.getRawValue()).subscribe({
      next: me => { this.auth.user.set(me); this.busy.set(false); this.toast.success('Profile updated'); },
      error: e => { this.busy.set(false); this.toast.error(e); },
    });
  }

  protected changePassword(): void {
    this.busy.set(true);
    this.api.changePassword(this.password.getRawValue()).subscribe({
      next: () => { this.busy.set(false); this.password.reset(); this.toast.success('Password changed'); },
      error: e => { this.busy.set(false); this.toast.error(e); },
    });
  }
}
