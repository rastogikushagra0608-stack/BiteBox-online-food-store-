import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../../core/state/auth.store';
import { CartStore } from '../../core/state/cart.store';
import { Icon } from '../../shared/icon';
import { EmptyState, InrPipe, SmartImage, Stepper, VegMark } from '../../shared/ui';

const FREE_DELIVERY = 499;

@Component({
  selector: 'bb-cart-page',
  imports: [RouterLink, Icon, EmptyState, InrPipe, SmartImage, Stepper, VegMark],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container page">
      <div class="page-title"><h1>Your cart</h1>
        @if (cart.cart().items.length) { <button class="btn btn-ghost btn-sm" (click)="cart.clear()"><bb-icon name="trash" [size]="16" /> Clear cart</button> }
      </div>
      @if (!cart.cart().items.length) {
        <bb-empty icon="bag" title="Your cart is empty" text="Good food is a few taps away." actionLabel="Browse the menu" actionLink="/menu" />
      } @else {
        <div class="layout">
          <div class="card">
            @for (item of cart.cart().items; track item.productId) {
              <div class="line">
                <a [routerLink]="['/menu', item.slug]"><bb-img class="img" [src]="item.imageUrl" [alt]="item.name" /></a>
                <div class="info">
                  <div class="row"><bb-veg [veg]="item.isVegetarian" /><a class="name" [routerLink]="['/menu', item.slug]">{{ item.name }}</a></div>
                  <span class="muted small">{{ item.unitPrice | inr }} each</span>
                  @if (!item.inStock) { <span class="badge badge-danger">Not enough stock: {{ item.availableQuantity }} left</span> }
                </div>
                <bb-stepper [value]="item.quantity" [disabled]="cart.busy()" (changed)="cart.setQuantity(item.productId, $event)" />
                <strong class="total">{{ item.lineTotal | inr }}</strong>
                <button class="icon-btn" (click)="cart.remove(item.productId)" [attr.aria-label]="'Remove ' + item.name"><bb-icon name="trash" [size]="18" /></button>
              </div>
            }
          </div>
          <aside class="card card-pad summary">
            <h3>Summary</h3>
            <div class="row"><span>Items ({{ cart.count() }})</span><span class="spacer"></span><strong>{{ cart.subtotal() | inr }}</strong></div>
            @if (toFree() > 0) {
              <div class="progress"><div [style.width.%]="progress()"></div></div>
              <p class="small muted">Add {{ toFree() | inr }} more for <strong>free delivery</strong>.</p>
            } @else { <p class="small" style="color:var(--bb-success)"><bb-icon name="check" [size]="14" /> You've unlocked free delivery</p> }
            <p class="small muted">Taxes, delivery and coupons are applied at checkout.</p>
            <button class="btn btn-primary btn-block" (click)="checkout()" [disabled]="cart.cart().hasUnavailableItems">
              {{ auth.isLoggedIn() ? 'Proceed to checkout' : 'Sign in to checkout' }}
            </button>
            @if (cart.cart().hasUnavailableItems) { <p class="field-error">Please adjust items that are out of stock.</p> }
            <a routerLink="/menu" class="btn btn-ghost btn-block">Add more items</a>
          </aside>
        </div>
      }
    </div>`,
  styles: `
    .layout { display: grid; grid-template-columns: 1fr 340px; gap: var(--s5); align-items: start; }
    .line { display: grid; grid-template-columns: 72px 1fr auto 80px 40px; gap: var(--s4); align-items: center; padding: var(--s4) var(--s5); border-bottom: 1px solid var(--bb-border); }
    .line:last-child { border-bottom: 0; }
    .img { width: 72px; height: 72px; border-radius: 14px; }
    .info { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
    .name { font-weight: 700; }
    .total { text-align: right; }
    .summary { position: sticky; top: calc(var(--bb-header-h) + 16px); display: flex; flex-direction: column; gap: var(--s3); }
    .progress { height: 8px; border-radius: 999px; background: var(--bb-surface-2); overflow: hidden; }
    .progress div { height: 100%; background: var(--bb-primary); border-radius: inherit; }
    @media (max-width: 860px) {
      .layout { grid-template-columns: 1fr; }
      .line { grid-template-columns: 60px 1fr auto; padding: var(--s3) var(--s4); }
      .line .total { grid-column: 2; text-align: left; }
      .img { width: 60px; height: 60px; }
    }`,
})
export class CartPage {
  protected readonly cart = inject(CartStore);
  protected readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  protected readonly toFree = computed(() => Math.max(0, FREE_DELIVERY - this.cart.subtotal()));
  protected readonly progress = computed(() => Math.min(100, (this.cart.subtotal() / FREE_DELIVERY) * 100));

  protected checkout(): void {
    void this.router.navigateByUrl(this.auth.isLoggedIn() ? '/checkout' : '/login?returnUrl=%2Fcheckout');
  }
}
