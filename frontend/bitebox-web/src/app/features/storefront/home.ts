import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, map, of, switchMap } from 'rxjs';
import { CatalogApi } from '../../core/api/api';
import { Category, ProductCard, Promo, Review } from '../../core/api/models';
import { Icon } from '../../shared/icon';
import { ProductCardComponent } from '../../shared/product-card';
import { ErrorState, InrPipe, SmartImage } from '../../shared/ui';

const HERO = 'https://images.unsplash.com/photo-1504754524776-8f4f37790ca0?auto=format&fit=crop&w=1400&q=75';
const HERO_SIDE = 'https://images.unsplash.com/photo-1589302168068-964664d93dc0?auto=format&fit=crop&w=700&q=75';

@Component({
  selector: 'bb-home',
  imports: [RouterLink, Icon, ProductCardComponent, InrPipe, SmartImage, ErrorState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="hero">
      <div class="container hero-grid">
        <div class="hero-copy fade-up">
          <span class="eyebrow">Freshly cooked · Delivered hot</span>
          <h1>Every Bite, <em>Anytime.</em></h1>
          <p class="lead">Biryanis sealed on dum, dosas off the tawa, stone-baked pizzas and protein bowls, cooked to order in our Bengaluru kitchen.</p>
          <div class="cta">
            <a class="btn btn-primary" routerLink="/menu">Order now <bb-icon name="arrowRight" [size]="18" /></a>
            <a class="btn" routerLink="/menu" [queryParams]="{ vegetarian: true }"><span class="veg-dot"></span> Pure veg menu</a>
          </div>
          <ul class="trust">
            <li><bb-icon name="clock" [size]="18" /> ~30 min delivery</li>
            <li><bb-icon name="bike" [size]="18" /> Free above ₹499</li>
            <li><bb-icon name="shield" [size]="18" /> Hygiene-first kitchen</li>
          </ul>
        </div>
        <div class="hero-art" aria-hidden="true">
          <img class="main" [src]="hero" alt="" />
          <img class="side" [src]="heroSide" alt="" />
          <div class="float-card">
            <bb-icon name="sparkles" [size]="18" />
            <div><strong>Not sure what to eat?</strong><span class="small muted">Ask BiteBot, bottom right</span></div>
          </div>
        </div>
      </div>
    </section>

    @if (promos().length) {
      <section class="container promos">
        @for (p of promos(); track p.code) {
          <div class="promo">
            <bb-icon name="tag" [size]="20" />
            <div><strong>{{ p.description }}</strong><span class="small">Use code <code>{{ p.code }}</code>@if (p.minOrderAmount) { · min order {{ p.minOrderAmount | inr }} }</span></div>
          </div>
        }
      </section>
    }

    <section class="container section">
      <div class="section-head">
        <div><span class="eyebrow">Explore</span><h2>What are you craving?</h2></div>
        <a routerLink="/menu" class="link">See full menu <bb-icon name="chevronRight" [size]="16" /></a>
      </div>
      <div class="cats">
        @for (c of categories(); track c.id) {
          <a class="cat" [routerLink]="['/menu']" [queryParams]="{ category: c.slug }">
            <bb-img class="cat-img" [src]="c.imageUrl" [alt]="c.name" />
            <span>{{ c.name }}</span>
            <small class="muted">{{ c.productCount }} dishes</small>
          </a>
        } @empty {
          @for (i of skeleton; track i) { <div class="cat"><div class="skeleton cat-img"></div><div class="skeleton" style="height:14px;width:70%"></div></div> }
        }
      </div>
    </section>

    <section class="container section">
      <div class="section-head">
        <div><span class="eyebrow">Bestsellers</span><h2>Most-loved this week</h2></div>
        <a routerLink="/menu" [queryParams]="{ sort: 'Rating' }" class="link">Top rated <bb-icon name="chevronRight" [size]="16" /></a>
      </div>
      @if (error()) {
        <bb-error message="We couldn't load the menu right now." (retry)="load()" />
      } @else {
        <div class="products">
          @for (p of featured(); track p.id) { <bb-product-card [product]="p" /> }
          @empty { @for (i of skeleton; track i) { <div class="skeleton" style="height:360px;border-radius:22px"></div> } }
        </div>
      }
    </section>

    <section class="container section how">
      <div class="how-card"><span class="num">1</span><h3>Pick your favourites</h3><p class="muted">Filter by veg, spice level or budget, or just ask BiteBot.</p></div>
      <div class="how-card"><span class="num">2</span><h3>Pay your way</h3><p class="muted">Cash on delivery or online payment. Apply offers at checkout.</p></div>
      <div class="how-card"><span class="num">3</span><h3>Track it live</h3><p class="muted">Follow your order from kitchen to doorstep with real-time status updates.</p></div>
    </section>

    @if (reviews().length) {
      <section class="container section">
        <div class="section-head"><div><span class="eyebrow">Reviews</span><h2>From our regulars</h2></div></div>
        <div class="reviews">
          @for (r of reviews(); track r.id) {
            <figure class="review card card-pad">
              <div class="stars">@for (s of [1,2,3,4,5]; track s) {<bb-icon name="star" [size]="16" [filled]="s <= r.rating" />}</div>
              <blockquote>"{{ r.comment }}"</blockquote>
              <figcaption><strong>{{ r.authorName }}</strong> <span class="muted small">on {{ r.dish }}</span></figcaption>
            </figure>
          }
        </div>
      </section>
    }

    <section class="container section">
      <div class="bitebot-band">
        <div>
          <span class="eyebrow light">Meet BiteBot</span>
          <h2>"Spicy veg dinner for two under ₹600?"</h2>
          <p>BiteBot reads requests like that and suggests real dishes from today's menu, priced and in stock, even a combo that fits your budget.</p>
        </div>
        <bb-icon name="sparkles" [size]="88" [stroke]="1.2" />
      </div>
    </section>`,
  styles: `
    .hero { padding: var(--s7) 0 var(--s6); background: radial-gradient(1200px 500px at 85% 0%, #ffe3d4 0%, transparent 60%); }
    .hero-grid { display: grid; grid-template-columns: 1.05fr 1fr; gap: var(--s7); align-items: center; }
    h1 em { font-style: italic; color: var(--bb-primary); }
    .lead { font-size: 1.1rem; color: var(--bb-ink-2); max-width: 520px; margin-bottom: var(--s5); }
    .cta { display: flex; gap: var(--s3); flex-wrap: wrap; }
    .veg-dot { width: 12px; height: 12px; border: 1.6px solid var(--bb-veg); border-radius: 2px; position: relative; }
    .veg-dot::after { content: ''; position: absolute; inset: 2px; border-radius: 50%; background: var(--bb-veg); }
    .trust { list-style: none; padding: 0; margin: var(--s5) 0 0; display: flex; gap: var(--s5); flex-wrap: wrap; color: var(--bb-ink-2); font-weight: 600; font-size: .9rem; }
    .trust li { display: flex; align-items: center; gap: 6px; }
    .trust bb-icon { color: var(--bb-primary); }
    .hero-art { position: relative; height: 460px; }
    .hero-art img { position: absolute; object-fit: cover; box-shadow: var(--bb-shadow-lg); }
    .hero-art .main { inset: 0 70px 40px 0; width: calc(100% - 70px); height: calc(100% - 40px); border-radius: 28px; }
    .hero-art .side { right: 0; bottom: 0; width: 46%; height: 52%; border-radius: 22px; border: 6px solid var(--bb-bg); }
    .float-card { position: absolute; left: -18px; bottom: 70px; display: flex; gap: 10px; align-items: center; background: var(--bb-surface); padding: 12px 16px; border-radius: 16px; box-shadow: var(--bb-shadow-lg); }
    .float-card div { display: flex; flex-direction: column; line-height: 1.3; }
    .float-card bb-icon { color: var(--bb-primary); }
    .promos { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: var(--s3); margin-top: var(--s2); }
    .promo { display: flex; gap: 12px; align-items: center; padding: 14px 18px; border-radius: var(--bb-radius); background: var(--bb-primary-soft); color: var(--bb-primary-ink); border: 1px dashed color-mix(in srgb, var(--bb-primary) 45%, white); }
    .promo div { display: flex; flex-direction: column; }
    code { font-weight: 800; background: #fff; padding: 1px 6px; border-radius: 6px; }
    .section { margin-top: var(--s8); }
    .section-head { display: flex; align-items: flex-end; justify-content: space-between; gap: var(--s4); margin-bottom: var(--s5); }
    .section-head h2 { margin: 4px 0 0; }
    .link { display: inline-flex; align-items: center; gap: 4px; font-weight: 700; color: var(--bb-primary-ink); white-space: nowrap; }
    .cats { display: grid; grid-template-columns: repeat(auto-fill, minmax(124px, 1fr)); gap: var(--s4); }
    .cat { display: flex; flex-direction: column; align-items: center; gap: 6px; text-align: center; font-weight: 650; font-size: .9rem; }
    .cat-img { width: 108px; height: 108px; border-radius: 50%; box-shadow: var(--bb-shadow); transition: transform .25s; }
    .cat:hover .cat-img { transform: translateY(-4px) rotate(-3deg); }
    .products { display: grid; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); gap: var(--s5); }
    .how { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--s5); }
    .how-card { padding: var(--s5); border-radius: var(--bb-radius-lg); background: var(--bb-surface); border: 1px solid var(--bb-border); }
    .num { display: grid; place-items: center; width: 38px; height: 38px; border-radius: 50%; background: var(--bb-ink); color: #fff; font-weight: 800; margin-bottom: var(--s3); }
    .reviews { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: var(--s5); }
    .review { margin: 0; }
    .stars { color: var(--bb-star); display: flex; gap: 2px; margin-bottom: var(--s2); }
    blockquote { margin: 0 0 var(--s3); font-family: var(--bb-font-display); font-size: 1.15rem; line-height: 1.4; }
    .bitebot-band { display: flex; align-items: center; justify-content: space-between; gap: var(--s6); padding: var(--s7); border-radius: 28px; background: var(--bb-ink); color: #eadfd4; }
    .bitebot-band h2 { color: #fff; max-width: 640px; }
    .bitebot-band p { max-width: 600px; margin: 0; }
    .bitebot-band bb-icon { color: var(--bb-primary); }
    .eyebrow.light { color: #ffb38f; }
    @media (max-width: 900px) {
      .hero-grid { grid-template-columns: 1fr; gap: var(--s5); }
      .hero-art { height: 300px; }
      .float-card { left: 8px; bottom: 50px; }
      .how { grid-template-columns: 1fr; }
      .bitebot-band { padding: var(--s5); }
      .bitebot-band bb-icon { display: none; }
    }`,
})
export class Home {
  private readonly api = inject(CatalogApi);
  protected readonly hero = HERO;
  protected readonly heroSide = HERO_SIDE;
  protected readonly skeleton = [1, 2, 3, 4, 5, 6, 7, 8];

  protected readonly categories = signal<Category[]>([]);
  protected readonly featured = signal<ProductCard[]>([]);
  protected readonly promos = signal<Promo[]>([]);
  protected readonly reviews = signal<(Review & { dish: string })[]>([]);
  protected readonly error = signal(false);

  constructor() { this.load(); }

  protected load(): void {
    this.error.set(false);
    this.api.categories().subscribe({ next: c => this.categories.set(c), error: () => this.error.set(true) });
    this.api.promotions().subscribe({ next: p => this.promos.set(p), error: () => undefined });
    this.api.products({ featuredOnly: true, sort: 'Popular', pageSize: 8 }).pipe(
      switchMap(page => {
        this.featured.set(page.items);
        // Real reviews (with comments) from a few top dishes.
        const top = page.items.slice(0, 4);
        return forkJoin(top.map(p => this.api.reviews(p.id).pipe(
          map(r => r.items.filter(x => x.comment && x.rating >= 4).slice(0, 1).map(x => ({ ...x, dish: p.name }))),
          catchError(() => of([])))));
      }),
    ).subscribe({ next: lists => this.reviews.set(lists.flat().slice(0, 3)), error: () => this.error.set(true) });
  }
}
