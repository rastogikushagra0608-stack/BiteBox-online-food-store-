import { Injectable, effect, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AccountApi } from '../api/api';
import { AuthStore } from './auth.store';
import { ToastService } from './toast';

/** Set of favourite product ids for heart icons across the app. */
@Injectable({ providedIn: 'root' })
export class FavoritesStore {
  private api = inject(AccountApi);
  private auth = inject(AuthStore);
  private router = inject(Router);
  private toast = inject(ToastService);

  readonly ids = signal<ReadonlySet<number>>(new Set());

  constructor() {
    effect(() => {
      const signedIn = this.auth.isLoggedIn();
      untracked(() => {
        if (!signedIn) { this.ids.set(new Set()); return; }
        this.api.favoriteIds().subscribe({ next: ids => this.ids.set(new Set(ids)), error: () => undefined });
      });
    });
  }

  has(productId: number): boolean { return this.ids().has(productId); }

  async toggle(productId: number): Promise<void> {
    if (!this.auth.isLoggedIn()) {
      this.toast.info('Sign in to save your favourite dishes.');
      await this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
      return;
    }
    const adding = !this.has(productId);
    this.ids.update(set => { const next = new Set(set); if (adding) { next.add(productId); } else { next.delete(productId); } return next; });
    try {
      await firstValueFrom(adding ? this.api.addFavorite(productId) : this.api.removeFavorite(productId));
    } catch (e) {
      this.ids.update(set => { const next = new Set(set); if (adding) { next.delete(productId); } else { next.add(productId); } return next; });
      this.toast.error(e);
    }
  }
}
