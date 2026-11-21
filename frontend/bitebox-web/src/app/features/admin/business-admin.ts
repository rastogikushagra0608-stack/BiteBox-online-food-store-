import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { AdminApi } from '../../core/api/api';
import { AdminUser, Coupon, DailySales, DiscountType, OrderSummary, PagedResult, Role, TopProduct } from '../../core/api/models';
import { AuthStore } from '../../core/state/auth.store';
import { ToastService } from '../../core/state/toast';
import { Icon } from '../../shared/icon';
import { InrPipe, StatusBadge, utc } from '../../shared/ui';
import { INDIAN_MOBILE, strongPassword } from '../auth/auth-pages';
import { ADMIN_PAGE } from './admin-layout';
import { BarDatum, BarList, RevenueChart } from './charts';
import { daysAgo, isoDate } from './dashboard-page';

/* ============================== Promotions ============================== */

const toLocalInput = (iso: string) => { const d = new Date(utc(iso)); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16); };

@Component({
  selector: 'bb-admin-coupons',
  imports: [ReactiveFormsModule, DatePipe, Icon, InrPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><div><span class="eyebrow">Marketing</span><h1>Promotions</h1></div>
      <button class="btn btn-primary" (click)="edit(null)"><bb-icon name="plus" [size]="18" /> New coupon</button></div>
    <div class="card"><div class="table-wrap"><table class="table">
      <thead><tr><th>Code</th><th>Offer</th><th>Min order</th><th>Valid</th><th class="num">Used</th><th>Status</th><th></th></tr></thead>
      <tbody>@for (c of items(); track c.id) {
        <tr><td><strong class="code">{{ c.code }}</strong></td>
          <td>{{ c.discountType === 'Flat' ? (c.value | inr) + ' off' : c.value + '% off' }}@if (c.maxDiscountAmount) { <span class="muted small">(max {{ c.maxDiscountAmount | inr }})</span>}<br /><span class="muted small">{{ c.description }}</span></td>
          <td>{{ c.minOrderAmount | inr }}</td>
          <td class="small">{{ utc(c.startsAt) | date: 'd MMM' }} – {{ utc(c.endsAt) | date: 'd MMM y' }}</td>
          <td class="num">{{ c.usedCount }}@if (c.usageLimit) { / {{ c.usageLimit }}}</td>
          <td>@if (c.isCurrentlyValid) {<span class="badge badge-success">Live</span>} @else if (!c.isActive) {<span class="badge">Inactive</span>} @else {<span class="badge badge-warning">Not live</span>}</td>
          <td><div class="actions-cell"><button class="btn btn-sm" (click)="edit(c)">Edit</button><button class="btn btn-sm btn-danger" (click)="remove(c)">Delete</button></div></td></tr>
      }</tbody></table></div></div>

    @if (editing() !== undefined) {
      <div class="modal-scrim" (click)="editing.set(undefined)">
        <form class="modal" [formGroup]="form" (ngSubmit)="save()" (click)="$event.stopPropagation()">
          <h2>{{ editing() ? 'Edit ' + editing()!.code : 'New coupon' }}</h2>
          <div class="form-grid">
            <div class="field"><label for="cp-code">Code</label><input id="cp-code" class="input" formControlName="code" style="text-transform:uppercase" /></div>
            <div class="field"><label for="cp-type">Type</label><select id="cp-type" class="input" formControlName="discountType"><option value="Percentage">Percentage</option><option value="Flat">Flat amount</option></select></div>
            <div class="field full"><label for="cp-desc">Description shown to customers</label><input id="cp-desc" class="input" formControlName="description" /></div>
            <div class="field"><label for="cp-val">{{ form.value.discountType === 'Flat' ? 'Amount (₹)' : 'Percent (%)' }}</label><input id="cp-val" class="input" type="number" formControlName="value" /></div>
            <div class="field"><label for="cp-max">Max discount (₹, % only)</label><input id="cp-max" class="input" type="number" formControlName="maxDiscountAmount" /></div>
            <div class="field"><label for="cp-min">Minimum order (₹)</label><input id="cp-min" class="input" type="number" formControlName="minOrderAmount" /></div>
            <div class="field"><label for="cp-per">Uses per customer</label><input id="cp-per" class="input" type="number" formControlName="perUserLimit" /></div>
            <div class="field"><label for="cp-start">Starts</label><input id="cp-start" class="input" type="datetime-local" formControlName="startsAt" /></div>
            <div class="field"><label for="cp-end">Ends</label><input id="cp-end" class="input" type="datetime-local" formControlName="endsAt" /></div>
            <div class="field"><label for="cp-limit">Total usage limit (blank = unlimited)</label><input id="cp-limit" class="input" type="number" formControlName="usageLimit" /></div>
            <label class="check"><input type="checkbox" formControlName="isActive" /> Active</label>
          </div>
          <div class="row" style="margin-top:var(--s5)"><button class="btn btn-primary" [disabled]="form.invalid">Save</button>
            <button class="btn btn-ghost" type="button" (click)="editing.set(undefined)">Cancel</button></div>
        </form>
      </div>
    }`,
  styles: [ADMIN_PAGE, `.code { font-family: ui-monospace, monospace; background: var(--bb-primary-soft); color: var(--bb-primary-ink); padding: 2px 8px; border-radius: 6px; }`],
})
export class AdminCouponsPage {
  private readonly api = inject(AdminApi);
  private readonly toast = inject(ToastService);
  protected readonly utc = utc;
  protected readonly items = signal<Coupon[]>([]);
  protected readonly editing = signal<Coupon | null | undefined>(undefined);
  protected readonly form = inject(FormBuilder).group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{3,30}$/)]],
    description: ['', [Validators.required, Validators.maxLength(150)]],
    discountType: ['Percentage' as DiscountType],
    value: [10, [Validators.required, Validators.min(1)]],
    maxDiscountAmount: [null as number | null],
    minOrderAmount: [0, [Validators.min(0)]],
    perUserLimit: [1, [Validators.required, Validators.min(1)]],
    usageLimit: [null as number | null],
    startsAt: ['', Validators.required],
    endsAt: ['', Validators.required],
    isActive: [true],
  });

  constructor() { this.load(); }
  protected load(): void { this.api.coupons().subscribe({ next: c => this.items.set(c), error: e => this.toast.error(e) }); }

  protected edit(c: Coupon | null): void {
    const now = new Date().toISOString();
    this.form.reset({
      code: c?.code ?? '', description: c?.description ?? '', discountType: c?.discountType ?? 'Percentage', value: c?.value ?? 10,
      maxDiscountAmount: c?.maxDiscountAmount ?? null, minOrderAmount: c?.minOrderAmount ?? 0, perUserLimit: c?.perUserLimit ?? 1,
      usageLimit: c?.usageLimit ?? null, startsAt: toLocalInput(c?.startsAt ?? now),
      endsAt: toLocalInput(c?.endsAt ?? new Date(Date.now() + 30 * 86400000).toISOString()), isActive: c?.isActive ?? true,
    });
    this.editing.set(c);
  }

  protected save(): void {
    const v = this.form.getRawValue();
    const body = {
      code: v.code!.toUpperCase(), description: v.description!, discountType: v.discountType!, value: Number(v.value),
      maxDiscountAmount: v.maxDiscountAmount ? Number(v.maxDiscountAmount) : null, minOrderAmount: Number(v.minOrderAmount),
      perUserLimit: Number(v.perUserLimit), usageLimit: v.usageLimit ? Number(v.usageLimit) : null,
      startsAt: new Date(v.startsAt!).toISOString(), endsAt: new Date(v.endsAt!).toISOString(), isActive: !!v.isActive,
    };
    const existing = this.editing();
    (existing ? this.api.updateCoupon(existing.id, body) : this.api.createCoupon(body)).subscribe({
      next: () => { this.toast.success('Coupon saved'); this.editing.set(undefined); this.load(); },
      error: e => this.toast.error(e),
    });
  }

  protected remove(c: Coupon): void {
    this.api.deleteCoupon(c.id).subscribe({
      next: () => { this.toast.success(c.usedCount ? 'Coupon deactivated (it has been used)' : 'Coupon deleted'); this.load(); },
      error: e => this.toast.error(e),
    });
  }
}

/* ============================== Customers & staff ============================== */

@Component({
  selector: 'bb-admin-users',
  imports: [FormsModule, ReactiveFormsModule, DatePipe, Icon, InrPipe, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><div><span class="eyebrow">People</span><h1>Customers & staff</h1></div>
      <button class="btn btn-primary" (click)="adding.set(true)"><bb-icon name="plus" [size]="18" /> Add staff member</button></div>
    <div class="toolbar">
      <input class="input grow" [(ngModel)]="search" (keyup.enter)="page = 1; load()" placeholder="Search name, email or phone" aria-label="Search users" />
      <select class="input" [(ngModel)]="role" (ngModelChange)="page = 1; load()" aria-label="Role"><option value="">All roles</option>@for (r of roles; track r) {<option [value]="r">{{ r }}</option>}</select>
    </div>
    <div class="card">
      @if (result(); as r) {
        <div class="table-wrap"><table class="table">
          <thead><tr><th>Name</th><th>Contact</th><th>Role</th><th class="num">Orders</th><th class="num">Spent</th><th>Joined</th><th>Status</th><th></th></tr></thead>
          <tbody>@for (u of r.items; track u.id) {
            <tr><td><strong>{{ u.fullName }}</strong></td><td class="small">{{ u.email }}<br /><span class="muted">{{ u.phone }}</span></td>
              <td><select class="input role" [ngModel]="u.roles[0]" (ngModelChange)="setRole(u, $event)" [disabled]="u.id === auth.user()?.id" [attr.aria-label]="'Role for ' + u.fullName">
                @for (ro of roles; track ro) {<option [value]="ro">{{ ro }}</option>}</select></td>
              <td class="num">{{ u.orderCount }}</td><td class="num">{{ u.totalSpent | inr }}</td>
              <td class="muted small">{{ utc(u.createdAt) | date: 'd MMM y' }}</td>
              <td>@if (u.isActive) {<span class="badge badge-success">Active</span>} @else {<span class="badge badge-danger">Blocked</span>}</td>
              <td><div class="actions-cell">
                @if (u.orderCount) { <button class="btn btn-sm" (click)="showOrders(u)">Orders</button> }
                @if (u.id !== auth.user()?.id) { <button class="btn btn-sm" [class.btn-danger]="u.isActive" (click)="toggleActive(u)">{{ u.isActive ? 'Block' : 'Unblock' }}</button> }
              </div></td></tr>
          }</tbody></table></div>
        <div class="pager"><span class="muted small">{{ r.totalCount }} people</span>
          <button class="btn btn-sm" [disabled]="r.page <= 1" (click)="page = r.page - 1; load()">Prev</button>
          <button class="btn btn-sm" [disabled]="r.page >= r.totalPages" (click)="page = r.page + 1; load()">Next</button></div>
      }
    </div>

    @if (ordersOf(); as o) {
      <div class="modal-scrim" (click)="ordersOf.set(null)"><div class="modal" (click)="$event.stopPropagation()">
        <h2>{{ o.user.fullName }}'s orders</h2>
        <table class="table"><tbody>@for (x of o.orders; track x.id) {
          <tr><td><strong>{{ x.orderNumber }}</strong></td><td class="muted small">{{ x.itemsPreview }}</td><td><bb-status [status]="x.status" /></td><td class="num">{{ x.total | inr: true }}</td></tr>
        }</tbody></table>
        <button class="btn" style="margin-top:var(--s4)" (click)="ordersOf.set(null)">Close</button></div></div>
    }

    @if (adding()) {
      <div class="modal-scrim" (click)="adding.set(false)">
        <form class="modal" [formGroup]="staff" (ngSubmit)="createStaff()" (click)="$event.stopPropagation()">
          <h2>New staff account</h2>
          <div class="form-grid">
            <div class="field"><label for="s-name">Full name</label><input id="s-name" class="input" formControlName="fullName" /></div>
            <div class="field"><label for="s-role">Role</label><select id="s-role" class="input" formControlName="role"><option>Kitchen</option><option>Delivery</option><option>Admin</option></select></div>
            <div class="field"><label for="s-email">Email</label><input id="s-email" class="input" type="email" formControlName="email" /></div>
            <div class="field"><label for="s-phone">Mobile</label><input id="s-phone" class="input" formControlName="phone" maxlength="10" /></div>
            <div class="field full"><label for="s-pw">Temporary password</label><input id="s-pw" class="input" type="password" formControlName="password" autocomplete="new-password" />
              <span class="small muted">8+ characters with upper, lower case and a digit. Share it securely.</span></div>
          </div>
          <div class="row" style="margin-top:var(--s5)"><button class="btn btn-primary" [disabled]="staff.invalid">Create account</button><button class="btn btn-ghost" type="button" (click)="adding.set(false)">Cancel</button></div>
        </form>
      </div>
    }`,
  styles: [ADMIN_PAGE, `.role { min-height: 34px; padding: 4px 8px; width: auto; }`],
})
export class AdminUsersPage {
  private readonly api = inject(AdminApi);
  private readonly toast = inject(ToastService);
  protected readonly auth = inject(AuthStore);
  protected readonly utc = utc;
  protected readonly roles: Role[] = ['Customer', 'Kitchen', 'Delivery', 'Admin'];
  protected readonly result = signal<PagedResult<AdminUser> | null>(null);
  protected readonly ordersOf = signal<{ user: AdminUser; orders: OrderSummary[] } | null>(null);
  protected readonly adding = signal(false);
  protected search = '';
  protected role = '';
  protected page = 1;
  protected readonly staff = inject(FormBuilder).nonNullable.group({
    fullName: ['', Validators.required], role: ['Kitchen'], email: ['', [Validators.required, Validators.email]],
    phone: ['', [Validators.required, Validators.pattern(INDIAN_MOBILE)]], password: ['', [Validators.required, strongPassword]],
  });

  constructor() { this.load(); }
  protected load(): void {
    this.api.users({ search: this.search.trim() || undefined, role: this.role || undefined, page: this.page })
      .subscribe({ next: r => this.result.set(r), error: e => this.toast.error(e) });
  }
  protected setRole(u: AdminUser, role: string): void {
    this.api.setUserRole(u.id, role).subscribe({ next: () => { this.toast.success(`${u.fullName} is now ${role}`); this.load(); }, error: e => { this.toast.error(e); this.load(); } });
  }
  protected toggleActive(u: AdminUser): void {
    this.api.setUserActive(u.id, !u.isActive).subscribe({ next: () => { this.toast.success(u.isActive ? 'Account blocked' : 'Account unblocked'); this.load(); }, error: e => this.toast.error(e) });
  }
  protected showOrders(u: AdminUser): void {
    this.api.userOrders(u.id).subscribe({ next: r => this.ordersOf.set({ user: u, orders: r.items }), error: e => this.toast.error(e) });
  }
  protected createStaff(): void {
    this.api.createStaff(this.staff.getRawValue()).subscribe({
      next: () => { this.toast.success('Staff account created'); this.adding.set(false); this.staff.reset({ role: 'Kitchen' }); this.load(); },
      error: e => this.toast.error(e),
    });
  }
}

/* ============================== Reports ============================== */

@Component({
  selector: 'bb-admin-reports',
  imports: [FormsModule, Icon, InrPipe, RevenueChart, BarList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><div><span class="eyebrow">Insights</span><h1>Reports</h1></div>
      <div class="row"><input class="input" type="date" [(ngModel)]="from" [max]="to" aria-label="From" /><span>→</span><input class="input" type="date" [(ngModel)]="to" [min]="from" aria-label="To" />
        <button class="btn btn-dark btn-sm" (click)="load()">Run report</button></div></div>

    <div class="grid">
      <section class="card card-pad">
        <div class="row"><h3>Sales by day</h3><span class="spacer"></span>
          <a class="btn btn-sm" [href]="csv('sales')" download><bb-icon name="download" [size]="16" /> CSV</a></div>
        <div class="totals"><div><span class="muted small">Revenue</span><strong>{{ totalRevenue() | inr }}</strong></div><div><span class="muted small">Orders</span><strong>{{ totalOrders() }}</strong></div></div>
        <bb-revenue-chart [data]="sales()" [showTable]="true" />
      </section>
      <section class="card card-pad">
        <div class="row"><h3>Dish performance</h3><span class="spacer"></span>
          <a class="btn btn-sm" [href]="csv('products')" download><bb-icon name="download" [size]="16" /> CSV</a></div>
        <bb-bar-list [data]="productBars()" />
      </section>
    </div>`,
  styles: [ADMIN_PAGE, `
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: var(--s4); align-items: start; }
    h3 { margin: 0; font-family: var(--bb-font); font-size: 1rem; }
    .totals { display: flex; gap: var(--s6); margin: var(--s4) 0; } .totals div { display: flex; flex-direction: column; } .totals strong { font-size: 1.5rem; }
    .head .input { width: auto; min-height: 36px; }
    bb-bar-list { display: block; margin-top: var(--s4); max-height: 640px; overflow: auto; }
    @media (max-width: 1100px) { .grid { grid-template-columns: 1fr; } }`],
})
export class AdminReportsPage {
  private readonly api = inject(AdminApi);
  private readonly toast = inject(ToastService);
  protected from = daysAgo(29);
  protected to = isoDate(new Date());
  protected readonly sales = signal<DailySales[]>([]);
  protected readonly products = signal<TopProduct[]>([]);
  protected readonly totalRevenue = signal(0);
  protected readonly totalOrders = signal(0);
  protected readonly productBars = signal<BarDatum[]>([]);

  constructor() { this.load(); }

  protected csv(kind: 'sales' | 'products'): string { return this.api.csvUrl(kind, this.from, this.to); }

  protected load(): void {
    this.api.sales(this.from, this.to).subscribe({
      next: s => { this.sales.set(s); this.totalRevenue.set(s.reduce((a, x) => a + x.revenue, 0)); this.totalOrders.set(s.reduce((a, x) => a + x.orders, 0)); },
      error: e => this.toast.error(e),
    });
    this.api.productReport(this.from, this.to).subscribe({
      next: p => this.productBars.set(p.map(x => ({ label: x.name, value: x.revenue, display: `₹${Math.round(x.revenue).toLocaleString('en-IN')}`, sub: `${x.quantitySold} sold` }))),
      error: e => this.toast.error(e),
    });
  }
}
