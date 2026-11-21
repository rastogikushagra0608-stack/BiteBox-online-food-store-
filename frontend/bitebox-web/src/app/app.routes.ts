import { Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard } from './core/http/guards';
import { StoreLayout } from './layout/store-layout';

/**
 * Every feature is lazy-loaded, so the first page only downloads the storefront shell.
 * The admin area is a separate layout and bundle, protected by role guards (the API enforces the same rules).
 */
export const routes: Routes = [
  {
    path: 'admin',
    canActivate: [roleGuard('Admin', 'Kitchen', 'Delivery')],
    loadComponent: () => import('./features/admin/admin-layout').then(m => m.AdminLayout),
    children: [
      { path: '', canActivate: [roleGuard('Admin')], title: 'Dashboard · BiteBox Admin', loadComponent: () => import('./features/admin/dashboard-page').then(m => m.DashboardPage) },
      { path: 'orders', title: 'Orders · BiteBox Admin', loadComponent: () => import('./features/admin/orders-page').then(m => m.OrdersPage) },
      { path: 'products', canActivate: [roleGuard('Admin')], title: 'Menu items · BiteBox Admin', loadComponent: () => import('./features/admin/catalog-admin').then(m => m.AdminProductsPage) },
      { path: 'categories', canActivate: [roleGuard('Admin')], title: 'Categories · BiteBox Admin', loadComponent: () => import('./features/admin/catalog-admin').then(m => m.AdminCategoriesPage) },
      { path: 'inventory', canActivate: [roleGuard('Admin')], title: 'Inventory · BiteBox Admin', loadComponent: () => import('./features/admin/catalog-admin').then(m => m.AdminInventoryPage) },
      { path: 'coupons', canActivate: [roleGuard('Admin')], title: 'Promotions · BiteBox Admin', loadComponent: () => import('./features/admin/business-admin').then(m => m.AdminCouponsPage) },
      { path: 'users', canActivate: [roleGuard('Admin')], title: 'People · BiteBox Admin', loadComponent: () => import('./features/admin/business-admin').then(m => m.AdminUsersPage) },
      { path: 'reports', canActivate: [roleGuard('Admin')], title: 'Reports · BiteBox Admin', loadComponent: () => import('./features/admin/business-admin').then(m => m.AdminReportsPage) },
    ],
  },
  {
    path: '',
    component: StoreLayout,
    children: [
      { path: '', title: 'BiteBox · Every Bite, Anytime.', loadComponent: () => import('./features/storefront/home').then(m => m.Home) },
      { path: 'menu', title: 'Menu · BiteBox', loadComponent: () => import('./features/menu/menu-page').then(m => m.MenuPage) },
      { path: 'menu/:slug', title: 'Dish · BiteBox', loadComponent: () => import('./features/menu/product-detail').then(m => m.ProductDetailPage) },
      { path: 'cart', title: 'Cart · BiteBox', loadComponent: () => import('./features/checkout/cart-page').then(m => m.CartPage) },
      { path: 'checkout', canActivate: [authGuard], title: 'Checkout · BiteBox', loadComponent: () => import('./features/checkout/checkout-page').then(m => m.CheckoutPage) },
      { path: 'pay/:orderNumber', canActivate: [authGuard], title: 'Payment · BiteBox', loadComponent: () => import('./features/checkout/payment-page').then(m => m.PaymentPage) },
      { path: 'login', canActivate: [guestGuard], title: 'Sign in · BiteBox', loadComponent: () => import('./features/auth/auth-pages').then(m => m.LoginPage) },
      { path: 'register', canActivate: [guestGuard], title: 'Create account · BiteBox', loadComponent: () => import('./features/auth/auth-pages').then(m => m.RegisterPage) },
      {
        path: 'account',
        canActivate: [authGuard],
        loadComponent: () => import('./features/account/account-pages').then(m => m.AccountLayout),
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'orders' },
          { path: 'orders', title: 'My orders · BiteBox', loadComponent: () => import('./features/account/account-pages').then(m => m.MyOrdersPage) },
          { path: 'orders/:orderNumber', title: 'Order · BiteBox', loadComponent: () => import('./features/account/account-pages').then(m => m.OrderDetailPage) },
          { path: 'favorites', title: 'Favourites · BiteBox', loadComponent: () => import('./features/account/account-pages').then(m => m.FavoritesPage) },
          { path: 'addresses', title: 'Addresses · BiteBox', loadComponent: () => import('./features/account/account-pages').then(m => m.AddressesPage) },
          { path: 'profile', title: 'Profile · BiteBox', loadComponent: () => import('./features/account/account-pages').then(m => m.ProfilePage) },
        ],
      },
      { path: '**', title: 'Not found · BiteBox', loadComponent: () => import('./features/storefront/not-found').then(m => m.NotFound) },
    ],
  },
];
