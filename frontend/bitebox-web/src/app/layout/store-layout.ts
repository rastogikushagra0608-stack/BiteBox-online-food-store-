import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { catchError, filter, interval, of, startWith, switchMap } from 'rxjs';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { AccountApi } from '../core/api/api';
import { NotificationFeed } from '../core/api/models';
import { AuthStore } from '../core/state/auth.store';
import { CartStore } from '../core/state/cart.store';
import { BiteBotPanel } from '../features/ai-assistant/bitebot-panel';
import { Icon } from '../shared/icon';
import { InrPipe, SmartImage, Stepper, TimeAgoPipe, VegMark } from '../shared/ui';
import { Logo } from './brand';

@Component({
  selector: 'bb-store-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FormsModule, Icon, Logo, InrPipe, SmartImage, Stepper, VegMark, TimeAgoPipe, BiteBotPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-layout.html',
  styleUrl: './store-layout.scss',
})
export class StoreLayout {
  protected readonly auth = inject(AuthStore);
  protected readonly cart = inject(CartStore);
  private readonly router = inject(Router);
  private readonly accountApi = inject(AccountApi);

  protected readonly mobileNav = signal(false);
  protected readonly accountMenu = signal(false);
  protected readonly bellOpen = signal(false);
  protected readonly feed = signal<NotificationFeed | null>(null);
  protected search = '';
  protected readonly year = new Date().getFullYear();

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.router.events.pipe(filter(e => e instanceof NavigationEnd), takeUntilDestroyed(destroyRef)).subscribe(() => {
      this.mobileNav.set(false);
      this.accountMenu.set(false);
      this.bellOpen.set(false);
      this.cart.drawerOpen.set(false);
    });

    // Lightweight polling for order-status notifications (no websockets needed at this scale).
    toObservable(this.auth.user).pipe(
      switchMap(user => user && !this.auth.isStaff()
        ? interval(45_000).pipe(startWith(0), switchMap(() => this.accountApi.notifications().pipe(catchError(() => of(null)))))
        : of(null)),
      takeUntilDestroyed(destroyRef),
    ).subscribe(f => this.feed.set(f));
  }

  protected submitSearch(): void {
    const q = this.search.trim();
    void this.router.navigate(['/menu'], { queryParams: q ? { search: q } : {} });
    this.mobileNav.set(false);
  }

  protected toggleBell(): void {
    this.bellOpen.update(v => !v);
    this.accountMenu.set(false);
    const f = this.feed();
    if (this.bellOpen() && f && f.unreadCount > 0) {
      this.accountApi.markAllRead().subscribe(() => this.feed.set({ ...f, unreadCount: 0 }));
    }
  }

  protected checkout(): void {
    this.cart.drawerOpen.set(false);
    void this.router.navigateByUrl(this.auth.isLoggedIn() ? '/checkout' : '/login?returnUrl=%2Fcheckout');
  }
}
