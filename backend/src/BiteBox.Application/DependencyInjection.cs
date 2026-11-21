using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using BiteBox.Application.Features.Account;
using BiteBox.Application.Features.Admin;
using BiteBox.Application.Features.AI;
using BiteBox.Application.Features.Cart;
using BiteBox.Application.Features.Catalog;
using BiteBox.Application.Features.Orders;
using BiteBox.Domain.Events;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BiteBox.Application;

public static class DependencyInjection
{
    /// <summary>Registers use-case services. Comparable to component scanning of @Service classes in Spring.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PricingOptions>(configuration.GetSection(PricingOptions.Section));
        services.Configure<RestaurantOptions>(configuration.GetSection(RestaurantOptions.Section));
        services.AddSingleton(TimeProvider.System);

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // Strategies are registered as a collection; FulfillmentFeeCalculator picks the right one.
        services.AddSingleton<IFulfillmentFeeStrategy, HomeDeliveryFeeStrategy>();
        services.AddSingleton<IFulfillmentFeeStrategy, PickupFeeStrategy>();
        services.AddSingleton<FulfillmentFeeCalculator>();

        services.AddScoped<CatalogService>();
        services.AddScoped<CartService>();
        services.AddScoped<StockLedger>();
        services.AddScoped<OrderService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<AddressService>();
        services.AddScoped<FavoriteService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ReviewService>();

        services.AddScoped<AdminProductService>();
        services.AddScoped<AdminCategoryService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<CouponService>();
        services.AddScoped<AdminOrderService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<ReportService>();

        services.AddScoped<IBiteBotService, BiteBotService>();

        services.AddScoped<IDomainEventHandler<OrderPlacedEvent>, OrderNotificationHandler>();
        services.AddScoped<IDomainEventHandler<OrderStatusChangedEvent>, OrderNotificationHandler>();
        return services;
    }
}
