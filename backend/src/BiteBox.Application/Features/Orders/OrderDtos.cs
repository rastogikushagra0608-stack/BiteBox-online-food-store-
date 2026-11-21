using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;

namespace BiteBox.Application.Features.Orders;

public sealed record QuoteRequest(FulfillmentType FulfillmentType, string? CouponCode);

public sealed record QuoteLineDto(int ProductId, string Name, string? ImageUrl, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record QuoteDto(
    IReadOnlyList<QuoteLineDto> Lines,
    decimal Subtotal, decimal Discount, decimal DeliveryFee, decimal Tax, decimal Total,
    string? AppliedCouponCode, string? CouponMessage, bool CouponValid,
    decimal MinimumOrderAmount, decimal FreeDeliveryThreshold, decimal TaxRate,
    IReadOnlyList<string> Problems);

public sealed record PlaceOrderRequest(
    FulfillmentType FulfillmentType,
    int? AddressId,
    PaymentMethod PaymentMethod,
    string? CouponCode,
    string? Notes,
    string IdempotencyKey);

public sealed record OrderSummaryDto(
    int Id, string OrderNumber, OrderStatus Status, FulfillmentType FulfillmentType,
    PaymentMethod PaymentMethod, PaymentStatus PaymentStatus, decimal Total, int ItemCount,
    string ItemsPreview, DateTime PlacedAt, string CustomerName);

public sealed record OrderItemDto(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal, string? ImageUrl, string? Slug);

public sealed record OrderTimelineDto(OrderStatus? From, OrderStatus To, string? Note, DateTime At);

public sealed record PaymentDto(PaymentMethod Method, PaymentStatus Status, decimal Amount, string? Reference, string? FailureReason, DateTime? PaidAt);

public sealed record OrderDetailDto(
    int Id, string OrderNumber, OrderStatus Status, FulfillmentType FulfillmentType,
    DeliveryAddress? DeliveryAddress, IReadOnlyList<OrderItemDto> Items,
    decimal Subtotal, decimal Discount, decimal DeliveryFee, decimal Tax, decimal Total,
    string? CouponCode, string? Notes, string? CancellationReason, PaymentDto Payment,
    IReadOnlyList<OrderTimelineDto> Timeline, DateTime PlacedAt, DateTime? CompletedAt,
    int CustomerId, string CustomerName, string CustomerPhone,
    IReadOnlyList<OrderStatus> AllowedNextStatuses, bool CanCancel, bool CanReview);

internal static class OrderMapping
{
    public static OrderDetailDto ToDetail(Order o, OrderActor actor, IReadOnlyDictionary<int, (string? Image, string Slug)> productInfo)
    {
        var next = o.NextStatusesFor(actor);
        return new OrderDetailDto(
            o.Id, o.OrderNumber, o.Status, o.FulfillmentType, o.DeliveryAddress,
            o.Items.Select(i =>
            {
                productInfo.TryGetValue(i.ProductId, out var info);
                return new OrderItemDto(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.LineTotal, info.Image, info.Slug);
            }).ToList(),
            o.Subtotal, o.DiscountAmount, o.DeliveryFee, o.TaxAmount, o.Total, o.CouponCode, o.Notes, o.CancellationReason,
            new PaymentDto(o.Payment.Method, o.Payment.Status, o.Payment.Amount, o.Payment.ProviderReference, o.Payment.FailureReason, o.Payment.PaidAt),
            o.StatusHistory.OrderBy(h => h.ChangedAt).ThenBy(h => h.Id).Select(h => new OrderTimelineDto(h.FromStatus, h.ToStatus, h.Note, h.ChangedAt)).ToList(),
            o.PlacedAt, o.CompletedAt, o.CustomerId, o.CustomerName, o.CustomerPhone,
            next.Where(s => s != OrderStatus.Cancelled).ToList(),
            next.Contains(OrderStatus.Cancelled),
            o.Status is OrderStatus.Delivered or OrderStatus.PickedUp);
    }
}
