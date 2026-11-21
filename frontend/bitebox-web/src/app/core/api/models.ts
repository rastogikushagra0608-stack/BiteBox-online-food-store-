// Typed mirrors of the BiteBox API contracts (backend DTOs). Enums travel as strings.

export type SpiceLevel = 'None' | 'Mild' | 'Medium' | 'Hot';
export type FulfillmentType = 'Delivery' | 'Pickup';
export type PaymentMethod = 'CashOnDelivery' | 'OnlineSimulated';
export type PaymentStatus = 'Pending' | 'Paid' | 'Failed' | 'RefundPending' | 'Refunded';
export type DiscountType = 'Percentage' | 'Flat';
export type InventoryReason = 'InitialStock' | 'Restock' | 'OrderPlaced' | 'OrderCancelled' | 'ManualAdjustment' | 'Wastage';
export type OrderStatus =
  | 'AwaitingPayment' | 'Placed' | 'Confirmed' | 'Preparing' | 'Ready'
  | 'OutForDelivery' | 'Delivered' | 'PickedUp' | 'Cancelled' | 'PaymentFailed';
export type Role = 'Admin' | 'Kitchen' | 'Delivery' | 'Customer';
export type ProductSort = 'Popular' | 'PriceLowToHigh' | 'PriceHighToLow' | 'Rating' | 'Newest';

export interface PagedResult<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }

export interface Me { id: number; fullName: string; email: string; phone: string | null; roles: Role[]; }

export interface Category { id: number; name: string; slug: string; description: string | null; imageUrl: string | null; productCount: number; }

export interface ProductCard {
  id: number; name: string; slug: string; description: string; imageUrl: string | null; price: number;
  isVegetarian: boolean; spiceLevel: SpiceLevel; categoryName: string; categorySlug: string;
  averageRating: number; reviewCount: number; inStock: boolean; onlyLeft: number | null;
  prepTimeMinutes: number; isFeatured: boolean; servesCount: number; tags: string[];
}

export interface ProductDetail extends ProductCard { categoryId: number; calories: number | null; isFavorite: boolean; }

export interface ProductQuery {
  search?: string; category?: string; vegetarian?: boolean; minPrice?: number; maxPrice?: number;
  maxSpice?: SpiceLevel; featuredOnly?: boolean; inStockOnly?: boolean; sort?: ProductSort; page?: number; pageSize?: number;
}

export interface Review { id: number; productId: number; authorName: string; rating: number; comment: string | null; createdAt: string; }
export interface ReviewSummary { items: Review[]; averageRating: number; reviewCount: number; canReview: boolean; hasReviewed: boolean; }

export interface Promo { code: string; description: string; discountType: DiscountType; value: number; maxDiscountAmount: number | null; minOrderAmount: number; endsAt: string; }

export interface CartItem {
  productId: number; name: string; slug: string; imageUrl: string | null; unitPrice: number; quantity: number;
  lineTotal: number; isVegetarian: boolean; inStock: boolean; availableQuantity: number;
}
export interface Cart { items: CartItem[]; itemCount: number; subtotal: number; hasUnavailableItems: boolean; }

export interface Quote {
  lines: { productId: number; name: string; imageUrl: string | null; unitPrice: number; quantity: number; lineTotal: number }[];
  subtotal: number; discount: number; deliveryFee: number; tax: number; total: number;
  appliedCouponCode: string | null; couponMessage: string | null; couponValid: boolean;
  minimumOrderAmount: number; freeDeliveryThreshold: number; taxRate: number; problems: string[];
}

export interface PlaceOrderRequest {
  fulfillmentType: FulfillmentType; addressId: number | null; paymentMethod: PaymentMethod;
  couponCode: string | null; notes: string | null; idempotencyKey: string;
}

export interface DeliveryAddress { recipientName: string; phone: string; line1: string; line2: string | null; city: string; pincode: string; landmark: string | null; }

export interface OrderSummary {
  id: number; orderNumber: string; status: OrderStatus; fulfillmentType: FulfillmentType; paymentMethod: PaymentMethod;
  paymentStatus: PaymentStatus; total: number; itemCount: number; itemsPreview: string; placedAt: string; customerName: string;
}

export interface OrderDetail {
  id: number; orderNumber: string; status: OrderStatus; fulfillmentType: FulfillmentType; deliveryAddress: DeliveryAddress | null;
  items: { productId: number; productName: string; unitPrice: number; quantity: number; lineTotal: number; imageUrl: string | null; slug: string | null }[];
  subtotal: number; discount: number; deliveryFee: number; tax: number; total: number;
  couponCode: string | null; notes: string | null; cancellationReason: string | null;
  payment: { method: PaymentMethod; status: PaymentStatus; amount: number; reference: string | null; failureReason: string | null; paidAt: string | null };
  timeline: { from: OrderStatus | null; to: OrderStatus; note: string | null; at: string }[];
  placedAt: string; completedAt: string | null; customerId: number; customerName: string; customerPhone: string;
  allowedNextStatuses: OrderStatus[]; canCancel: boolean; canReview: boolean;
}

export interface Address {
  id: number; label: string; recipientName: string; phone: string; line1: string; line2: string | null;
  city: string; pincode: string; landmark: string | null; isDefault: boolean;
}
export type AddressRequest = Omit<Address, 'id'>;

export interface Notification { id: number; title: string; message: string; link: string | null; isRead: boolean; createdAt: string; }
export interface NotificationFeed { items: Notification[]; unreadCount: number; }

export interface BiteBotResponse {
  reply: string;
  suggestions: { product: ProductCard; reason: string }[];
  combo: { items: { product: ProductCard; quantity: number }[]; foodTotal: number; summary: string } | null;
  understoodAs: string[];
  followUps: string[];
  engine: string;
}

/* ----- Admin ----- */
export interface AdminProduct {
  id: number; name: string; slug: string; description: string; categoryId: number; categoryName: string; price: number;
  imageUrl: string | null; isVegetarian: boolean; spiceLevel: SpiceLevel; calories: number | null; prepTimeMinutes: number;
  servesCount: number; tags: string; isAvailable: boolean; isFeatured: boolean; isDeleted: boolean; stockQuantity: number;
  lowStockThreshold: number; isLowStock: boolean; averageRating: number; reviewCount: number; updatedAt: string;
}
export interface ProductUpsert {
  name: string; description: string; categoryId: number; price: number; imageUrl: string | null; isVegetarian: boolean;
  spiceLevel: SpiceLevel; calories: number | null; prepTimeMinutes: number; servesCount: number; tags: string | null;
  isAvailable: boolean; isFeatured: boolean; lowStockThreshold: number; initialStock: number | null;
}
export interface AdminCategory { id: number; name: string; slug: string; description: string | null; imageUrl: string | null; displayOrder: number; isActive: boolean; productCount: number; }
export type CategoryUpsert = Pick<AdminCategory, 'name' | 'description' | 'imageUrl' | 'displayOrder' | 'isActive'>;
export interface InventoryItem { productId: number; name: string; categoryName: string; imageUrl: string | null; stockQuantity: number; lowStockThreshold: number; isLowStock: boolean; isAvailable: boolean; }
export interface InventoryTransaction { id: number; productId: number; productName: string; quantityChange: number; stockAfter: number; reason: InventoryReason; note: string | null; orderNumber: string | null; createdAt: string; }
export interface Coupon {
  id: number; code: string; description: string; discountType: DiscountType; value: number; maxDiscountAmount: number | null;
  minOrderAmount: number; startsAt: string; endsAt: string; usageLimit: number | null; perUserLimit: number; usedCount: number;
  isActive: boolean; isCurrentlyValid: boolean;
}
export type CouponUpsert = Omit<Coupon, 'id' | 'usedCount' | 'isCurrentlyValid'>;
export interface AdminUser { id: number; fullName: string; email: string; phone: string | null; isActive: boolean; roles: Role[]; createdAt: string; orderCount: number; totalSpent: number; }
export interface DailySales { date: string; orders: number; revenue: number; }
export interface TopProduct { productId: number; name: string; imageUrl: string | null; quantitySold: number; revenue: number; }
export interface Dashboard {
  from: string; to: string; totalOrders: number; revenue: number; averageOrderValue: number;
  activeOrders: number; completedOrders: number; cancelledOrders: number; newCustomers: number;
  salesByDay: DailySales[]; statusBreakdown: { status: OrderStatus; count: number }[];
  topProducts: TopProduct[]; lowStock: InventoryItem[]; recentOrders: OrderSummary[];
}

/** RFC 7807 error body returned by the API. */
export interface Problem { title?: string; detail?: string; status?: number; code?: string; errors?: Record<string, string[]>; }
