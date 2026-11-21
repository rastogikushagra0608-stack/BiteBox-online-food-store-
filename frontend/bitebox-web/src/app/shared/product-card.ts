import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProductCard as Product } from '../core/api/models';
import { CartStore } from '../core/state/cart.store';
import { FavoritesStore } from '../core/state/favorites.store';
import { Icon } from './icon';
import { InrPipe, Rating, SmartImage, SpiceMeter, Stepper, VegMark } from './ui';

@Component({
  selector: 'bb-product-card',
  imports: [RouterLink, Icon, InrPipe, Rating, SmartImage, SpiceMeter, Stepper, VegMark],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let p = product();
    <article class="pc" [class.soldout]="!p.inStock">
      <a class="media" [routerLink]="['/menu', p.slug]" [attr.aria-label]="p.name">
        <bb-img [src]="p.imageUrl" [alt]="p.name" />
        @if (!p.inStock) { <span class="ribbon dark">Sold out</span> }
        @else if (p.onlyLeft) { <span class="ribbon">Only {{ p.onlyLeft }} left</span> }
        @else if (p.isFeatured) { <span class="ribbon">Bestseller</span> }
      </a>
      <button class="fav" type="button" (click)="favorites.toggle(p.id)" [class.on]="favorites.has(p.id)"
              [attr.aria-pressed]="favorites.has(p.id)" [attr.aria-label]="favorites.has(p.id) ? 'Remove from favourites' : 'Add to favourites'">
        <bb-icon name="heart" [size]="18" [filled]="favorites.has(p.id)" />
      </button>
      <div class="body">
        <div class="meta">
          <bb-veg [veg]="p.isVegetarian" />
          <bb-spice [level]="p.spiceLevel" />
          <span class="spacer"></span>
          <bb-rating [value]="p.averageRating" [count]="p.reviewCount" [showCount]="false" />
        </div>
        <a class="name" [routerLink]="['/menu', p.slug]">{{ p.name }}</a>
        <p class="desc">{{ p.description }}</p>
        <div class="foot">
          <div>
            <div class="price">{{ p.price | inr }}</div>
            <div class="muted small"><bb-icon name="clock" [size]="13" /> {{ p.prepTimeMinutes }} min@if (p.servesCount > 1) { · serves {{ p.servesCount }} }</div>
          </div>
          @if (qty() > 0) {
            <bb-stepper [value]="qty()" [small]="true" [disabled]="cart.busy()" (changed)="cart.setQuantity(p.id, $event)" />
          } @else {
            <button class="btn btn-sm add" type="button" [disabled]="!p.inStock || cart.busy()" (click)="cart.add(p)">
              <bb-icon name="plus" [size]="16" /> Add
            </button>
          }
        </div>
      </div>
    </article>`,
  styles: `
    .pc { position: relative; display: flex; flex-direction: column; height: 100%; background: var(--bb-surface); border: 1px solid var(--bb-border); border-radius: var(--bb-radius-lg); overflow: hidden; transition: box-shadow .2s, transform .2s; }
    .pc:hover { box-shadow: var(--bb-shadow); transform: translateY(-2px); }
    .media { position: relative; display: block; aspect-ratio: 4 / 3; }
    .media bb-img { height: 100%; transition: transform .4s ease; }
    .pc:hover .media bb-img { transform: scale(1.04); }
    .soldout .media bb-img { filter: grayscale(.7); opacity: .75; }
    .ribbon { position: absolute; left: 12px; top: 12px; background: var(--bb-surface); color: var(--bb-primary-ink); font-size: .74rem; font-weight: 700; padding: 4px 10px; border-radius: 999px; box-shadow: var(--bb-shadow-sm); }
    .ribbon.dark { background: var(--bb-ink); color: #fff; }
    .fav { position: absolute; right: 10px; top: 10px; width: 36px; height: 36px; border-radius: 50%; border: 0; display: grid; place-items: center; background: rgba(255,255,255,.92); color: var(--bb-ink-2); cursor: pointer; box-shadow: var(--bb-shadow-sm); }
    .fav.on { color: var(--bb-primary); }
    .body { padding: 14px 16px 16px; display: flex; flex-direction: column; gap: 6px; flex: 1; }
    .meta { display: flex; align-items: center; gap: 8px; }
    .name { font-weight: 700; font-size: 1.02rem; line-height: 1.3; }
    .name:hover { color: var(--bb-primary-ink); }
    .desc { color: var(--bb-muted); font-size: .86rem; margin: 0; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
    .foot { margin-top: auto; padding-top: 8px; display: flex; align-items: flex-end; justify-content: space-between; gap: 8px; }
    .price { font-weight: 800; font-size: 1.1rem; }
    .small bb-icon { vertical-align: -2px; }
    .add { border-color: var(--bb-primary); color: var(--bb-primary-ink); }
    .add:hover:not(:disabled) { background: var(--bb-primary); color: #fff; }`,
})
export class ProductCardComponent {
  readonly product = input.required<Product>();
  protected readonly cart = inject(CartStore);
  protected readonly favorites = inject(FavoritesStore);
  protected readonly qty = computed(() => this.cart.quantityOf(this.product().id));
}
