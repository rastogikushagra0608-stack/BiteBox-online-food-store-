using Microsoft.AspNetCore.Identity;

namespace BiteBox.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity user. Lives in Infrastructure so the Domain stays free of framework types;
/// domain entities reference users only by their integer id.
/// </summary>
public class AppUser : IdentityUser<int>
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class AppRole : IdentityRole<int>
{
    public AppRole() { }
    public AppRole(string name) : base(name) { }
}
