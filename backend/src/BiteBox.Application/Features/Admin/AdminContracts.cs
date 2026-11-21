using BiteBox.Domain.Enums;

namespace BiteBox.Application.Features.Admin;

public sealed record ProductUpsertRequest(
    string Name, string Description, int CategoryId, decimal Price, string? ImageUrl,
    bool IsVegetarian, SpiceLevel SpiceLevel, int? Calories, int PrepTimeMinutes, int ServesCount,
    string? Tags, bool IsAvailable, bool IsFeatured, int LowStockThreshold, int? InitialStock);

public sealed record AdminProductDto(
    int Id, string Name, string Slug, string Description, int CategoryId, string CategoryName, decimal Price, string? ImageUrl,
    bool IsVegetarian, SpiceLevel SpiceLevel, int? Calories, int PrepTimeMinutes, int ServesCount, string Tags,
    bool IsAvailable, bool IsFeatured, bool IsDeleted, int StockQuantity, int LowStockThreshold, bool IsLowStock,
    decimal AverageRating, int ReviewCount, DateTime UpdatedAt);

public sealed record AdminProductQuery(string? Search, int? CategoryId, bool? LowStockOnly, bool IncludeDeleted = false, int Page = 1, int PageSize = 20);

public sealed record CategoryUpsertRequest(string Name, string? Description, string? ImageUrl, int DisplayOrder, bool IsActive);

public sealed record AdminCategoryDto(int Id, string Name, string Slug, string? Description, string? ImageUrl, int DisplayOrder, bool IsActive, int ProductCount);

public sealed record StockAdjustmentRequest(int QuantityChange, InventoryReason Reason, string? Note);

public sealed record InventoryItemDto(int ProductId, string Name, string CategoryName, string? ImageUrl, int StockQuantity, int LowStockThreshold, bool IsLowStock, bool IsAvailable);

public sealed record InventoryTransactionDto(int Id, int ProductId, string ProductName, int QuantityChange, int StockAfter, InventoryReason Reason, string? Note, string? OrderNumber, DateTime CreatedAt);

public sealed record CouponUpsertRequest(
    string Code, string Description, DiscountType DiscountType, decimal Value, decimal? MaxDiscountAmount,
    decimal MinOrderAmount, DateTime StartsAt, DateTime EndsAt, int? UsageLimit, int PerUserLimit, bool IsActive);

public sealed record CouponDto(
    int Id, string Code, string Description, DiscountType DiscountType, decimal Value, decimal? MaxDiscountAmount,
    decimal MinOrderAmount, DateTime StartsAt, DateTime EndsAt, int? UsageLimit, int PerUserLimit, int UsedCount,
    bool IsActive, bool IsCurrentlyValid);

/// <summary>Public promo shown on the storefront.</summary>
public sealed record PromoDto(string Code, string Description, DiscountType DiscountType, decimal Value, decimal? MaxDiscountAmount, decimal MinOrderAmount, DateTime EndsAt);

public sealed record AdminOrderQuery(
    OrderStatus? Status, string? Search, DateTime? From, DateTime? To, FulfillmentType? Fulfillment,
    bool ActiveOnly = false, int Page = 1, int PageSize = 20);

public sealed record ChangeOrderStatusRequest(OrderStatus Status, string? Note);

public sealed record CreateStaffRequest(string FullName, string Email, string Phone, string Password, string Role);
public sealed record SetRoleRequest(string Role);
public sealed record SetActiveRequest(bool IsActive);

public sealed record AdminUserDto(int Id, string FullName, string Email, string? Phone, bool IsActive, IReadOnlyList<string> Roles,
    DateTime CreatedAt, int OrderCount, decimal TotalSpent);

public sealed record DailySalesDto(DateOnly Date, int Orders, decimal Revenue);
public sealed record StatusCountDto(OrderStatus Status, int Count);
public sealed record TopProductDto(int ProductId, string Name, string? ImageUrl, int QuantitySold, decimal Revenue);

public sealed record DashboardDto(
    DateOnly From, DateOnly To,
    int TotalOrders, decimal Revenue, decimal AverageOrderValue,
    int ActiveOrders, int CompletedOrders, int CancelledOrders, int NewCustomers,
    IReadOnlyList<DailySalesDto> SalesByDay,
    IReadOnlyList<StatusCountDto> StatusBreakdown,
    IReadOnlyList<TopProductDto> TopProducts,
    IReadOnlyList<InventoryItemDto> LowStock,
    IReadOnlyList<Orders.OrderSummaryDto> RecentOrders);
