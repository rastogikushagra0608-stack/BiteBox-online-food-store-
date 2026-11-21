import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { FormControl } from '@angular/forms';
import { firstValueFrom, isObservable, Observable } from 'rxjs';
import { App } from './app';
import { ProductCard } from './core/api/models';
import { authGuard, roleGuard } from './core/http/guards';
import { AuthStore } from './core/state/auth.store';
import { CartStore } from './core/state/cart.store';
import { problemMessage } from './core/state/toast';
import { HttpErrorResponse } from '@angular/common/http';
import { safeReturnUrl, strongPassword } from './features/auth/auth-pages';
import { InrPipe } from './shared/ui';

const dish: ProductCard = {
  id: 7, name: 'Masala Dosa', slug: 'masala-dosa', description: '', imageUrl: null, price: 149, isVegetarian: true,
  spiceLevel: 'Mild', categoryName: 'South Indian', categorySlug: 'south-indian', averageRating: 4.6, reviewCount: 10,
  inStock: true, onlyLeft: null, prepTimeMinutes: 15, isFeatured: false, servesCount: 1, tags: [],
};

function setup() {
  TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
  return { http: TestBed.inject(HttpTestingController), auth: TestBed.inject(AuthStore) };
}

async function runGuard(guard: ReturnType<typeof roleGuard>, url = '/admin'): Promise<boolean | UrlTree> {
  const result = TestBed.runInInjectionContext(() => guard({} as never, { url } as never));
  return isObservable(result) ? firstValueFrom(result as Observable<boolean | UrlTree>) : (result as boolean | UrlTree);
}

describe('App shell', () => {
  it('creates the root component', () => {
    setup();
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });
});

describe('Route guards', () => {
  beforeEach(() => localStorage.clear());

  it('sends guests to /login with a returnUrl', async () => {
    const { auth } = setup();
    auth.user.set(null);
    const result = await runGuard(authGuard, '/checkout');
    expect(result instanceof UrlTree).toBe(true);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/login?returnUrl=%2Fcheckout');
  });

  it('lets a customer into protected customer pages', async () => {
    const { auth } = setup();
    auth.user.set({ id: 1, fullName: 'Priya Sharma', email: 'p@example.com', phone: null, roles: ['Customer'] });
    expect(await runGuard(authGuard)).toBe(true);
  });

  it('keeps customers out of the admin area', async () => {
    const { auth } = setup();
    auth.user.set({ id: 1, fullName: 'Priya Sharma', email: 'p@example.com', phone: null, roles: ['Customer'] });
    const result = await runGuard(roleGuard('Admin'));
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
  });

  it('allows kitchen staff into staff routes but not admin-only routes', async () => {
    const { auth } = setup();
    auth.user.set({ id: 2, fullName: 'Kitchen Team', email: 'k@bitebox.dev', phone: null, roles: ['Kitchen'] });
    expect(await runGuard(roleGuard('Admin', 'Kitchen', 'Delivery'))).toBe(true);
    expect((await runGuard(roleGuard('Admin'))) instanceof UrlTree).toBe(true);
  });
});

describe('CartStore (guest mode)', () => {
  beforeEach(() => localStorage.clear());

  it('keeps a guest cart in localStorage and totals it', async () => {
    const { auth } = setup();
    auth.user.set(null);
    const cart = TestBed.inject(CartStore);
    TestBed.tick();

    await cart.add(dish, 2);
    await cart.add(dish, 1);

    expect(cart.count()).toBe(3);
    expect(cart.subtotal()).toBe(447);
    expect(JSON.parse(localStorage.getItem('bitebox.guestCart.v1')!)[0].quantity).toBe(3);

    await cart.setQuantity(dish.id, 0);
    expect(cart.count()).toBe(0);
  });

  it('merges the guest cart into the server cart after sign-in', async () => {
    const { auth, http } = setup();
    auth.user.set(null);
    const cart = TestBed.inject(CartStore);
    TestBed.tick();
    await cart.add(dish, 2);

    auth.user.set({ id: 1, fullName: 'Priya Sharma', email: 'p@example.com', phone: null, roles: ['Customer'] });
    TestBed.tick();

    const req = http.expectOne('/api/v1/cart/merge');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual([{ productId: 7, quantity: 2 }]);
    req.flush({ items: [], itemCount: 2, subtotal: 298, hasUnavailableItems: false });
    await Promise.resolve();
    await Promise.resolve();
    expect(localStorage.getItem('bitebox.guestCart.v1')).toBeNull();
  });
});

describe('Validators & helpers', () => {
  it('enforces the same password rule as the server', () => {
    expect(strongPassword(new FormControl('weak'))).toEqual({ weakPassword: true });
    expect(strongPassword(new FormControl('alllowercase1'))).toEqual({ weakPassword: true });
    expect(strongPassword(new FormControl('Student@123'))).toBeNull();
  });

  it('only accepts internal return URLs (no open redirects)', () => {
    expect(safeReturnUrl('/checkout')).toBe('/checkout');
    expect(safeReturnUrl('https://evil.example')).toBeNull();
    expect(safeReturnUrl('//evil.example')).toBeNull();
  });

  it('formats rupees with Indian digit grouping', () => {
    const pipe = new InrPipe();
    expect(pipe.transform(125000)).toBe('₹1,25,000');
    expect(pipe.transform(149.5)).toBe('₹149.50');
  });

  it('turns API problem details into readable messages', () => {
    const validation = new HttpErrorResponse({ status: 400, error: { errors: { phone: ['Enter a valid 10-digit Indian mobile number.'] } } });
    expect(problemMessage(validation)).toBe('Enter a valid 10-digit Indian mobile number.');
    expect(problemMessage(new HttpErrorResponse({ status: 0 }))).toContain('Cannot reach BiteBox');
    expect(problemMessage(new HttpErrorResponse({ status: 409, error: { detail: 'Only 2 portions left.' } }))).toBe('Only 2 portions left.');
  });
});
