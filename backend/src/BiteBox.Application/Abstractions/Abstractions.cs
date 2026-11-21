using BiteBox.Domain.Common;
using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BiteBox.Application.Abstractions;

/// <summary>
/// The application's view of the database. EF Core's DbContext already implements the Repository
/// (DbSet) and Unit of Work (SaveChanges) patterns, so we expose it through this interface instead of
/// wrapping every entity in a generic repository. Application code stays independent of MySQL.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<Address> Addresses { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Favorite> Favorites { get; }
    DbSet<Review> Reviews { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<InventoryTransaction> InventoryTransactions { get; }
    DbSet<Coupon> Coupons { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs <paramref name="work"/> inside a database transaction (with the provider's retry strategy).</summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);
}

/// <summary>The authenticated caller of the current request.</summary>
public interface ICurrentUser
{
    int? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);

    /// <summary>Returns the user id or throws <see cref="Common.UnauthorizedException"/>.</summary>
    int RequireUserId();
}

public sealed record UserSummary(int Id, string FullName, string Email, string? PhoneNumber, bool IsActive, IReadOnlyList<string> Roles, DateTime CreatedAt);

/// <summary>Port to ASP.NET Core Identity (implemented in Infrastructure).</summary>
public interface IIdentityService
{
    Task<UserSummary> RegisterCustomerAsync(string fullName, string email, string phone, string password, CancellationToken ct);
    Task<UserSummary> SignInAsync(string email, string password, bool rememberMe, CancellationToken ct);
    Task SignOutAsync();
    Task<UserSummary?> GetUserAsync(int userId, CancellationToken ct);
    Task<UserSummary> UpdateProfileAsync(int userId, string fullName, string phone, CancellationToken ct);
    Task ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct);
    Task<Common.PagedResult<UserSummary>> ListUsersAsync(string? search, string? role, int page, int pageSize, CancellationToken ct);
    Task SetActiveAsync(int userId, bool isActive, CancellationToken ct);
    Task SetRoleAsync(int userId, string role, CancellationToken ct);
    Task<UserSummary> CreateStaffAsync(string fullName, string email, string phone, string password, string role, CancellationToken ct);
    Task<int> CountCustomersAsync(DateTime? createdFrom, CancellationToken ct);
}

/// <summary>Stores uploaded product images. Local disk today; could be swapped for S3/Azure Blob.</summary>
public interface IFileStorage
{
    Task<string> SaveImageAsync(Stream content, string extension, CancellationToken ct);
}

public sealed record PaymentGatewayRequest(string OrderNumber, decimal Amount, bool SimulateSuccess);
public sealed record PaymentGatewayResult(bool Succeeded, string? TransactionReference, string? FailureReason);

/// <summary>
/// Adapter pattern: the application talks to "a payment gateway" through this interface.
/// The included implementation is a simulator; a Razorpay/Stripe adapter would implement the same contract.
/// </summary>
public interface IPaymentGateway
{
    string Name { get; }
    Task<PaymentGatewayResult> ChargeAsync(PaymentGatewayRequest request, CancellationToken ct);
}

/// <summary>Observer pattern: handlers react to domain events after they are committed.</summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Kitchen = "Kitchen";
    public const string Delivery = "Delivery";
    public const string Customer = "Customer";

    public static readonly string[] All = [Admin, Kitchen, Delivery, Customer];
    public static readonly string[] Staff = [Admin, Kitchen, Delivery];

    /// <summary>Maps the caller's highest role to the actor used by the order state machine.</summary>
    public static OrderActor ToOrderActor(ICurrentUser user) =>
        user.IsInRole(Admin) ? OrderActor.Admin
        : user.IsInRole(Kitchen) ? OrderActor.Kitchen
        : user.IsInRole(Delivery) ? OrderActor.Delivery
        : OrderActor.Customer;
}
