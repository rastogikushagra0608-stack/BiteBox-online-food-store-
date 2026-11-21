using BiteBox.Application.Abstractions;
using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;
using BiteBox.Domain.Events;

namespace BiteBox.Application.Features.Orders;

/// <summary>
/// Observer: turns order domain events into in-app notifications for the customer.
/// The order code never calls this directly, which keeps the Order aggregate unaware of notifications.
/// </summary>
public sealed class OrderNotificationHandler(IApplicationDbContext db) :
    IDomainEventHandler<OrderPlacedEvent>,
    IDomainEventHandler<OrderStatusChangedEvent>
{
    public Task HandleAsync(OrderPlacedEvent e, CancellationToken ct)
    {
        Add(e.Order, "Order placed 🎉", $"We've received order {e.Order.OrderNumber}. The kitchen will confirm it shortly.", e.OccurredAt);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderStatusChangedEvent e, CancellationToken ct)
    {
        var (title, message) = e.To switch
        {
            OrderStatus.Confirmed => ("Order confirmed", "The restaurant has accepted your order."),
            OrderStatus.Preparing => ("Being prepared 👩‍🍳", "Our chefs are cooking your food."),
            OrderStatus.Ready when e.Order.FulfillmentType == FulfillmentType.Pickup => ("Ready for pickup", "Your order is packed and waiting at the counter."),
            OrderStatus.Ready => ("Packed and ready", "Your order is packed and waiting for a delivery partner."),
            OrderStatus.OutForDelivery => ("Out for delivery 🛵", "Your order is on its way."),
            OrderStatus.Delivered => ("Delivered", "Enjoy your meal! Tell us how it was by leaving a review."),
            OrderStatus.PickedUp => ("Picked up", "Enjoy your meal! Tell us how it was by leaving a review."),
            OrderStatus.Cancelled => ("Order cancelled", $"Order {e.Order.OrderNumber} was cancelled. {e.Order.CancellationReason}"),
            OrderStatus.PaymentFailed => ("Payment failed", "Your payment did not go through, so the order was not placed. You can reorder anytime."),
            _ => (null, null)
        };

        if (title is not null) Add(e.Order, title, message!, e.OccurredAt);
        return Task.CompletedTask;
    }

    private void Add(Order order, string title, string message, DateTime at) =>
        db.Notifications.Add(new Notification
        {
            UserId = order.CustomerId,
            Title = title,
            Message = message,
            Link = $"/account/orders/{order.OrderNumber}",
            CreatedAt = at
        });
}
