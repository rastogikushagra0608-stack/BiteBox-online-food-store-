import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { interval } from 'rxjs';
import { AdminApi } from '../../core/api/api';
import { FulfillmentType, OrderDetail, OrderStatus, OrderSummary, PagedResult } from '../../core/api/models';
import { AuthStore } from '../../core/state/auth.store';
import { ToastService } from '../../core/state/toast';
import { Icon } from '../../shared/icon';
import { EmptyState, InrPipe, StatusBadge, statusLabel, utc } from '../../shared/ui';
import { ADMIN_PAGE } from './admin-layout';

const ACTION_LABEL: Partial<Record<OrderStatus, string>> = {
  Confirmed: 'Accept order', Preparing: 'Start cooking', Ready: 'Mark packed / ready', OutForDelivery: 'Out for delivery',
  Delivered: 'Mark delivered', PickedUp: 'Handed to customer',
};

/**
 * Order operations for Admin, Kitchen and Delivery staff. The action buttons are exactly the transitions
 * the API says this user may perform (allowedNextStatuses), so the UI can never offer an illegal move.
 */
@Component({
  selector: 'bb-admin-orders',
  imports: [FormsModule, DatePipe, Icon, EmptyState, InrPipe, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <div><span class="eyebrow">{{ auth.isAdmin() ? 'Operations' : auth.hasRole('Kitchen') ? 'Kitchen' : 'Deliveries' }}</span><h1>Orders</h1></div>
      <button class="btn btn-sm" (click)="load()"><bb-icon name="refresh" [size]="16" /> Refresh</button>
    </div>

    <div class="toolbar">
      <button class="chip" [class.active]="activeOnly" (click)="activeOnly = true; status = ''; page = 1; load()">Live queue</button>
      <button class="chip" [class.active]="!activeOnly && !status" (click)="activeOnly = false; status = ''; page = 1; load()">All</button>
      <select class="input" [(ngModel)]="status" (ngModelChange)="activeOnly = false; page = 1; load()" aria-label="Filter by status">
        <option value="">Any status</option>
        @for (s of statuses; track s) { <option [value]="s">{{ label(s) }}</option> }
      </select>
      <select class="input" [(ngModel)]="fulfillment" (ngModelChange)="page = 1; load()" aria-label="Filter by fulfilment">
        <option value="">Delivery & pickup</option><option value="Delivery">Delivery</option><option value="Pickup">Pickup</option>
      </select>
      <input class="input grow" [(ngModel)]="search" (keyup.enter)="page = 1; load()" placeholder="Search order no., customer or phone" aria-label="Search orders" />
    </div>

    <div class="layout" [class.with-detail]="selected()">
      <div class="card">
        @if (result(); as r) {
          @if (r.items.length) {
            <div class="table-wrap"><table class="table">
              <thead><tr><th>Order</th><th>Customer</th><th>Items</th><th>Type</th><th>Status</th><th>Placed</th><th class="num">Total</th></tr></thead>
              <tbody>
                @for (o of r.items; track o.id) {
                  <tr class="clickable" [class.sel]="selected()?.orderNumber === o.orderNumber" (click)="open(o.orderNumber)" tabindex="0" (keyup.enter)="open(o.orderNumber)">
                    <td><strong>{{ o.orderNumber }}</strong></td><td>{{ o.customerName }}</td>
                    <td class="muted ellipsis">{{ o.itemCount }} · {{ o.itemsPreview }}</td>
                    <td><bb-icon [name]="o.fulfillmentType === 'Pickup' ? 'store' : 'bike'" [size]="16" /></td>
                    <td><bb-status [status]="o.status" /></td>
                    <td class="muted">{{ utc(o.placedAt) | date: 'd MMM, h:mm a' }}</td>
                    <td class="num">{{ o.total | inr: true }}</td>
                  </tr>
                }
              </tbody></table></div>
            <div class="pager">
              <span class="muted small">{{ r.totalCount }} orders</span>
              <button class="btn btn-sm" [disabled]="r.page <= 1" (click)="page = r.page - 1; load()">Prev</button>
              <span class="small">{{ r.page }} / {{ r.totalPages || 1 }}</span>
              <button class="btn btn-sm" [disabled]="r.page >= r.totalPages" (click)="page = r.page + 1; load()">Next</button>
            </div>
          } @else { <bb-empty icon="receipt" title="No orders here" [text]="activeOnly ? 'The queue is clear. New orders will appear automatically.' : 'Try a different filter.'" /> }
        } @else { <div class="skeleton" style="height:300px"></div> }
      </div>

      @if (selected(); as o) {
        <aside class="card card-pad detail fade-up">
          <div class="row"><h3 style="margin:0">{{ o.orderNumber }}</h3><span class="spacer"></span>
            <button class="icon-btn" (click)="selected.set(null)" aria-label="Close"><bb-icon name="close" /></button></div>
          <div class="row"><bb-status [status]="o.status" /><bb-status [payment]="o.payment.status" />
            <span class="badge">{{ o.payment.method === 'CashOnDelivery' ? 'COD' : 'Online' }}</span></div>

          @if (o.allowedNextStatuses.length || o.canCancel) {
            <div class="next">
              @for (s of o.allowedNextStatuses; track s) {
                <button class="btn btn-primary" (click)="move(o, s)" [disabled]="busy()">{{ action(s) }}</button>
              }
              @if (o.canCancel) {
                @if (cancelling()) {
                  <input class="input" [(ngModel)]="cancelNote" placeholder="Reason for cancelling (required)" maxlength="200" />
                  <div class="row"><button class="btn btn-danger btn-sm" (click)="move(o, 'Cancelled')" [disabled]="!cancelNote.trim() || busy()">Confirm cancel</button>
                    <button class="btn btn-ghost btn-sm" (click)="cancelling.set(false)">Back</button></div>
                } @else { <button class="btn btn-danger btn-sm" (click)="cancelling.set(true)">Cancel order</button> }
              }
            </div>
          }
          @if (o.payment.status === 'RefundPending' && auth.isAdmin()) {
            <button class="btn btn-sm" (click)="refund(o)" [disabled]="busy()">Mark refund as completed</button>
          }

          <h4>Customer</h4>
          <p class="small">{{ o.customerName }} · <a [href]="'tel:' + o.customerPhone">{{ o.customerPhone }}</a></p>
          <h4>{{ o.fulfillmentType === 'Delivery' ? 'Deliver to' : 'Pickup at counter' }}</h4>
          @if (o.deliveryAddress; as a) { <p class="small">{{ a.recipientName }}, {{ a.line1 }}@if (a.line2) {, {{ a.line2 }}}, {{ a.city }} {{ a.pincode }}@if (a.landmark) { (near {{ a.landmark }})} · {{ a.phone }}</p> }
          @if (o.notes) { <div class="note"><bb-icon name="chef" [size]="16" /> {{ o.notes }}</div> }

          <h4>Items</h4>
          <ul class="items">@for (i of o.items; track i.productId) {<li><strong>{{ i.quantity }}×</strong> {{ i.productName }}<span class="spacer"></span>{{ i.lineTotal | inr }}</li>}</ul>
          <div class="row total"><span>Total</span><span class="spacer"></span><strong>{{ o.total | inr: true }}</strong></div>

          <h4>Timeline</h4>
          <ul class="timeline">@for (t of o.timeline; track $index) {<li><span class="small"><strong>{{ label(t.to) }}</strong> · {{ utc(t.at) | date: 'd MMM, h:mm a' }}</span>@if (t.note) {<span class="muted small">{{ t.note }}</span>}</li>}</ul>
        </aside>
      }
    </div>`,
  styles: [ADMIN_PAGE, `
    .layout { display: grid; grid-template-columns: 1fr; gap: var(--s4); align-items: start; }
    .layout.with-detail { grid-template-columns: minmax(0, 1fr) 380px; }
    .clickable { cursor: pointer; }
    .sel { background: var(--bb-primary-soft) !important; }
    .ellipsis { max-width: 240px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .detail { position: sticky; top: 16px; display: flex; flex-direction: column; gap: var(--s2); max-height: calc(100vh - 32px); overflow: auto; }
    .detail h4 { margin: var(--s3) 0 0; font-size: .8rem; text-transform: uppercase; letter-spacing: .06em; color: var(--bb-muted); }
    .detail .row { flex-wrap: wrap; gap: 6px; }
    .next { display: flex; flex-direction: column; gap: 8px; padding: var(--s3); background: var(--bb-surface-2); border-radius: 12px; margin-top: var(--s2); }
    .note { padding: 10px 12px; border-radius: 10px; background: var(--bb-warning-soft); color: var(--bb-warning); font-size: .86rem; display: flex; gap: 8px; }
    .items, .timeline { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 6px; font-size: .88rem; }
    .items li { display: flex; gap: 6px; }
    .timeline li { display: flex; flex-direction: column; padding-left: 12px; border-left: 2px solid var(--bb-border); }
    .total { font-size: 1rem; border-top: 1px dashed var(--bb-border); padding-top: 8px; }
    a { color: var(--bb-primary-ink); }
    @media (max-width: 1100px) { .layout.with-detail { grid-template-columns: 1fr; } .detail { position: static; max-height: none; } }`],
})
export class OrdersPage {
  private readonly api = inject(AdminApi);
  private readonly toast = inject(ToastService);
  protected readonly auth = inject(AuthStore);
  protected readonly utc = utc;
  protected readonly statuses: OrderStatus[] = ['AwaitingPayment', 'Placed', 'Confirmed', 'Preparing', 'Ready', 'OutForDelivery', 'Delivered', 'PickedUp', 'Cancelled', 'PaymentFailed'];

  protected readonly result = signal<PagedResult<OrderSummary> | null>(null);
  protected readonly selected = signal<OrderDetail | null>(null);
  protected readonly busy = signal(false);
  protected readonly cancelling = signal(false);
  protected cancelNote = '';
  protected activeOnly = !this.auth.isAdmin();
  protected status: OrderStatus | '' = '';
  protected fulfillment: FulfillmentType | '' = '';
  protected search = '';
  protected page = 1;

  constructor() {
    const params = inject(ActivatedRoute).snapshot.queryParamMap;
    if (params.has('active')) this.activeOnly = true;
    this.load();
    const open = params.get('open');
    if (open) this.open(open);
    // The live queue refreshes itself every 30s so the kitchen sees new orders without reloading.
    interval(30_000).pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => { if (this.activeOnly) this.load(); });
  }

  protected label(s: OrderStatus): string { return statusLabel(s); }
  protected action(s: OrderStatus): string { return ACTION_LABEL[s] ?? statusLabel(s); }

  protected load(): void {
    this.api.orders({
      activeOnly: this.activeOnly || undefined, status: this.status || undefined, fulfillment: this.fulfillment || undefined,
      search: this.search.trim() || undefined, page: this.page, pageSize: 20,
    }).subscribe({ next: r => this.result.set(r), error: e => this.toast.error(e) });
  }

  protected open(orderNumber: string): void {
    this.cancelling.set(false);
    this.api.order(orderNumber).subscribe({ next: o => this.selected.set(o), error: e => this.toast.error(e) });
  }

  protected move(o: OrderDetail, to: OrderStatus): void {
    this.busy.set(true);
    this.api.changeStatus(o.orderNumber, to, to === 'Cancelled' ? this.cancelNote.trim() : null).subscribe({
      next: updated => {
        this.selected.set(updated);
        this.busy.set(false);
        this.cancelling.set(false);
        this.cancelNote = '';
        this.toast.success(`${o.orderNumber} → ${statusLabel(to)}`);
        this.load();
      },
      error: e => { this.busy.set(false); this.toast.error(e); },
    });
  }

  protected refund(o: OrderDetail): void {
    this.busy.set(true);
    this.api.markRefunded(o.orderNumber).subscribe({
      next: updated => { this.selected.set(updated); this.busy.set(false); this.toast.success('Refund recorded'); },
      error: e => { this.busy.set(false); this.toast.error(e); },
    });
  }
}
