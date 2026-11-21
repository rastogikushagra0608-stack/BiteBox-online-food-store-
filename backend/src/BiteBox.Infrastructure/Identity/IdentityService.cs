using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BiteBox.Infrastructure.Identity;

/// <summary>
/// Adapter from the application's <see cref="IIdentityService"/> port to ASP.NET Core Identity.
/// Identity handles password hashing (PBKDF2), lockout, security stamps and the auth cookie.
/// </summary>
public sealed class IdentityService(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    Persistence.BiteBoxDbContext db,
    TimeProvider clock) : IIdentityService
{
    public async Task<UserSummary> RegisterCustomerAsync(string fullName, string email, string phone, string password, CancellationToken ct)
    {
        if (await users.FindByEmailAsync(email) is not null)
            throw new ConflictException("auth.email_taken", "An account with this email already exists. Try signing in.");

        var user = await CreateAsync(fullName, email, phone, password, Roles.Customer);
        await signIn.SignInAsync(user, isPersistent: false);
        return await ToSummaryAsync(user);
    }

    public async Task<UserSummary> SignInAsync(string email, string password, bool rememberMe, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        // Same message for "no such user" and "wrong password" so attackers can't enumerate accounts.
        const string invalid = "Incorrect email or password.";
        if (user is null) throw new UnauthorizedException(invalid);
        if (!user.IsActive) throw new ForbiddenException("This account has been deactivated. Please contact support.");

        var result = await signIn.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut) throw new ForbiddenException("Too many failed attempts. Please try again in 5 minutes.");
        if (!result.Succeeded) throw new UnauthorizedException(invalid);
        return await ToSummaryAsync(user);
    }

    public Task SignOutAsync() => signIn.SignOutAsync();

    public async Task<UserSummary?> GetUserAsync(int userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToSummaryAsync(user);
    }

    public async Task<UserSummary> UpdateProfileAsync(int userId, string fullName, string phone, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User", userId);
        user.FullName = fullName.Trim();
        user.PhoneNumber = phone.Trim();
        EnsureSucceeded(await users.UpdateAsync(user));
        return await ToSummaryAsync(user);
    }

    public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User", userId);
        var result = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded && result.Errors.Any(e => e.Code == "PasswordMismatch"))
            throw new BadRequestException("auth.wrong_password", "Your current password is incorrect.");
        EnsureSucceeded(result);
        await signIn.RefreshSignInAsync(user); // security stamp changed; reissue cookie
    }

    public async Task<PagedResult<UserSummary>> ListUsersAsync(string? search, string? role, int page, int pageSize, CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize, 20);
        IQueryable<AppUser> query = users.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(role))
        {
            var inRole = (await users.GetUsersInRoleAsync(role)).Select(u => u.Id).ToList();
            query = query.Where(u => inRole.Contains(u.Id));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(u => EF.Functions.Like(u.FullName, pattern) || EF.Functions.Like(u.Email!, pattern) || EF.Functions.Like(u.PhoneNumber!, pattern));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(u => u.CreatedAt).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var summaries = new List<UserSummary>(rows.Count);
        foreach (var u in rows) summaries.Add(await ToSummaryAsync(u));
        return new PagedResult<UserSummary>(summaries, p, size, total);
    }

    public async Task SetActiveAsync(int userId, bool isActive, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User", userId);
        user.IsActive = isActive;
        EnsureSucceeded(await users.UpdateAsync(user));
        // Rotating the security stamp invalidates existing cookies on the next validation cycle.
        if (!isActive) await users.UpdateSecurityStampAsync(user);
    }

    public async Task SetRoleAsync(int userId, string role, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User", userId);
        var current = await users.GetRolesAsync(user);
        EnsureSucceeded(await users.RemoveFromRolesAsync(user, current));
        EnsureSucceeded(await users.AddToRoleAsync(user, role));
        await users.UpdateSecurityStampAsync(user);
    }

    public async Task<UserSummary> CreateStaffAsync(string fullName, string email, string phone, string password, string role, CancellationToken ct)
    {
        if (await users.FindByEmailAsync(email) is not null)
            throw new ConflictException("auth.email_taken", "An account with this email already exists.");
        return await ToSummaryAsync(await CreateAsync(fullName, email, phone, password, role));
    }

    public Task<int> CountCustomersAsync(DateTime? createdFrom, CancellationToken ct)
    {
        var customers =
            from u in db.Users
            join ur in db.UserRoles on u.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where r.Name == Roles.Customer
            select u;
        if (createdFrom is not null) customers = customers.Where(u => u.CreatedAt >= createdFrom);
        return customers.CountAsync(ct);
    }

    private async Task<AppUser> CreateAsync(string fullName, string email, string phone, string password, string role)
    {
        var user = new AppUser
        {
            UserName = email.Trim().ToLowerInvariant(),
            Email = email.Trim().ToLowerInvariant(),
            FullName = fullName.Trim(),
            PhoneNumber = phone.Trim(),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            IsActive = true
        };
        EnsureSucceeded(await users.CreateAsync(user, password));
        EnsureSucceeded(await users.AddToRoleAsync(user, role));
        return user;
    }

    private async Task<UserSummary> ToSummaryAsync(AppUser u) =>
        new(u.Id, u.FullName, u.Email ?? "", u.PhoneNumber, u.IsActive, (await users.GetRolesAsync(u)).ToList(), u.CreatedAt);

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new BadRequestException("identity." + (result.Errors.FirstOrDefault()?.Code ?? "error"),
                string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
