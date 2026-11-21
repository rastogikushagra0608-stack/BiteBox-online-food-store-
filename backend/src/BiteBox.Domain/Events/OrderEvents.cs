using BiteBox.Domain.Common;
using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;

namespace BiteBox.Domain.Events;

// Events hold a reference to the aggregate rather than its Id, because the database-generated Id
// is only known after SaveChanges — which is exactly when events are dispatched.

public sealed record OrderPlacedEvent(Order Order, DateTime OccurredAt) : IDomainEvent;

public sealed record OrderStatusChangedEvent(Order Order, OrderStatus From, OrderStatus To, DateTime OccurredAt) : IDomainEvent;
