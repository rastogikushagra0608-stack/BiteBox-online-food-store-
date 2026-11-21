using BiteBox.Application.Common;
using BiteBox.Application.Features.Orders;
using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;
using BiteBox.Domain.Exceptions;
using BiteBox.Domain.Pricing;
using Microsoft.Extensions.Options;

namespace BiteBox.Tests.Domain;

public class PricingTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Price_calculator_applies_discount_before_tax_and_adds_delivery()
    {
        var lines = new[] { new PriceLine(1, "Biryani", 299m, 2), new PriceLine(2, "Shake", 169m, 1) }; // 767
        var result = PriceCalculator.Calculate(lines, discount: 100m, deliveryFee: 40m, taxRate: 0.05m);

        Assert.Equal(767m, result.Subtotal);
        Assert.Equal(100m, result.Discount);
        Assert.Equal(33.35m, result.Tax);               // 5% of 667
        Assert.Equal(740.35m, result.Total);            // 667 + 33.35 + 40
    }

    [Fact]
    public void Discount_can_never_exceed_subtotal()
    {
        var result = PriceCalculator.Calculate([new PriceLine(1, "Tea", 99m, 1)], discount: 500m, deliveryFee: 0, taxRate: 0.05m);
        Assert.Equal(99m, result.Discount);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public void Percentage_coupon_respects_cap()
    {
        var coupon = ValidCoupon(DiscountType.Percentage, 20, max: 120, min: 399);
        Assert.Equal(100m, coupon.CalculateDiscountFor(500m, Now, 0));   // 20% of 500
        Assert.Equal(120m, coupon.CalculateDiscountFor(1000m, Now, 0));  // capped
    }

    [Fact]
    public void Flat_coupon_gives_fixed_amount()
    {
        var coupon = ValidCoupon(DiscountType.Flat, 50, min: 299);
        Assert.Equal(50m, coupon.CalculateDiscountFor(350m, Now, 0));
    }

    [Theory]
    [InlineData(250, 0, "coupon.min_order")]
    [InlineData(500, 1, "coupon.user_limit")]
    public void Coupon_rules_are_enforced(decimal subtotal, int timesUsed, string expectedCode)
    {
        var coupon = ValidCoupon(DiscountType.Flat, 50, min: 299);
        var ex = Assert.Throws<DomainException>(() => coupon.CalculateDiscountFor(subtotal, Now, timesUsed));
        Assert.Equal(expectedCode, ex.Code);
    }

    [Fact]
    public void Expired_or_exhausted_coupons_are_rejected()
    {
        var expired = ValidCoupon(DiscountType.Flat, 50);
        expired.EndsAt = Now.AddDays(-1);
        Assert.Equal("coupon.expired", Assert.Throws<DomainException>(() => expired.CalculateDiscountFor(500, Now, 0)).Code);

        var exhausted = ValidCoupon(DiscountType.Flat, 50);
        exhausted.UsageLimit = 10;
        exhausted.UsedCount = 10;
        Assert.Equal("coupon.exhausted", Assert.Throws<DomainException>(() => exhausted.CalculateDiscountFor(500, Now, 0)).Code);
    }

    [Fact]
    public void Delivery_fee_strategy_waives_fee_above_threshold_and_pickup_is_free()
    {
        var options = Options.Create(new PricingOptions { DeliveryFee = 40, FreeDeliveryThreshold = 499 });
        var calculator = new FulfillmentFeeCalculator([new HomeDeliveryFeeStrategy(options), new PickupFeeStrategy()]);

        Assert.Equal(40m, calculator.Calculate(FulfillmentType.Delivery, 300));
        Assert.Equal(0m, calculator.Calculate(FulfillmentType.Delivery, 499));
        Assert.Equal(0m, calculator.Calculate(FulfillmentType.Pickup, 100));
    }

    [Fact]
    public void Product_rejects_invalid_prices()
    {
        Assert.Throws<DomainException>(() => new Product(1, "X", "x", "d", 0m, 1));
        Assert.Throws<DomainException>(() => new Product(1, "X", "x", "d", -5m, 1));
        Assert.Equal(199.99m, new Product(1, "X", "x", "d", 199.994m, 1).Price);
    }

    private static Coupon ValidCoupon(DiscountType type, decimal value, decimal? max = null, decimal min = 0) => new()
    {
        Code = "TEST", Description = "t", DiscountType = type, Value = value, MaxDiscountAmount = max, MinOrderAmount = min,
        StartsAt = Now.AddDays(-1), EndsAt = Now.AddDays(10), PerUserLimit = 1, IsActive = true
    };
}
