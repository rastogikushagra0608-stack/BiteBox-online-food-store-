import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CartApi } from '../api/api';
import { Cart, CartItem, ProductCard } from '../api/models';
import { AuthStore } from './auth.store';
import { ToastService } from './toast';

const GUEST_KEY = 'bitebox.guestCart.v1';
const EMPTY: Cart = { items: [], itemCount: 0, subtotal: 0, hasUnavailableItems: false };

/**
 * Cart state for the whole app.
 *  • Signed in: the server is the source of truth (prices recalculated there).
 *  • Guest: lines live in localStorage so browsing without an account still works;
 *    they are merged into the server cart right after sign-in.
 */
@Injectable({ providedIn: 'root' })
export class CartStore {
  private api = inject(CartApi);
  private auth = inject(AuthStore);
  private toast = inject(ToastService);

  readonly cart = signal<Cart>(EMPTY);
  readonly busy = signal(false);
  readonly drawerOpen = signal(false);
  readonly count = computed(() => this.cart().itemCount);
  readonly subtotal = computed(() => this.cart().subtotal);

  constructor() {
    // React to sign-in / sign-out.
    effect(() => {
      const user = this.auth.user();
      if (user === undefined) return;
      untracked(() => (user ? void this.onSignedIn() : this.cart.set(this.readGuest())));
    });
  }

  quantityOf(productId: number): number {
    return this.cart().items.find(i => i.productId === productId)?.quantity ?? 0;
  }

  async add(product: ProductCard, quantity = 1): Promise<boolean> {
    return this.run(async () => {
      if (this.auth.isLoggedIn()) {
        this.cart.set(await firstValueFrom(this.api.add(product.id, quantity)));
      } else {
        const items = [...this.cart().items];
        const existing = items.find(i => i.productId === product.id);
        const next = Math.min((existing?.quantity ?? 0) + quantity, 20);
        if (existing) Object.assign(existing, { quantity: next, lineTotal: next * existing.unitPrice });
        else items.push(this.guestLine(product, quantity));
        this.saveGuest(items);
      }
      this.toast.success(`${product.name} added to your cart`);
    });
  }

  async setQuantity(productId: number, quantity: number): Promise<boolean> {
    return this.run(async () => {
      if (this.auth.isLoggedIn()) {
        this.cart.set(await firstValueFrom(this.api.setQuantity(productId, quantity)));
      } else {
        const items = this.cart().items
          .map(i => (i.productId === productId ? { ...i, quantity, lineTotal: quantity * i.unitPrice } : i))
          .filter(i => i.quantity > 0);
        this.saveGuest(items);
      }
    });
  }

  remove(productId: number): Promise<boolean> { return this.setQuantity(productId, 0); }

  async clear(): Promise<void> {
    if (this.auth.isLoggedIn()) await firstValueFrom(this.api.clear()).catch(() => undefined);
    this.saveGuest([]);
    this.cart.set(EMPTY);
  }

  /** Reload from server (e.g. after placing an order or re-ordering). */
  async reload(): Promise<void> {
    if (!this.auth.isLoggedIn()) return;
    this.cart.set(await firstValueFrom(this.api.get()));
  }

  private async onSignedIn(): Promise<void> {
    const guest = this.readGuest().items;
    try {
      const cart = guest.length
        ? await firstValueFrom(this.api.merge(guest.map(i => ({ productId: i.productId, quantity: i.quantity }))))
        : await firstValueFrom(this.api.get());
      localStorage.removeItem(GUEST_KEY);
      this.cart.set(cart);
    } catch {
      this.cart.set(EMPTY);
    }
  }

  private async run(work: () => Promise<void>): Promise<boolean> {
    this.busy.set(true);
    try {
      await work();
      return true;
    } catch (e) {
      this.toast.error(e);
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  private guestLine(p: ProductCard, quantity: number): CartItem {
    return {
      productId: p.id, name: p.name, slug: p.slug, imageUrl: p.imageUrl, unitPrice: p.price, quantity,
      lineTotal: p.price * quantity, isVegetarian: p.isVegetarian, inStock: p.inStock, availableQuantity: p.onlyLeft ?? 20,
    };
  }

  private readGuest(): Cart {
    try {
      const items = JSON.parse(localStorage.getItem(GUEST_KEY) ?? '[]') as CartItem[];
      return this.summarise(Array.isArray(items) ? items : []);
    } catch {
      return EMPTY;
    }
  }

  private saveGuest(items: CartItem[]): void {
    localStorage.setItem(GUEST_KEY, JSON.stringify(items));
    this.cart.set(this.summarise(items));
  }

  private summarise(items: CartItem[]): Cart {
    return {
      items,
      itemCount: items.reduce((n, i) => n + i.quantity, 0),
      subtotal: items.reduce((s, i) => s + i.lineTotal, 0),
      hasUnavailableItems: items.some(i => !i.inStock),
    };
  }
}
