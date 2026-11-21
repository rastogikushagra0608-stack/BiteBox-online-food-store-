using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using BiteBox.Application.Features.Account;
using BiteBox.Application.Features.Admin;
using BiteBox.Application.Features.AI;
using BiteBox.Application.Features.Auth;
using BiteBox.Application.Features.Catalog;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BiteBox.API.Controllers;

/// <summary>Controllers are deliberately thin: HTTP in, call a use case, HTTP out. No business logic here.</summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiController : ControllerBase;

/// <summary>Registration, sign-in and session management.</summary>
[Route("api/v1/auth")]
public sealed class AuthController(IIdentityService identity, ICurrentUser currentUser, IAntiforgery antiforgery, IConfiguration config, IWebHostEnvironment env) : ApiController
{
    /// <summary>
    /// Issues an anti-forgery token. The token is returned in a JS-readable XSRF-TOKEN cookie which Angular's
    /// HttpClient echoes back as the X-XSRF-TOKEN header. Call on app start and after sign-in/sign-out.
    /// </summary>
    [HttpGet("csrf")]
    [IgnoreAntiforgeryToken]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, // must be readable by the SPA
            SameSite = SameSiteMode.Strict,
            Secure = config.GetValue("BiteBox:Security:SecureCookies", !env.IsDevelopment()),
            Path = "/"
        });
        return NoContent();
    }

    [HttpPost("register")]
    [EnableRateLimiting(RateLimits.Auth)]
    [ProducesResponseType<MeDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<MeDto>> Register(RegisterRequest request, CancellationToken ct)
    {
        var user = await identity.RegisterCustomerAsync(request.FullName, request.Email, request.Phone, request.Password, ct);
        return Created("/api/v1/auth/me", MeDto.From(user));
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<MeDto>> Login(LoginRequest request, CancellationToken ct) =>
        MeDto.From(await identity.SignInAsync(request.Email, request.Password, request.RememberMe, ct));

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await identity.SignOutAsync();
        return NoContent();
    }

    /// <summary>Current user, or 401 when signed out.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeDto>> Me(CancellationToken ct)
    {
        var user = await identity.GetUserAsync(currentUser.RequireUserId(), ct);
        if (user is null || !user.IsActive) return Unauthorized();
        return MeDto.From(user);
    }

    [HttpPut("me")]
    [Authorize]
    public async Task<ActionResult<MeDto>> UpdateProfile(UpdateProfileRequest request, CancellationToken ct) =>
        MeDto.From(await identity.UpdateProfileAsync(currentUser.RequireUserId(), request.FullName, request.Phone, ct));

    [HttpPost("me/password")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await identity.ChangePasswordAsync(currentUser.RequireUserId(), request.CurrentPassword, request.NewPassword, ct);
        return NoContent();
    }
}

/// <summary>Public menu browsing. No authentication required.</summary>
[Route("api/v1")]
public sealed class CatalogController(CatalogService catalog, ReviewService reviews, CouponService coupons) : ApiController
{
    [HttpGet("categories")]
    public Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken ct) => catalog.GetCategoriesAsync(ct);

    /// <summary>Search and filter the menu. Example: /products?search=paneer&amp;vegetarian=true&amp;maxPrice=300&amp;sort=Rating</summary>
    [HttpGet("products")]
    public Task<PagedResult<ProductCardDto>> Products([FromQuery] ProductQuery query, CancellationToken ct) =>
        catalog.SearchProductsAsync(query, ct);

    [HttpGet("products/{slug}")]
    public Task<ProductDetailDto> Product(string slug, CancellationToken ct) => catalog.GetProductAsync(slug, ct);

    [HttpGet("products/{slug}/related")]
    public Task<IReadOnlyList<ProductCardDto>> Related(string slug, CancellationToken ct) => catalog.GetRelatedAsync(slug, ct);

    [HttpGet("products/{productId:int}/reviews")]
    public Task<ReviewSummaryDto> Reviews(int productId, CancellationToken ct) => reviews.ListAsync(productId, ct);

    /// <summary>Only customers who received the dish can review it (enforced server-side).</summary>
    [HttpPost("products/{productId:int}/reviews")]
    [Authorize]
    public async Task<ActionResult<ReviewDto>> AddReview(int productId, CreateReviewRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await reviews.CreateAsync(productId, request, ct));

    [HttpGet("promotions")]
    public Task<IReadOnlyList<PromoDto>> Promotions(CancellationToken ct) => coupons.ListPublicAsync(ct);
}

/// <summary>BiteBot: product-grounded food recommendations from natural language.</summary>
[Route("api/v1/ai")]
public sealed class BiteBotController(IBiteBotService biteBot) : ApiController
{
    [HttpPost("bitebot")]
    [EnableRateLimiting(RateLimits.Ai)]
    public Task<BiteBotResponse> Ask(BiteBotRequest request, CancellationToken ct) => biteBot.AskAsync(request, ct);
}
