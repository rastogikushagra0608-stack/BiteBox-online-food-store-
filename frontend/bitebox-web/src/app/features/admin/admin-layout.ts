import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { Role } from '../../core/api/models';
import { AuthStore } from '../../core/state/auth.store';
import { Logo } from '../../layout/brand';
import { Icon } from '../../shared/icon';

interface NavItem { path: string; label: string; icon: string; roles: Role[]; exact?: boolean; }

const NAV: NavItem[] = [
  { path: '/admin', label: 'Dashboard', icon: 'dashboard', roles: ['Admin'], exact: true },
  { path: '/admin/orders', label: 'Orders', icon: 'receipt', roles: ['Admin', 'Kitchen', 'Delivery'] },
  { path: '/admin/products', label: 'Menu items', icon: 'chef', roles: ['Admin'] },
  { path: '/admin/categories', label: 'Categories', icon: 'layers', roles: ['Admin'] },
  { path: '/admin/inventory', label: 'Inventory', icon: 'box', roles: ['Admin'] },
  { path: '/admin/coupons', label: 'Promotions', icon: 'percent', roles: ['Admin'] },
  { path: '/admin/users', label: 'Customers & staff', icon: 'users', roles: ['Admin'] },
  { path: '/admin/reports', label: 'Reports', icon: 'chart', roles: ['Admin'] },
];

/** BiteBox Admin shell. The sidebar shows only what the signed-in role can use; the API enforces it. */
@Component({
  selector: 'bb-admin-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon, Logo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="shell">
      <aside class="side" [class.open]="navOpen()">
        <a routerLink="/admin" class="brand"><bb-logo [light]="true" suffix="Admin" /></a>
        <nav aria-label="Admin">
          @for (item of nav(); track item.path) {
            <a [routerLink]="item.path" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: !!item.exact }">
              <bb-icon [name]="item.icon" [size]="19" /> {{ item.label }}
            </a>
          }
        </nav>
        <div class="side-foot">
          <a routerLink="/" class="store-link"><bb-icon name="external" [size]="16" /> View storefront</a>
          <div class="me"><span class="avatar">{{ auth.user()?.fullName?.charAt(0) }}</span>
            <div><strong>{{ auth.user()?.fullName }}</strong><small>{{ roleLabel() }}</small></div></div>
          <button class="logout" type="button" (click)="auth.logout()"><bb-icon name="logout" [size]="16" /> Sign out</button>
        </div>
      </aside>
      @if (navOpen()) { <div class="scrim" (click)="navOpen.set(false)"></div> }
      <div class="main">
        <header class="top">
          <button class="icon-btn mobile" type="button" (click)="navOpen.set(true)" aria-label="Open navigation"><bb-icon name="menu" /></button>
          <span class="muted small">{{ today }}</span>
        </header>
        <main class="content"><router-outlet /></main>
      </div>
    </div>`,
  styles: `
    .shell { display: grid; grid-template-columns: 252px 1fr; min-height: 100vh; background: #f7f3ee; }
    .side { background: var(--bb-ink); color: #cfc4b8; display: flex; flex-direction: column; padding: var(--s5) var(--s4); position: sticky; top: 0; height: 100vh; }
    .brand { padding: 0 8px var(--s5); }
    nav { display: flex; flex-direction: column; gap: 2px; }
    nav a { display: flex; align-items: center; gap: 12px; padding: 10px 12px; border-radius: 10px; font-weight: 550; font-size: .92rem; }
    nav a:hover { background: rgba(255,255,255,.06); color: #fff; }
    nav a.active { background: rgba(232, 87, 42, .18); color: #fff; }
    nav a.active bb-icon { color: #ff9a74; }
    .side-foot { margin-top: auto; display: flex; flex-direction: column; gap: var(--s3); border-top: 1px solid rgba(255,255,255,.08); padding-top: var(--s4); }
    .store-link { display: flex; gap: 8px; align-items: center; font-size: .86rem; padding: 0 12px; }
    .store-link:hover { color: #fff; }
    .me { display: flex; gap: 10px; align-items: center; padding: 0 8px; }
    .me div { display: flex; flex-direction: column; line-height: 1.25; min-width: 0; }
    .me strong { color: #fff; font-size: .88rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .me small { color: #a3978b; }
    .avatar { width: 34px; height: 34px; border-radius: 50%; background: var(--bb-primary); color: #fff; display: grid; place-items: center; font-weight: 800; flex-shrink: 0; }
    .logout { display: flex; align-items: center; gap: 8px; background: rgba(255,255,255,.06); border: 0; color: #e6dbcf; border-radius: 10px; padding: 9px 12px; cursor: pointer; font-weight: 600; }
    .logout:hover { background: rgba(255,255,255,.12); }
    .main { min-width: 0; display: flex; flex-direction: column; }
    .top { height: 56px; display: flex; align-items: center; gap: var(--s3); justify-content: flex-end; padding: 0 var(--s6); border-bottom: 1px solid var(--bb-border); background: rgba(255,255,255,.6); }
    .content { padding: var(--s5) var(--s6) var(--s7); }
    .mobile { display: none; margin-right: auto; }
    .scrim { display: none; }
    @media (max-width: 980px) {
      .shell { grid-template-columns: 1fr; }
      .side { position: fixed; z-index: 100; left: 0; width: 260px; transform: translateX(-100%); transition: transform .25s; }
      .side.open { transform: none; }
      .scrim { display: block; position: fixed; inset: 0; background: rgba(0,0,0,.4); z-index: 90; }
      .mobile { display: inline-grid; }
      .content { padding: var(--s4); }
      .top { padding: 0 var(--s4); }
    }`,
})
export class AdminLayout {
  protected readonly auth = inject(AuthStore);
  protected readonly navOpen = signal(false);
  protected readonly today = new Date().toLocaleDateString('en-IN', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  protected readonly nav = computed(() => NAV.filter(n => n.roles.some(r => this.auth.hasRole(r))));
  protected readonly roleLabel = computed(() => (this.auth.isAdmin() ? 'Administrator' : this.auth.hasRole('Kitchen') ? 'Kitchen staff' : 'Delivery partner'));

  constructor() {
    inject(Router).events.pipe(filter(e => e instanceof NavigationEnd), takeUntilDestroyed()).subscribe(() => this.navOpen.set(false));
  }
}

/** Shared admin page styles (applied via component styles arrays). */
export const ADMIN_PAGE = `
  .head { display: flex; align-items: flex-end; justify-content: space-between; gap: var(--s4); flex-wrap: wrap; margin-bottom: var(--s5); }
  .head h1 { font-size: 1.8rem; margin: 0; }
  .toolbar { display: flex; gap: var(--s3); flex-wrap: wrap; align-items: center; margin-bottom: var(--s4); }
  .toolbar .input { width: auto; min-height: 40px; }
  .toolbar .grow { flex: 1; min-width: 200px; }
  .pager { display: flex; align-items: center; justify-content: flex-end; gap: var(--s3); padding: var(--s3) var(--s4); border-top: 1px solid var(--bb-border); }
  .modal-scrim { position: fixed; inset: 0; background: rgba(31,27,22,.45); z-index: 200; display: grid; place-items: center; padding: var(--s4); }
  .modal { background: var(--bb-surface); border-radius: var(--bb-radius-lg); width: min(720px, 100%); max-height: calc(100vh - 32px); overflow: auto; padding: var(--s5) var(--s6); box-shadow: var(--bb-shadow-lg); animation: bb-fade-up .2s both; }
  .modal h2 { margin-bottom: var(--s4); }
  .actions-cell { display: flex; gap: 6px; justify-content: flex-end; }
`;
