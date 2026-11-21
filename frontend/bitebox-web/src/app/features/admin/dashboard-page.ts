import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminApi } from '../../core/api/api';
import { Dashboard } from '../../core/api/models';
import { Icon } from '../../shared/icon';
import { ErrorState, InrPipe, SmartImage, StatusBadge, statusLabel, utc } from '../../shared/ui';
import { ADMIN_PAGE } from './admin-layout';
import { BarDatum, BarList, RevenueChart } from './charts';

/** yyyy-mm-dd in local time. */
export const isoDate = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
export const daysAgo = (n: number) => isoDate(new Date(Date.now() - n * 86_400_000));

@Component({
  selector: 'bb-admin-dashboard',
  imports: [RouterLink, FormsModule, DatePipe, Icon, InrPipe, SmartImage, StatusBadge, ErrorState, RevenueChart, BarList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <div><span class="eyebrow">BiteBox Admin</span><h1>Dashboard</h1></div>
      <div class="range" role="group" aria-label="Date range">
        @for (p of presets; track p.days) {
          <button class="chip" [class.active]="preset() === p.days" (click)="usePreset(p.days)">{{ p.label }}</button>
        }
        <input class="input" type="date" [(ngModel)]="from" (change)="preset.set(0); load()" aria-label="From date" [max]="to" />
        <span class="muted">→</span>
        <input class="input" type="date" [(ngModel)]="to" (change)="preset.set(0); load()" aria-label="To date" [min]="from" />
      </div>
    </div>

    @if (error()) { <bb-error message="Could not load dashboard data." (retry)="load()" /> }
    @if (data(); as d) {
      <section class="kpis">
        <div class="kpi"><span class="label">Revenue</span><strong>{{ d.revenue | inr }}</strong><span class="hint">{{ d.totalOrders }} paid/placed orders</span></div>
        <div class="kpi"><span class="label">Avg. order value</span><strong>{{ d.averageOrderValue | inr }}</strong><span class="hint">excl. cancelled</span></div>
        <div class="kpi"><span class="label">Active right now</span><strong>{{ d.activeOrders }}</strong><a routerLink="/admin/orders" [queryParams]="{ active: 1 }" class="hint link">Open kitchen queue →</a></div>
        <div class="kpi"><span class="label">Completed</span><strong>{{ d.completedOrders }}</strong><span class="hint">{{ d.cancelledOrders }} cancelled</span></div>
        <div class="kpi"><span class="label">New customers</span><strong>{{ d.newCustomers }}</strong><span class="hint">in this period</span></div>
      </section>

      <section class="grid-2">
        <div class="card card-pad span">
          <div class="card-head"><div><h3>Revenue per day</h3><span class="muted small">{{ d.from }} to {{ d.to }} · IST business days</span></div>
            <button class="btn btn-sm btn-ghost" (click)="table.set(!table())">{{ table() ? 'Show chart' : 'Show table' }}</button></div>
          <bb-revenue-chart [data]="d.salesByDay" [showTable]="table()" />
        </div>
        <div class="card card-pad">
          <div class="card-head"><h3>Top dishes</h3><span class="muted small">by portions sold</span></div>
          <bb-bar-list [data]="topBars()" />
        </div>
        <div class="card card-pad">
          <div class="card-head"><h3>Orders by status</h3><span class="muted small">placed in this period</span></div>
          <bb-bar-list [data]="statusBars()" />
        </div>
        <div class="card card-pad">
          <div class="card-head"><h3>Low stock</h3><a routerLink="/admin/inventory" class="small link">Manage inventory →</a></div>
          @for (i of d.lowStock; track i.productId) {
            <div class="low"><bb-img class="thumb" [src]="i.imageUrl" [alt]="i.name" /><div><strong>{{ i.name }}</strong><span class="muted small">{{ i.categoryName }}</span></div>
              <span class="spacer"></span><span class="badge" [class.badge-danger]="i.stockQuantity === 0" [class.badge-warning]="i.stockQuantity > 0">{{ i.stockQuantity === 0 ? 'Out of stock' : i.stockQuantity + ' left' }}</span></div>
          } @empty { <p class="muted small"><bb-icon name="check" [size]="14" /> All dishes are above their stock threshold.</p> }
        </div>
        <div class="card span">
          <div class="card-head card-pad" style="padding-bottom:0"><h3>Latest orders</h3><a routerLink="/admin/orders" class="small link">All orders →</a></div>
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Order</th><th>Customer</th><th>Items</th><th>Status</th><th>Placed</th><th class="num">Total</th></tr></thead>
            <tbody>@for (o of d.recentOrders; track o.id) {
              <tr><td><a [routerLink]="['/admin/orders']" [queryParams]="{ open: o.orderNumber }" class="link">{{ o.orderNumber }}</a></td><td>{{ o.customerName }}</td>
                <td class="muted ellipsis">{{ o.itemsPreview }}</td><td><bb-status [status]="o.status" /></td>
                <td class="muted">{{ utc(o.placedAt) | date: 'd MMM, h:mm a' }}</td><td class="num">{{ o.total | inr: true }}</td></tr>
            }</tbody></table></div>
        </div>
      </section>
    } @else if (!error()) {
      <div class="kpis">@for (i of [1,2,3,4,5]; track i) {<div class="skeleton" style="height:104px"></div>}</div>
      <div class="skeleton" style="height:320px;margin-top:20px"></div>
    }`,
  styles: [ADMIN_PAGE, `
    .range { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .range .input { width: auto; min-height: 36px; padding: 4px 10px; }
    .kpis { display: grid; grid-template-columns: repeat(5, 1fr); gap: var(--s4); margin-bottom: var(--s5); }
    .kpi { background: var(--bb-surface); border: 1px solid var(--bb-border); border-radius: var(--bb-radius); padding: var(--s4) var(--s5); display: flex; flex-direction: column; gap: 2px; }
    .kpi .label { font-size: .8rem; color: var(--bb-muted); font-weight: 600; }
    .kpi strong { font-size: 1.7rem; font-weight: 800; letter-spacing: -.02em; font-variant-numeric: tabular-nums; }
    .kpi .hint { font-size: .78rem; color: var(--bb-muted); }
    .link { color: var(--bb-primary-ink); font-weight: 650; }
    .grid-2 { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--s4); }
    .span { grid-column: 1 / -1; }
    .card-head { display: flex; justify-content: space-between; align-items: flex-start; gap: 8px; margin-bottom: var(--s4); }
    .card-head h3 { margin: 0; font-family: var(--bb-font); font-size: 1rem; }
    .low { display: flex; align-items: center; gap: 10px; padding: 8px 0; border-bottom: 1px solid var(--bb-border); }
    .low div { display: flex; flex-direction: column; }
    .ellipsis { max-width: 260px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    @media (max-width: 1200px) { .kpis { grid-template-columns: repeat(3, 1fr); } .grid-2 { grid-template-columns: 1fr 1fr; } }
    @media (max-width: 700px) { .kpis { grid-template-columns: 1fr 1fr; } .grid-2 { grid-template-columns: 1fr; } }`],
})
export class DashboardPage {
  private readonly api = inject(AdminApi);
  protected readonly utc = utc;
  protected readonly presets = [{ days: 7, label: '7 days' }, { days: 30, label: '30 days' }, { days: 90, label: '90 days' }];
  protected readonly preset = signal(30);
  protected readonly data = signal<Dashboard | null>(null);
  protected readonly error = signal(false);
  protected readonly table = signal(false);
  protected from = daysAgo(29);
  protected to = isoDate(new Date());

  protected readonly topBars = computed<BarDatum[]>(() => (this.data()?.topProducts ?? []).map(p => ({
    label: p.name, value: p.quantitySold, display: `${p.quantitySold} sold`, sub: `₹${Math.round(p.revenue).toLocaleString('en-IN')}`,
  })));
  protected readonly statusBars = computed<BarDatum[]>(() => (this.data()?.statusBreakdown ?? [])
    .slice().sort((a, b) => b.count - a.count)
    .map(s => ({ label: statusLabel(s.status), value: s.count, display: String(s.count) })));

  constructor() { this.load(); }

  protected usePreset(days: number): void {
    this.preset.set(days);
    this.from = daysAgo(days - 1);
    this.to = isoDate(new Date());
    this.load();
  }

  protected load(): void {
    this.error.set(false);
    this.api.dashboard(this.from, this.to).subscribe({ next: d => this.data.set(d), error: () => this.error.set(true) });
  }
}
