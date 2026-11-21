using BiteBox.Domain.Common;
using BiteBox.Domain.Enums;
using BiteBox.Domain.Exceptions;
using BiteBox.Domain.Pricing;

namespace BiteBox.Domain.Entities;

public class Coupon : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DiscountType DiscountType { get; set; }
    /// <summary>Percentage (e.g. 20 = 20%) or flat rupee amount depending on <see cref="DiscountType"/>.</summary>
    public decimal Value { get; set; }
    /// <summary>Cap for percentage discounts. Null = no cap.</summary>
    public decimal? MaxDiscountAmount { get; set; }
    public decimal MinOrderAmount { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    /// <summary>Total redemptions allowed across all users. Null = unlimited.</summary>
    public int? UsageLimit { get; set; }
    public int PerUserLimit { get; set; } = 1;
    public int UsedCount { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Validates the coupon for a given cart and returns the discount. Throws <see cref="DomainException"/>
    /// with a customer-friendly message when the coupon cannot be applied.
    /// </summary>
    public decimal CalculateDiscountFor(decimal subtotal, DateTime now, int timesUsedByUser)
    {
        if (!IsActive) throw new DomainException("coupon.inactive", "This coupon is no longer active.");
        if (now < StartsAt) throw new DomainException("coupon.not_started", "This coupon is not valid yet.");
        if (now > EndsAt) throw new DomainException("coupon.expired", "This coupon has expired.");
        if (UsageLimit is not null && UsedCount >= UsageLimit)
            throw new DomainException("coupon.exhausted", "This coupon has reached its usage limit.");
        if (timesUsedByUser >= PerUserLimit)
            throw new DomainException("coupon.user_limit", "You have already used this coupon.");
        if (subtotal < MinOrderAmount)
            throw new DomainException("coupon.min_order", $"Add items worth ₹{MinOrderAmount - subtotal:0.##} more to use this coupon.");

        var strategy = DiscountStrategyFactory.Create(DiscountType);
        var discount = strategy.Calculate(subtotal, Value, MaxDiscountAmount);
        return Money.Round(Math.Min(discount, subtotal));
    }
}
