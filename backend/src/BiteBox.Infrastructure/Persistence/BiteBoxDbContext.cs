using BiteBox.Application.Abstractions;
using BiteBox.Domain.Common;
using BiteBox.Domain.Entities;
using BiteBox.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BiteBox.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for MySQL. Besides mapping, it does two cross-cutting jobs on SaveChanges:
///  1. stamps CreatedAt/UpdatedAt on auditable entities;
///  2. dispatches domain events to their handlers after the data is saved (Observer pattern).
/// </summary>
public class BiteBoxDbContext(DbContextOptions<BiteBoxDbContext> options, IServiceProvider services, TimeProvider clock)
    : IdentityDbContext<AppUser, AppRole, int>(options), IApplicationDbContext
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(BiteBoxDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money everywhere is DECIMAL(10,2); never float/double.
        configurationBuilder.Properties<decimal>().HavePrecision(10, 2);
        // Persist enums as readable strings ("OutForDelivery") rather than magic numbers.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(30);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();

        var entitiesWithEvents = ChangeTracker.Entries<Entity>()
            .Select(e => e.Entity).Where(e => e.DomainEvents.Count > 0).ToList();
        var events = entitiesWithEvents.SelectMany(e => e.DomainEvents).ToList();
        entitiesWithEvents.ForEach(e => e.ClearDomainEvents());

        var result = await base.SaveChangesAsync(cancellationToken);

        if (events.Count > 0)
        {
            foreach (var domainEvent in events) await DispatchAsync(domainEvent, cancellationToken);
            StampAuditFields();
            result += await base.SaveChangesAsync(cancellationToken); // persist side effects (e.g. notifications)
        }

        return result;
    }

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is not null) return await work(cancellationToken); // already inside one

        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            await using var tx = await Database.BeginTransactionAsync(ct);
            var result = await work(ct);
            await tx.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }

    private async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        foreach (var handler in services.GetServices(handlerType))
        {
            await (Task)handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!
                .Invoke(handler, [domainEvent, ct])!;
        }
    }

    private void StampAuditFields()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
