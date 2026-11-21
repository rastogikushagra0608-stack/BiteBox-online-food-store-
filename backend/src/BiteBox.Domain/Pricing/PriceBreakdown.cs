using BiteBox.Domain.Common;

namespace BiteBox.Domain.Pricing;

public sealed record PriceLine(int ProductId, string ProductName, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => Money.Round(UnitPrice * Quantity);
}

public sealed record PriceBreakdown(decimal Subtotal, decimal Discount, decimal DeliveryFee, decimal Tax, decimal Total);

/// <summary>
/// Pure domain service that computes an order's totals. It is the single source of truth for pricing:
/// both the checkout "quote" and the actual order placement use it, so they can never disagree.
/// </summary>
public static class PriceCalculator
{
    /// <param name="taxRate">e.g. 0.05 for 5% GST, applied on (subtotal - discount).</param>
    public static PriceBreakdown Calculate(IEnumerable<PriceLine> lines, decimal discount, decimal deliveryFee, decimal taxRate)
    {
        var subtotal = Money.Round(lines.Sum(l => l.LineTotal));
        discount = Money.Round(Math.Clamp(discount, 0, subtotal));
        var taxable = subtotal - discount;
        var tax = Money.Round(taxable * taxRate);
        var total = Money.Round(taxable + tax + deliveryFee);
        return new PriceBreakdown(subtotal, discount, Money.Round(deliveryFee), tax, total);
    }
}
