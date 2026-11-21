using BiteBox.Application.Abstractions;

namespace BiteBox.Application.Features.Auth;

public sealed record RegisterRequest(string FullName, string Email, string Phone, string Password);
public sealed record LoginRequest(string Email, string Password, bool RememberMe);
public sealed record UpdateProfileRequest(string FullName, string Phone);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record MeDto(int Id, string FullName, string Email, string? Phone, IReadOnlyList<string> Roles)
{
    public static MeDto From(UserSummary u) => new(u.Id, u.FullName, u.Email, u.PhoneNumber, u.Roles);
}
