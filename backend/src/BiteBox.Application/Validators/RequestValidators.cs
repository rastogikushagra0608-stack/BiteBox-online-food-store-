using BiteBox.Application.Features.Account;
using BiteBox.Application.Features.Admin;
using BiteBox.Application.Features.AI;
using BiteBox.Application.Features.Auth;
using BiteBox.Application.Features.Cart;
using BiteBox.Application.Features.Orders;
using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;
using FluentValidation;

namespace BiteBox.Application.Validators;

// Server-side validation for every request body. The Angular forms mirror these rules for UX,
// but these are the ones that actually protect the system.

internal static class Rules
{
    public const string IndianMobile = @"^[6-9]\d{9}$";
    public const string Pincode = @"^[1-9]\d{5}$";

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(8).MaximumLength(100)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MinimumLength(2).MaximumLength(80);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(120);
        RuleFor(x => x.Phone).NotEmpty().Matches(Rules.IndianMobile).WithMessage("Enter a valid 10-digit Indian mobile number.");
        RuleFor(x => x.Password).StrongPassword();
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MinimumLength(2).MaximumLength(80);
        RuleFor(x => x.Phone).NotEmpty().Matches(Rules.IndianMobile).WithMessage("Enter a valid 10-digit Indian mobile number.");
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).StrongPassword().NotEqual(x => x.CurrentPassword).WithMessage("New password must be different.");
    }
}

public sealed class CartLineRequestValidator : AbstractValidator<CartLineRequest>
{
    public CartLineRequestValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Quantity).InclusiveBetween(1, CartItem.MaxQuantityPerItem);
    }
}

public sealed class AddressRequestValidator : AbstractValidator<AddressRequest>
{
    public AddressRequestValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(30);
        RuleFor(x => x.RecipientName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Phone).NotEmpty().Matches(Rules.IndianMobile).WithMessage("Enter a valid 10-digit Indian mobile number.");
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Line2).MaximumLength(150);
        RuleFor(x => x.City).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Pincode).NotEmpty().Matches(Rules.Pincode).WithMessage("Enter a valid 6-digit pincode.");
        RuleFor(x => x.Landmark).MaximumLength(100);
    }
}

public sealed class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(x => x.FulfillmentType).IsInEnum();
        RuleFor(x => x.PaymentMethod).IsInEnum();
        RuleFor(x => x.AddressId).NotNull().GreaterThan(0).When(x => x.FulfillmentType == FulfillmentType.Delivery)
            .WithMessage("Please choose a delivery address.");
        RuleFor(x => x.CouponCode).MaximumLength(30);
        RuleFor(x => x.Notes).MaximumLength(300);
        RuleFor(x => x.IdempotencyKey).NotEmpty().Length(8, 64);
    }
}

public sealed class CreateReviewRequestValidator : AbstractValidator<CreateReviewRequest>
{
    public CreateReviewRequestValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).MaximumLength(500);
    }
}

public sealed class BiteBotRequestValidator : AbstractValidator<BiteBotRequest>
{
    public BiteBotRequestValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(300);
    }
}

public sealed class ProductUpsertRequestValidator : AbstractValidator<ProductUpsertRequest>
{
    public ProductUpsertRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(600);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Price).GreaterThan(0).LessThanOrEqualTo(100_000).PrecisionScale(10, 2, true);
        RuleFor(x => x.SpiceLevel).IsInEnum();
        RuleFor(x => x.PrepTimeMinutes).InclusiveBetween(1, 180);
        RuleFor(x => x.ServesCount).InclusiveBetween(1, 20);
        RuleFor(x => x.Calories).InclusiveBetween(0, 5000).When(x => x.Calories is not null);
        RuleFor(x => x.LowStockThreshold).InclusiveBetween(0, 1000);
        RuleFor(x => x.InitialStock).InclusiveBetween(0, 10_000).When(x => x.InitialStock is not null);
        RuleFor(x => x.ImageUrl).MaximumLength(500)
            .Must(u => u is null || u.StartsWith("/uploads/") || Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage("Image must be an uploaded image or an https:// URL.");
        RuleFor(x => x.Tags).MaximumLength(200);
    }
}

public sealed class CategoryUpsertRequestValidator : AbstractValidator<CategoryUpsertRequest>
{
    public CategoryUpsertRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.ImageUrl).MaximumLength(500);
        RuleFor(x => x.DisplayOrder).InclusiveBetween(0, 1000);
    }
}

public sealed class StockAdjustmentRequestValidator : AbstractValidator<StockAdjustmentRequest>
{
    public StockAdjustmentRequestValidator()
    {
        RuleFor(x => x.QuantityChange).NotEqual(0).InclusiveBetween(-10_000, 10_000);
        RuleFor(x => x.Reason).IsInEnum()
            .Must(r => r is InventoryReason.Restock or InventoryReason.ManualAdjustment or InventoryReason.Wastage)
            .WithMessage("Reason must be Restock, ManualAdjustment or Wastage.");
        RuleFor(x => x.Note).MaximumLength(200);
    }
}

public sealed class CouponUpsertRequestValidator : AbstractValidator<CouponUpsertRequest>
{
    public CouponUpsertRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(3, 30).Matches("^[A-Za-z0-9_-]+$").WithMessage("Code may contain letters, digits, '-' and '_' only.");
        RuleFor(x => x.Description).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DiscountType).IsInEnum();
        RuleFor(x => x.Value).GreaterThan(0);
        RuleFor(x => x.Value).LessThanOrEqualTo(90).When(x => x.DiscountType == DiscountType.Percentage)
            .WithMessage("Percentage discount cannot exceed 90%.");
        RuleFor(x => x.MaxDiscountAmount).GreaterThan(0).When(x => x.MaxDiscountAmount is not null);
        RuleFor(x => x.MinOrderAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt).WithMessage("End date must be after start date.");
        RuleFor(x => x.UsageLimit).GreaterThan(0).When(x => x.UsageLimit is not null);
        RuleFor(x => x.PerUserLimit).InclusiveBetween(1, 100);
    }
}

public sealed class ChangeOrderStatusRequestValidator : AbstractValidator<ChangeOrderStatusRequest>
{
    public ChangeOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(200);
        RuleFor(x => x.Note).NotEmpty().When(x => x.Status == OrderStatus.Cancelled).WithMessage("Please give a cancellation reason.");
    }
}

public sealed class CreateStaffRequestValidator : AbstractValidator<CreateStaffRequest>
{
    public CreateStaffRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Phone).NotEmpty().Matches(Rules.IndianMobile);
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.Role).Must(r => r is "Admin" or "Kitchen" or "Delivery").WithMessage("Role must be Admin, Kitchen or Delivery.");
    }
}
