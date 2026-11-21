import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { DailySales } from '../../core/api/models';
import { InrPipe } from '../../shared/ui';

/**
 * Daily revenue as a single-series area chart (hand-rolled SVG, no chart library).
 * One series → no legend; the card title names it. Crosshair tooltip on hover/focus, plus a table view.
 */
@Component({
  selector: 'bb-revenue-chart',
  imports: [InrPipe, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (showTable()) {
      <div class="table-wrap tbl">
        <table class="table"><thead><tr><th>Date</th><th class="num">Orders</th><th class="num">Revenue</th></tr></thead>
          <tbody>@for (d of data(); track d.date) {<tr><td>{{ label(d.date) }}</td><td class="num">{{ d.orders }}</td><td class="num">{{ d.revenue | inr }}</td></tr>}</tbody></table>
      </div>
    } @else {
      <div class="chart" (pointerleave)="hover.set(null)">
        <div class="y-axis">@for (t of ticks(); track t.value) {<span [style.top.%]="(t.y / H) * 100">{{ t.value | inr }}</span>}</div>
        <div class="plot">
        <svg [attr.viewBox]="'0 0 ' + W + ' ' + H" preserveAspectRatio="none" role="img" [attr.aria-label]="ariaLabel()">
          @for (t of ticks(); track t.value) {
            <line [attr.x1]="P.l" [attr.x2]="W - P.r" [attr.y1]="t.y" [attr.y2]="t.y" class="grid" />
          }
          <path [attr.d]="area()" class="area" />
          <path [attr.d]="line()" class="line" />
          @if (hover() !== null) {
            <line [attr.x1]="x(hover()!)" [attr.x2]="x(hover()!)" [attr.y1]="P.t" [attr.y2]="H - P.b" class="cross" />
            <circle [attr.cx]="x(hover()!)" [attr.cy]="y(data()[hover()!].revenue)" r="5" class="dot" />
          }
          @for (d of data(); track d.date; let i = $index) {
            <rect [attr.x]="x(i) - step() / 2" [attr.y]="P.t" [attr.width]="step()" [attr.height]="H - P.t - P.b" class="hit"
                  tabindex="0" (pointerenter)="hover.set(i)" (focus)="hover.set(i)" (blur)="hover.set(null)"
                  [attr.aria-label]="label(d.date) + ': ' + (d.revenue | inr) + ', ' + d.orders + ' orders'" />
          }
        </svg>
        <div class="x-axis">@for (l of xLabels(); track l.i) {<span [style.left.%]="(x(l.i) / W) * 100">{{ l.text }}</span>}</div>
        @if (hover() !== null) {
          @let d = data()[hover()!];
          <div class="tip" [style.left.%]="(x(hover()!) / W) * 100" [class.flip]="hover()! > data().length * 0.7">
            <strong>{{ d.revenue | inr }}</strong>
            <span><i class="key"></i>{{ d.orders }} orders · {{ label(d.date) }}</span>
            @if (d.orders) {<span class="muted">Avg {{ d.revenue / d.orders | number: '1.0-0' }} per order</span>}
          </div>
        }
        </div>
      </div>
    }`,
  styles: `
    :host { display: block; }
    .chart { position: relative; height: 260px; display: grid; grid-template-columns: 64px 1fr; }
    .plot { position: relative; height: 236px; }
    svg { width: 100%; height: 100%; display: block; overflow: visible; }
    .grid { stroke: var(--bb-border); stroke-width: 1; vector-effect: non-scaling-stroke; }
    .area { fill: color-mix(in srgb, var(--bb-primary) 16%, transparent); }
    .line { fill: none; stroke: var(--bb-primary); stroke-width: 2; vector-effect: non-scaling-stroke; stroke-linejoin: round; }
    .cross { stroke: var(--bb-ink-2); stroke-width: 1; stroke-dasharray: 3 3; vector-effect: non-scaling-stroke; }
    .dot { fill: var(--bb-primary); stroke: var(--bb-surface); stroke-width: 2; vector-effect: non-scaling-stroke; }
    .hit { fill: transparent; cursor: crosshair; outline: none; }
    .y-axis { position: relative; height: 236px; padding-right: 6px; }
    .y-axis span { position: absolute; right: 0; transform: translateY(-50%); font-size: .72rem; color: var(--bb-muted); font-variant-numeric: tabular-nums; }
    .x-axis { position: absolute; left: 0; right: 0; top: 240px; height: 20px; }
    .x-axis span { position: absolute; transform: translateX(-50%); font-size: .72rem; color: var(--bb-muted); white-space: nowrap; }
    .tip { position: absolute; top: 0; transform: translateX(12px); background: var(--bb-surface); border: 1px solid var(--bb-border); box-shadow: var(--bb-shadow); border-radius: 10px; padding: 8px 12px; display: flex; flex-direction: column; font-size: .8rem; pointer-events: none; white-space: nowrap; z-index: 2; }
    .tip.flip { transform: translateX(calc(-100% - 12px)); }
    .tip strong { font-size: 1rem; }
    .key { display: inline-block; width: 12px; height: 2px; background: var(--bb-primary); margin-right: 6px; vertical-align: middle; }
    .tbl { max-height: 280px; overflow: auto; }`,
})
export class RevenueChart {
  readonly data = input.required<DailySales[]>();
  readonly showTable = input(false);
  protected readonly hover = signal<number | null>(null);

  protected readonly W = 800;
  protected readonly H = 236;
  protected readonly P = { t: 10, r: 8, b: 4, l: 0 };

  private readonly max = computed(() => niceMax(Math.max(1, ...this.data().map(d => d.revenue))));
  protected readonly step = computed(() => (this.W - this.P.l - this.P.r) / Math.max(1, this.data().length - 1));
  protected readonly ticks = computed(() => [0, 0.25, 0.5, 0.75, 1].map(f => ({ value: Math.round(this.max() * f), y: this.y(this.max() * f) })));
  protected readonly xLabels = computed(() => {
    const n = this.data().length;
    const every = Math.max(1, Math.ceil(n / 6));
    return this.data().map((d, i) => ({ i, text: this.label(d.date) })).filter(l => l.i % every === 0 || l.i === n - 1);
  });
  protected readonly line = computed(() => this.data().map((d, i) => `${i ? 'L' : 'M'}${this.x(i)},${this.y(d.revenue)}`).join(''));
  protected readonly area = computed(() => {
    const n = this.data().length;
    if (!n) return '';
    return `${this.line()}L${this.x(n - 1)},${this.H - this.P.b}L${this.x(0)},${this.H - this.P.b}Z`;
  });
  protected readonly ariaLabel = computed(() => {
    const d = this.data();
    const total = d.reduce((s, x) => s + x.revenue, 0);
    return `Daily revenue over ${d.length} days, total ₹${Math.round(total).toLocaleString('en-IN')}. Use Tab to step through days.`;
  });

  protected x(i: number): number { return this.P.l + i * this.step(); }
  protected y(v: number): number { return this.P.t + (1 - v / this.max()) * (this.H - this.P.t - this.P.b); }
  protected label(date: string): string { return new Date(`${date}T00:00:00`).toLocaleDateString('en-IN', { day: 'numeric', month: 'short' }); }
}

/** Rounds an axis maximum up to a "nice" number (1, 2, 2.5, 5 × 10ⁿ). */
function niceMax(v: number): number {
  const exp = Math.pow(10, Math.floor(Math.log10(v)));
  return ([1, 2, 2.5, 5, 10].find(m => m * exp >= v) ?? 10) * exp;
}

export interface BarDatum { label: string; value: number; display: string; sub?: string; }

/**
 * Horizontal bar list (magnitude): one hue, thin bars anchored at zero with rounded data-ends,
 * labels and values in text ink. The value is printed beside each bar, so nothing depends on hover.
 */
@Component({
  selector: 'bb-bar-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul role="list">
      @for (d of data(); track d.label) {
        <li [attr.title]="d.label + ': ' + d.display">
          <div class="row"><span class="label">{{ d.label }}</span>@if (d.sub) {<span class="sub">{{ d.sub }}</span>}<span class="spacer"></span><strong>{{ d.display }}</strong></div>
          <div class="track"><div class="bar" [style.width.%]="(d.value / max()) * 100"></div></div>
        </li>
      } @empty { <li class="muted small">No data for this period.</li> }
    </ul>`,
  styles: `
    ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 12px; }
    li .row { display: flex; gap: 8px; font-size: .86rem; margin-bottom: 5px; }
    .label { font-weight: 600; }
    .sub { color: var(--bb-muted); }
    strong { font-variant-numeric: tabular-nums; }
    .track { height: 8px; }
    .bar { height: 100%; min-width: 2px; background: var(--bb-primary); border-radius: 0 4px 4px 0; transition: width .4s ease; }
    li:hover .bar { background: var(--bb-primary-hover); }`,
})
export class BarList {
  readonly data = input.required<BarDatum[]>();
  protected readonly max = computed(() => Math.max(1, ...this.data().map(d => d.value)));
}
