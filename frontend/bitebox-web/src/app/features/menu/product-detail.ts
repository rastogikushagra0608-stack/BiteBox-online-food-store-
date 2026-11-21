import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { CatalogApi } from '../../core/api/api';
import { ProductCard, ProductDetail as Detail, ReviewSummary } from '../../core/api/models';
import { AuthStore } from '../../core/state/auth.store';
import { CartStore } from '../../core/state/cart.store';
import { FavoritesStore } from '../../core/state/favorites.store';
import { ToastService } from '../../core/state/toast';
import { Icon } from '../../shared/icon';
import { ProductCardComponent } from '../../shared/product-card';
import { EmptyState, InrPipe, Rating, SmartImage, SpiceMeter, Stepper, VegMark, utc } from '../../shared/ui';

@Component({
  selector: 'bb-product-detail',
  imports: [FormsModule, RouterLink, DatePipe, Icon, ProductCardComponent, EmptyState, InrPipe, Rating, SmartImage, SpiceMeter, Stepper, VegMark],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container page">
      @if (notFound()) {
        <bb-empty icon="search" title="We couldn't find that dish" text="It may have been removed from the menu." actionLabel="Back to menu" actionLink="/menu" />
      } @else if (product(); as p) {
        <nav class="crumbs small muted"><a routerLink="/menu">Menu</a> / <a [routerLink]="['/menu']" [queryParams]="{ category: p.categorySlug }">{{ p.categoryName }}</a> / <span>{{ p.name }}</span></nav>
        <div class="detail">
          <div class="media"><bb-img [src]="p.imageUrl" [alt]="p.name" [eager]="true" /></div>
          <div class="info">
            <div class="row tags">
              <bb-veg [veg]="p.isVegetarian" /><span class="small muted">{{ p.isVegetarian ? 'Vegetarian' : 'Non-vegetarian' }}</span>
              <bb-spice [level]="p.spiceLevel" />
              @if (p.isFeatured) { <span class="badge badge-brand">Bestseller</span> }
            </div>
            <h1>{{ p.name }}</h1>
            <div class="row"><bb-rating [value]="p.averageRating" [count]="p.reviewCount" /><span class="muted small">· {{ p.categoryName }}</span></div>
            <p class="desc">{{ p.description }}</p>
            <div class="facts">
              <div><bb-icon name="clock" [size]="18" /><span><strong>{{ p.prepTimeMinutes }} min</strong><small>prep time</small></span></div>
              <div><bb-icon name="users" [size]="18" /><span><strong>Serves {{ p.servesCount }}</strong><small>portion</small></span></div>
              @if (p.calories) { <div><bb-icon name="flame" [size]="18" /><span><strong>{{ p.calories }} kcal</strong><small>approx.</small></span></div> }
            </div>
            <div class="price-row">
              <span class="price">{{ p.price | inr }}</span>
              @if (!p.inStock) { <span class="badge badge-danger">Sold out for today</span> }
              @else if (p.onlyLeft) { <span class="badge badge-warning">Only {{ p.onlyLeft }} left</span> }
              @else { <span class="badge badge-success">Available</span> }
            </div>
            <div class="buy">
              <bb-stepper [value]="qty()" [min]="1" [max]="maxQty()" (changed)="qty.set($event)" [disabled]="!p.inStock" />
              <button class="btn btn-primary" type="button" [disabled]="!p.inStock || cart.busy()" (click)="add(p)">
                Add {{ qty() }} to cart · {{ p.price * qty() | inr }}
              </button>
              <button class="icon-btn fav" type="button" (click)="favorites.toggle(p.id)" [class.on]="favorites.has(p.id)"
                      [attr.aria-label]="favorites.has(p.id) ? 'Remove from favourites' : 'Save to favourites'">
                <bb-icon name="heart" [filled]="favorites.has(p.id)" />
              </button>
            </div>
            @if (inCart()) { <p class="small muted"><bb-icon name="check" [size]="14" /> {{ inCart() }} already in your cart</p> }
            @if (p.tags.length) { <div class="row wrap">@for (t of p.tags; track t) {<span class="badge">#{{ t }}</span>}</div> }
          </div>
        </div>

        <section class="reviews">
          <h2>Reviews</h2>
          @if (reviews(); as r) {
            @if (r.canReview) {
              <form class="card card-pad review-form" (submit)="$event.preventDefault(); submitReview(p.id)">
                <strong>How was your {{ p.name }}?</strong>
                <div class="stars-input" role="radiogroup" aria-label="Rating">
                  @for (s of [1,2,3,4,5]; track s) {
                    <button type="button" (click)="myRating.set(s)" [class.on]="s <= myRating()" [attr.aria-label]="s + ' stars'" role="radio" [attr.aria-checked]="s === myRating()"><bb-icon name="star" [size]="26" [filled]="s <= myRating()" /></button>
                  }
                </div>
                <textarea class="input" name="comment" [(ngModel)]="myComment" maxlength="500" placeholder="Tell others what you liked (optional)"></textarea>
                <button class="btn btn-dark" type="submit" [disabled]="!myRating() || submitting()">Post review</button>
              </form>
            }
            @for (rv of r.items; track rv.id) {
              <div class="review">
                <div class="row"><strong>{{ rv.authorName }}</strong><span class="stars">@for (s of [1,2,3,4,5]; track s) {<bb-icon name="star" [size]="14" [filled]="s <= rv.rating" />}</span><span class="spacer"></span><span class="muted small">{{ utc(rv.createdAt) | date: 'mediumDate' }}</span></div>
                @if (rv.comment) { <p>{{ rv.comment }}</p> }
              </div>
            } @empty { <p class="muted">No reviews yet. Order it and be the first to share your thoughts.</p> }
          }
        </section>

        @if (related().length) {
          <section class="related">
            <h2>You might also like</h2>
            <div class="grid-products">@for (r of related(); track r.id) { <bb-product-card [product]="r" /> }</div>
          </section>
        }
      } @else {
        <div class="detail"><div class="skeleton" style="aspect-ratio:1"></div><div class="stack"><div class="skeleton" style="height:40px"></div><div class="skeleton" style="height:120px"></div></div></div>
      }
    </div>`,
  styles: `
    .crumbs { margin-bottom: var(--s4); } .crumbs a:hover { color: var(--bb-primary-ink); }
    .detail { display: grid; grid-template-columns: 1fr 1fr; gap: var(--s7); align-items: start; }
    .media bb-img { aspect-ratio: 1; border-radius: 28px; box-shadow: var(--bb-shadow-lg); }
    .info { display: flex; flex-direction: column; gap: var(--s3); }
    .info h1 { margin: 0; font-size: clamp(1.8rem, 3.5vw, 2.6rem); }
    .tags { gap: 8px; }
    .desc { font-size: 1.05rem; color: var(--bb-ink-2); }
    .facts { display: flex; gap: var(--s5); flex-wrap: wrap; padding: var(--s3) 0; border-top: 1px solid var(--bb-border); border-bottom: 1px solid var(--bb-border); }
    .facts div { display: flex; gap: 8px; align-items: center; color: var(--bb-primary); }
    .facts span { display: flex; flex-direction: column; color: var(--bb-ink); line-height: 1.2; }
    .facts small { color: var(--bb-muted); }
    .price-row { display: flex; align-items: center; gap: var(--s3); }
    .price { font-size: 2rem; font-weight: 800; }
    .buy { display: flex; gap: var(--s3); align-items: center; flex-wrap: wrap; }
    .fav { border: 1.5px solid var(--bb-border-strong); width: 44px; height: 44px; }
    .fav.on { color: var(--bb-primary); border-color: var(--bb-primary); }
    .wrap { flex-wrap: wrap; gap: 6px; }
    .reviews, .related { margin-top: var(--s7); }
    .review-form { display: flex; flex-direction: column; gap: var(--s3); margin-bottom: var(--s5); max-width: 640px; }
    .stars-input { display: flex; gap: 4px; }
    .stars-input button { border: 0; background: none; cursor: pointer; color: var(--bb-border-strong); padding: 2px; }
    .stars-input button.on { color: var(--bb-star); }
    .review { padding: var(--s4) 0; border-bottom: 1px solid var(--bb-border); max-width: 760px; }
    .review p { margin: 6px 0 0; }
    .stars { color: var(--bb-star); display: inline-flex; }
    .grid-products { display: grid; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); gap: var(--s5); }
    @media (max-width: 860px) { .detail { grid-template-columns: 1fr; gap: var(--s5); } }`,
})
export class ProductDetailPage {
  private readonly api = inject(CatalogApi);
  private readonly toast = inject(ToastService);
  protected readonly auth = inject(AuthStore);
  protected readonly cart = inject(CartStore);
  protected readonly favorites = inject(FavoritesStore);
  protected readonly utc = utc;

  protected readonly product = signal<Detail | null>(null);
  protected readonly notFound = signal(false);
  protected readonly related = signal<ProductCard[]>([]);
  protected readonly reviews = signal<ReviewSummary | null>(null);
  protected readonly qty = signal(1);
  protected readonly myRating = signal(0);
  protected readonly submitting = signal(false);
  protected myComment = '';

  protected readonly inCart = computed(() => (this.product() ? this.cart.quantityOf(this.product()!.id) : 0));
  protected readonly maxQty = computed(() => Math.min(20, this.product()?.onlyLeft ?? 20));

  constructor() {
    inject(ActivatedRoute).paramMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(params => {
      const slug = params.get('slug')!;
      this.product.set(null);
      this.notFound.set(false);
      this.qty.set(1);
      this.api.product(slug).subscribe({
        next: p => { this.product.set(p); this.loadReviews(p.id); },
        error: (e: HttpErrorResponse) => (e.status === 404 ? this.notFound.set(true) : this.toast.error(e)),
      });
      this.api.related(slug).subscribe({ next: r => this.related.set(r), error: () => undefined });
    });
  }

  protected async add(p: Detail): Promise<void> {
    if (await this.cart.add(p, this.qty())) this.qty.set(1);
  }

  protected submitReview(productId: number): void {
    this.submitting.set(true);
    this.api.addReview(productId, { rating: this.myRating(), comment: this.myComment.trim() || null }).subscribe({
      next: () => { this.toast.success('Thanks for your review!'); this.myComment = ''; this.myRating.set(0); this.submitting.set(false); this.loadReviews(productId); },
      error: e => { this.toast.error(e); this.submitting.set(false); },
    });
  }

  private loadReviews(productId: number): void {
    this.api.reviews(productId).subscribe({ next: r => this.reviews.set(r), error: () => undefined });
  }
}
