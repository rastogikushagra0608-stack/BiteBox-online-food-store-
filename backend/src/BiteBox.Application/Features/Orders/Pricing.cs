using BiteBox.Application.Common;
using BiteBox.Domain.Enums;
using Microsoft.Extensions.Options;

namespace BiteBox.Application.Features.Orders;

/// <summary>
/// Strategy pattern for fulfilment charges. Home delivery and pickup price differently; a future
/// "express delivery" or distance-based fee is just another implementation registered in DI.
/// </summary>
public interface IFulfillmentFeeStrategy
{
    FulfillmentType Type { get; }
    decimal Calculate(decimal amountAfterDiscount);
}

public sealed class HomeDeliveryFeeStrategy(IOptions<PricingOptions> options) : IFulfillmentFeeStrategy
{
    public FulfillmentType Type => FulfillmentType.Delivery;

    public decimal Calculate(decimal amountAfterDiscount) =>
        amountAfterDiscount >= options.Value.FreeDeliveryThreshold ? 0m : options.Value.DeliveryFee;
}

public sealed class PickupFeeStrategy : IFulfillmentFeeStrategy
{
    public FulfillmentType Type => FulfillmentType.Pickup;
    public decimal Calculate(decimal amountAfterDiscount) => 0m;
}

/// <summary>Selects the strategy for a fulfilment type (all strategies are injected by DI).</summary>
public sealed class FulfillmentFeeCalculator(IEnumerable<IFulfillmentFeeStrategy> strategies)
{
    private readonly Dictionary<FulfillmentType, IFulfillmentFeeStrategy> _strategies = strategies.ToDictionary(s => s.Type);

    public decimal Calculate(FulfillmentType type, decimal amountAfterDiscount) =>
        _strategies.TryGetValue(type, out var strategy)
            ? strategy.Calculate(amountAfterDiscount)
            : throw new InvalidOperationException($"No fee strategy registered for {type}.");
}
