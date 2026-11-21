using BiteBox.Domain.Common;
using BiteBox.Domain.Enums;

namespace BiteBox.Domain.Pricing;

/// <summary>
/// Strategy pattern: each discount type encapsulates its own calculation. Adding a new type
/// (e.g. "buy one get one") means adding a class, not editing a switch spread across the codebase.
/// </summary>
public interface IDiscountStrategy
{
    decimal Calculate(decimal subtotal, decimal value, decimal? maxDiscount);
}

public sealed class PercentageDiscountStrategy : IDiscountStrategy
{
    public decimal Calculate(decimal subtotal, decimal value, decimal? maxDiscount)
    {
        var discount = Money.Round(subtotal * value / 100m);
        return maxDiscount is null ? discount : Math.Min(discount, maxDiscount.Value);
    }
}

public sealed class FlatDiscountStrategy : IDiscountStrategy
{
    public decimal Calculate(decimal subtotal, decimal value, decimal? maxDiscount) => Math.Min(value, subtotal);
}

/// <summary>Factory pattern: hides which concrete strategy is used for a given discount type.</summary>
public static class DiscountStrategyFactory
{
    private static readonly PercentageDiscountStrategy Percentage = new();
    private static readonly FlatDiscountStrategy Flat = new();

    public static IDiscountStrategy Create(DiscountType type) => type switch
    {
        DiscountType.Percentage => Percentage,
        DiscountType.Flat => Flat,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown discount type.")
    };
}
