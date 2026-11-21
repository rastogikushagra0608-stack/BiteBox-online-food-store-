using BiteBox.Domain.Enums;
using static BiteBox.Domain.Enums.OrderActor;
using static BiteBox.Domain.Enums.OrderStatus;

namespace BiteBox.Domain.Orders;

/// <summary>
/// Declarative table of every legal order status transition and who may perform it.
/// Keeping the rules as data (rather than if/else scattered through services) makes them easy
/// to test exhaustively, to show in the UI as "next possible actions", and to explain in a viva.
/// </summary>
public static class OrderStateMachine
{
    private sealed record Rule(OrderStatus From, OrderStatus To, OrderActor[] Actors, FulfillmentType? OnlyFor = null);

    private static readonly Rule[] Rules =
    [
        // Online payment outcome: only the payment workflow (System) can move these.
        new(AwaitingPayment, Placed, [OrderActor.System]),
        new(AwaitingPayment, PaymentFailed, [OrderActor.System]),
        new(AwaitingPayment, Cancelled, [Customer, Admin]),

        new(Placed, Confirmed, [Admin, Kitchen]),
        new(Placed, Cancelled, [Customer, Admin]),

        new(Confirmed, Preparing, [Admin, Kitchen]),
        new(Confirmed, Cancelled, [Admin]),

        new(Preparing, Ready, [Admin, Kitchen]),

        new(Ready, PickedUp, [Admin, Kitchen], FulfillmentType.Pickup),
        new(Ready, OutForDelivery, [Admin, Delivery], FulfillmentType.Delivery),

        new(OutForDelivery, Delivered, [Admin, Delivery], FulfillmentType.Delivery),
    ];

    public static bool CanTransition(OrderStatus from, OrderStatus to, OrderActor actor, FulfillmentType fulfillment) =>
        Rules.Any(r => r.From == from && r.To == to && r.Actors.Contains(actor) && (r.OnlyFor is null || r.OnlyFor == fulfillment));

    public static IReadOnlyList<OrderStatus> NextStatuses(OrderStatus from, OrderActor actor, FulfillmentType fulfillment) =>
        Rules.Where(r => r.From == from && r.Actors.Contains(actor) && (r.OnlyFor is null || r.OnlyFor == fulfillment))
             .Select(r => r.To)
             .ToList();

    public static bool IsTerminal(OrderStatus status) => status is Delivered or PickedUp or Cancelled or PaymentFailed;

    /// <summary>Statuses that are considered "active" for kitchen/delivery dashboards.</summary>
    public static readonly OrderStatus[] ActiveStatuses = [Placed, Confirmed, Preparing, Ready, OutForDelivery];
}
