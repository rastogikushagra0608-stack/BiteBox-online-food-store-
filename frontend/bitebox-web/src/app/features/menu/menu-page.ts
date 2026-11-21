import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { debounceTime, distinctUntilChanged, Subject } from 'rxjs';
import { CatalogApi } from '../../core/api/api';
import { Category, ProductCard, ProductQuery, ProductSort, SpiceLevel } from '../../core/api/models';
import { Icon } from '../../shared/icon';
import { ProductCardComponent } from '../../shared/product-card';
import { EmptyState, ErrorState } from '../../shared/ui';

/**
 * Menu with search, filters and sorting. All filter state lives in the URL query string, so a filtered
 * view can be bookmarked or shared, and the back button behaves as expected.
 */
@Component({
  selector: 'bb-menu',
  imports: [FormsModule, Icon, ProductCardComponent, EmptyState, ErrorState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container page">
      <div class="page-title">
        <div>
          <span class="eyebrow">Our menu</span>
          <h1>{{ heading() }}</h1>
          <p class="muted" style="margin:6px 0 0">{{ total() }} dishes@if (query().search) { matching "{{ query().search }}" }</p>
        </div>
        <div class="search">
          <bb-icon name="search" [size]="18" />
          <input [ngModel]="query().search ?? ''" (ngModelChange)="search$.next($event)" placeholder="Search the menu" aria-label="Search the menu" />
        </div>
      </div>

      <div class="cats" role="tablist" aria-label="Categories">
        <button class="chip" [class.active]="!query().category" (click)="patch({ category: undefined })">All</button>
        @for (c of categories(); track c.id) {
          <button class="chip" [class.active]="query().category === c.slug" (click)="patch({ category: c.slug })">{{ c.name }}</button>
        }
      </div>

      <div class="filters">
        <label class="check"><input type="checkbox" [checked]="query().vegetarian === true" (change)="toggleVeg($event)" /> Veg only</label>
        <label class="check"><input type="checkbox" [checked]="query().inStockOnly === true" (change)="patch({ inStockOnly: check($event) || undefined })" /> In stock</label>
        <select class="input" [ngModel]="query().maxPrice ?? ''" (ngModelChange)="patch({ maxPrice: $event ? +$event : undefined })" aria-label="Maximum price">
          <option value="">Any price</option>
          <option value="150">Under ₹150</option>
          <option value="250">Under ₹250</option>
          <option value="350">Under ₹350</option>
          <option value="500">Under ₹500</option>
        </select>
        <select class="input" [ngModel]="query().maxSpice ?? ''" (ngModelChange)="patch({ maxSpice: $event || undefined })" aria-label="Spice level">
          <option value="">Any spice</option>
          <option value="None">No spice</option>
          <option value="Mild">Up to mild</option>
          <option value="Medium">Up to medium</option>
        </select>
        <span class="spacer"></span>
        <label class="sort"><span class="muted small">Sort</span>
          <select class="input" [ngModel]="query().sort ?? 'Popular'" (ngModelChange)="patch({ sort: $event })" aria-label="Sort by">
            <option value="Popular">Popularity</option>
            <option value="Rating">Rating</option>
            <option value="PriceLowToHigh">Price: low to high</option>
            <option value="PriceHighToLow">Price: high to low</option>
            <option value="Newest">Newest</option>
          </select>
        </label>
        @if (hasFilters()) { <button class="btn btn-ghost btn-sm" (click)="reset()">Clear filters</button> }
      </div>

      @if (error()) {
        <bb-error message="We couldn't load the menu." (retry)="fetch(true)" />
      } @else if (!loading() && !products().length) {
        <bb-empty icon="search" title="No dishes match those filters" text="Try a different search or clear a filter or two.">
          <button class="btn" (click)="reset()">Clear filters</button>
        </bb-empty>
      } @else {
        <div class="grid-products">
          @for (p of products(); track p.id) { <bb-product-card [product]="p" class="fade-up" /> }
          @if (loading()) { @for (i of [1,2,3,4]; track i) { <div class="skeleton" style="height:360px;border-radius:22px"></div> } }
        </div>
        @if (hasMore() && !loading()) {
          <div class="more"><button class="btn" (click)="loadMore()">Show more dishes</button></div>
        }
      }
    </div>`,
  styles: `
    .search { display: flex; align-items: center; gap: 8px; height: 46px; padding: 0 16px; border: 1.5px solid var(--bb-border-strong); border-radius: 999px; background: var(--bb-surface); width: min(360px, 100%); color: var(--bb-muted); }
    .search:focus-within { border-color: var(--bb-primary); box-shadow: 0 0 0 4px var(--bb-primary-soft); }
    .search input { border: 0; outline: none; background: none; font: inherit; flex: 1; color: var(--bb-ink); min-width: 0; }
    .cats { display: flex; gap: 8px; overflow-x: auto; padding-bottom: 6px; margin-bottom: var(--s4); scrollbar-width: thin; }
    .filters { display: flex; flex-wrap: wrap; align-items: center; gap: var(--s3) var(--s4); padding: var(--s3) var(--s4); background: var(--bb-surface); border: 1px solid var(--bb-border); border-radius: var(--bb-radius); margin-bottom: var(--s5); }
    .filters select.input { width: auto; min-height: 38px; padding: 6px 12px; }
    .sort { display: inline-flex; align-items: center; gap: 8px; }
    .grid-products { display: grid; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); gap: var(--s5); }
    .more { display: flex; justify-content: center; margin-top: var(--s6); }`,
})
export class MenuPage {
  private readonly api = inject(CatalogApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly categories = signal<Category[]>([]);
  protected readonly products = signal<ProductCard[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);
  protected readonly query = signal<ProductQuery>({});
  private page = 1;
  private totalPages = 1;
  protected readonly search$ = new Subject<string>();

  protected readonly hasMore = computed(() => this.products().length < this.total());
  protected readonly hasFilters = computed(() => {
    const q = this.query();
    return !!(q.search || q.category || q.vegetarian || q.maxPrice || q.maxSpice || q.inStockOnly);
  });
  protected readonly heading = computed(() => {
    const cat = this.categories().find(c => c.slug === this.query().category);
    return cat?.name ?? (this.query().vegetarian ? 'Vegetarian dishes' : 'Everything we cook');
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.api.categories().subscribe({ next: c => this.categories.set(c), error: () => undefined });

    this.route.queryParamMap.pipe(takeUntilDestroyed(destroyRef)).subscribe(params => {
      this.query.set({
        search: params.get('search') ?? undefined,
        category: params.get('category') ?? undefined,
        vegetarian: params.get('vegetarian') === 'true' ? true : undefined,
        maxPrice: params.get('maxPrice') ? Number(params.get('maxPrice')) : undefined,
        maxSpice: (params.get('maxSpice') as SpiceLevel) ?? undefined,
        inStockOnly: params.get('inStockOnly') === 'true' ? true : undefined,
        sort: (params.get('sort') as ProductSort) ?? undefined,
      });
      this.fetch(true);
    });

    this.search$.pipe(debounceTime(350), distinctUntilChanged(), takeUntilDestroyed(destroyRef))
      .subscribe(search => this.patch({ search: search.trim() || undefined }));
  }

  protected patch(change: Partial<ProductQuery>): void {
    void this.router.navigate([], { queryParams: { ...this.query(), ...change }, replaceUrl: true });
  }

  protected reset(): void { void this.router.navigate([], { queryParams: {} }); }
  protected check(e: Event): boolean { return (e.target as HTMLInputElement).checked; }
  protected toggleVeg(e: Event): void { this.patch({ vegetarian: this.check(e) || undefined }); }

  protected loadMore(): void {
    if (this.page >= this.totalPages) return;
    this.page++;
    this.fetch(false);
  }

  protected fetch(reset: boolean): void {
    if (reset) { this.page = 1; this.products.set([]); }
    this.loading.set(true);
    this.error.set(false);
    this.api.products({ ...this.query(), page: this.page, pageSize: 12 }).subscribe({
      next: res => {
        this.products.update(list => (reset ? res.items : [...list, ...res.items]));
        this.total.set(res.totalCount);
        this.totalPages = res.totalPages;
        this.loading.set(false);
      },
      error: () => { this.loading.set(false); this.error.set(true); },
    });
  }
}
