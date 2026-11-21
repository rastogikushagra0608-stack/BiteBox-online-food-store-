import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Address, AddressRequest, AdminCategory, AdminProduct, AdminUser, BiteBotResponse, Cart, Category, CategoryUpsert,
  Coupon, CouponUpsert, Dashboard, FulfillmentType, InventoryItem, InventoryReason, InventoryTransaction, Me,
  NotificationFeed, OrderDetail, OrderStatus, OrderSummary, PagedResult, PlaceOrderRequest, ProductCard, ProductDetail,
  ProductQuery, ProductUpsert, Promo, Quote, Review, ReviewSummary, TopProduct, DailySales,
} from './models';

const API = '/api/v1';

/** Drops undefined/null/'' so they don't appear in the query string. */
export function toParams(query: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
  }
  return params;
}

// One small service per API area keeps components free of URLs and HTTP details.

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private http = inject(HttpClient);
  csrf(): Observable<void> { return this.http.get<void>(`${API}/auth/csrf`); }
  me(): Observable<Me> { return this.http.get<Me>(`${API}/auth/me`); }
  login(body: { email: string; password: string; rememberMe: boolean }): Observable<Me> { return this.http.post<Me>(`${API}/auth/login`, body); }
  register(body: { fullName: string; email: string; phone: string; password: string }): Observable<Me> { return this.http.post<Me>(`${API}/auth/register`, body); }
  logout(): Observable<void> { return this.http.post<void>(`${API}/auth/logout`, {}); }
  updateProfile(body: { fullName: string; phone: string }): Observable<Me> { return this.http.put<Me>(`${API}/auth/me`, body); }
  changePassword(body: { currentPassword: string; newPassword: string }): Observable<void> { return this.http.post<void>(`${API}/auth/me/password`, body); }
}

@Injectable({ providedIn: 'root' })
export class CatalogApi {
  private http = inject(HttpClient);
  categories(): Observable<Category[]> { return this.http.get<Category[]>(`${API}/categories`); }
  products(query: ProductQuery): Observable<PagedResult<ProductCard>> { return this.http.get<PagedResult<ProductCard>>(`${API}/products`, { params: toParams(query) }); }
  product(slug: string): Observable<ProductDetail> { return this.http.get<ProductDetail>(`${API}/products/${encodeURIComponent(slug)}`); }
  related(slug: string): Observable<ProductCard[]> { return this.http.get<ProductCard[]>(`${API}/products/${encodeURIComponent(slug)}/related`); }
  reviews(productId: number): Observable<ReviewSummary> { return this.http.get<ReviewSummary>(`${API}/products/${productId}/reviews`); }
  addReview(productId: number, body: { rating: number; comment: string | null }): Observable<Review> { return this.http.post<Review>(`${API}/products/${productId}/reviews`, body); }
  promotions(): Observable<Promo[]> { return this.http.get<Promo[]>(`${API}/promotions`); }
}

@Injectable({ providedIn: 'root' })
export class CartApi {
  private http = inject(HttpClient);
  get(): Observable<Cart> { return this.http.get<Cart>(`${API}/cart`); }
  add(productId: number, quantity: number): Observable<Cart> { return this.http.post<Cart>(`${API}/cart/items`, { productId, quantity }); }
  setQuantity(productId: number, quantity: number): Observable<Cart> { return this.http.put<Cart>(`${API}/cart/items/${productId}`, { quantity }); }
  remove(productId: number): Observable<Cart> { return this.http.delete<Cart>(`${API}/cart/items/${productId}`); }
  clear(): Observable<void> { return this.http.delete<void>(`${API}/cart`); }
  merge(lines: { productId: number; quantity: number }[]): Observable<Cart> { return this.http.post<Cart>(`${API}/cart/merge`, lines); }
}

@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private http = inject(HttpClient);
  quote(fulfillmentType: FulfillmentType, couponCode: string | null): Observable<Quote> { return this.http.post<Quote>(`${API}/orders/quote`, { fulfillmentType, couponCode }); }
  place(body: PlaceOrderRequest): Observable<OrderDetail> { return this.http.post<OrderDetail>(`${API}/orders`, body); }
  mine(page = 1, pageSize = 10): Observable<PagedResult<OrderSummary>> { return this.http.get<PagedResult<OrderSummary>>(`${API}/orders`, { params: toParams({ page, pageSize }) }); }
  get(orderNumber: string): Observable<OrderDetail> { return this.http.get<OrderDetail>(`${API}/orders/${orderNumber}`); }
  cancel(orderNumber: string, reason: string | null): Observable<OrderDetail> { return this.http.post<OrderDetail>(`${API}/orders/${orderNumber}/cancel`, { reason }); }
  reorder(orderNumber: string): Observable<{ added: number }> { return this.http.post<{ added: number }>(`${API}/orders/${orderNumber}/reorder`, {}); }
  pay(orderNumber: string, success: boolean): Observable<OrderDetail> { return this.http.post<OrderDetail>(`${API}/orders/${orderNumber}/payment`, { success }); }
}

@Injectable({ providedIn: 'root' })
export class AccountApi {
  private http = inject(HttpClient);
  addresses(): Observable<Address[]> { return this.http.get<Address[]>(`${API}/account/addresses`); }
  createAddress(body: AddressRequest): Observable<Address> { return this.http.post<Address>(`${API}/account/addresses`, body); }
  updateAddress(id: number, body: AddressRequest): Observable<Address> { return this.http.put<Address>(`${API}/account/addresses/${id}`, body); }
  deleteAddress(id: number): Observable<void> { return this.http.delete<void>(`${API}/account/addresses/${id}`); }
  favorites(): Observable<ProductCard[]> { return this.http.get<ProductCard[]>(`${API}/account/favorites`); }
  favoriteIds(): Observable<number[]> { return this.http.get<number[]>(`${API}/account/favorites/ids`); }
  addFavorite(productId: number): Observable<void> { return this.http.put<void>(`${API}/account/favorites/${productId}`, {}); }
  removeFavorite(productId: number): Observable<void> { return this.http.delete<void>(`${API}/account/favorites/${productId}`); }
  notifications(): Observable<NotificationFeed> { return this.http.get<NotificationFeed>(`${API}/account/notifications`); }
  markAllRead(): Observable<void> { return this.http.post<void>(`${API}/account/notifications/read-all`, {}); }
  myReviews(): Observable<Review[]> { return this.http.get<Review[]>(`${API}/account/reviews`); }
}

@Injectable({ providedIn: 'root' })
export class BiteBotApi {
  private http = inject(HttpClient);
  ask(message: string): Observable<BiteBotResponse> { return this.http.post<BiteBotResponse>(`${API}/ai/bitebot`, { message }); }
}

@Injectable({ providedIn: 'root' })
export class AdminApi {
  private http = inject(HttpClient);
  private base = `${API}/admin`;

  dashboard(from?: string, to?: string): Observable<Dashboard> { return this.http.get<Dashboard>(`${this.base}/dashboard`, { params: toParams({ from, to }) }); }
  sales(from?: string, to?: string): Observable<DailySales[]> { return this.http.get<DailySales[]>(`${this.base}/reports/sales`, { params: toParams({ from, to }) }); }
  productReport(from?: string, to?: string): Observable<TopProduct[]> { return this.http.get<TopProduct[]>(`${this.base}/reports/products`, { params: toParams({ from, to }) }); }
  csvUrl(kind: 'sales' | 'products', from?: string, to?: string): string { return `${this.base}/reports/${kind}.csv?${toParams({ from, to }).toString()}`; }

  products(q: { search?: string; categoryId?: number; lowStockOnly?: boolean; page?: number; pageSize?: number }): Observable<PagedResult<AdminProduct>> {
    return this.http.get<PagedResult<AdminProduct>>(`${this.base}/products`, { params: toParams(q) });
  }
  product(id: number): Observable<AdminProduct> { return this.http.get<AdminProduct>(`${this.base}/products/${id}`); }
  createProduct(body: ProductUpsert): Observable<AdminProduct> { return this.http.post<AdminProduct>(`${this.base}/products`, body); }
  updateProduct(id: number, body: ProductUpsert): Observable<AdminProduct> { return this.http.put<AdminProduct>(`${this.base}/products/${id}`, body); }
  deleteProduct(id: number): Observable<void> { return this.http.delete<void>(`${this.base}/products/${id}`); }
  uploadImage(id: number, file: File): Observable<AdminProduct> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<AdminProduct>(`${this.base}/products/${id}/image`, form);
  }

  categories(): Observable<AdminCategory[]> { return this.http.get<AdminCategory[]>(`${this.base}/categories`); }
  createCategory(body: CategoryUpsert): Observable<AdminCategory> { return this.http.post<AdminCategory>(`${this.base}/categories`, body); }
  updateCategory(id: number, body: CategoryUpsert): Observable<AdminCategory> { return this.http.put<AdminCategory>(`${this.base}/categories/${id}`, body); }
  deleteCategory(id: number): Observable<void> { return this.http.delete<void>(`${this.base}/categories/${id}`); }

  inventory(lowStockOnly = false): Observable<InventoryItem[]> { return this.http.get<InventoryItem[]>(`${this.base}/inventory`, { params: toParams({ lowStockOnly }) }); }
  adjustStock(productId: number, body: { quantityChange: number; reason: InventoryReason; note: string | null }): Observable<InventoryItem> {
    return this.http.post<InventoryItem>(`${this.base}/inventory/${productId}/adjust`, body);
  }
  inventoryHistory(productId?: number, page = 1): Observable<PagedResult<InventoryTransaction>> {
    return this.http.get<PagedResult<InventoryTransaction>>(`${this.base}/inventory/transactions`, { params: toParams({ productId, page }) });
  }

  coupons(): Observable<Coupon[]> { return this.http.get<Coupon[]>(`${this.base}/coupons`); }
  createCoupon(body: CouponUpsert): Observable<Coupon> { return this.http.post<Coupon>(`${this.base}/coupons`, body); }
  updateCoupon(id: number, body: CouponUpsert): Observable<Coupon> { return this.http.put<Coupon>(`${this.base}/coupons/${id}`, body); }
  deleteCoupon(id: number): Observable<void> { return this.http.delete<void>(`${this.base}/coupons/${id}`); }

  orders(q: { status?: OrderStatus; search?: string; from?: string; to?: string; fulfillment?: FulfillmentType; activeOnly?: boolean; page?: number; pageSize?: number }): Observable<PagedResult<OrderSummary>> {
    return this.http.get<PagedResult<OrderSummary>>(`${this.base}/orders`, { params: toParams(q) });
  }
  order(orderNumber: string): Observable<OrderDetail> { return this.http.get<OrderDetail>(`${this.base}/orders/${orderNumber}`); }
  changeStatus(orderNumber: string, status: OrderStatus, note: string | null): Observable<OrderDetail> {
    return this.http.post<OrderDetail>(`${this.base}/orders/${orderNumber}/status`, { status, note });
  }
  markRefunded(orderNumber: string): Observable<OrderDetail> { return this.http.post<OrderDetail>(`${this.base}/orders/${orderNumber}/refund`, {}); }

  users(q: { search?: string; role?: string; page?: number }): Observable<PagedResult<AdminUser>> { return this.http.get<PagedResult<AdminUser>>(`${this.base}/users`, { params: toParams(q) }); }
  userOrders(id: number): Observable<PagedResult<OrderSummary>> { return this.http.get<PagedResult<OrderSummary>>(`${this.base}/users/${id}/orders`); }
  setUserActive(id: number, isActive: boolean): Observable<void> { return this.http.put<void>(`${this.base}/users/${id}/active`, { isActive }); }
  setUserRole(id: number, role: string): Observable<void> { return this.http.put<void>(`${this.base}/users/${id}/role`, { role }); }
  createStaff(body: { fullName: string; email: string; phone: string; password: string; role: string }): Observable<unknown> { return this.http.post(`${this.base}/users/staff`, body); }
}
