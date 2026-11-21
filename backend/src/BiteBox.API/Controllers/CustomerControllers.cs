using BiteBox.Application.Common;
using BiteBox.Application.Features.Account;
using BiteBox.Application.Features.Cart;
using BiteBox.Application.Features.Catalog;
using BiteBox.Application.Features.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BiteBox.API.Controllers;

[Route("api/v1/cart")]
[Authorize]
public sealed class CartController(CartService cart) : ApiController
{
    [HttpGet]
    public Task<CartDto> Get(CancellationToken ct) => cart.GetAsync(ct);

    [HttpPost("items")]
    public Task<CartDto> Add(CartLineRequest request, CancellationToken ct) => cart.AddAsync(request, ct);

    public sealed record QuantityRequest(int Quantity);

    /// <summary>Sets the quantity; 0 removes the line.</summary>
    [HttpPut("items/{productId:int}")]
    public Task<CartDto> SetQuantity(int productId, QuantityRequest request, CancellationToken ct) =>
        cart.SetQuantityAsync(productId, Math.Clamp(request.Quantity, 0, 20), ct);

    [HttpDelete("items/{productId:int}")]
    public Task<CartDto> Remove(int productId, CancellationToken ct) => cart.RemoveAsync(productId, ct);

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await cart.ClearAsync(ct);
        return NoContent();
    }

    /// <summary>Merges a guest (browser-stored) cart after sign-in.</summary>
    [HttpPost("merge")]
    public Task<CartDto> Merge(List<CartLineRequest> lines, CancellationToken ct) => cart.MergeAsync(lines.Take(50).ToList(), ct);
}

[Route("api/v1/orders")]
[Authorize]
public sealed class OrdersController(OrderService orders, PaymentService payments) : ApiController
{
    /// <summary>Server-calculated price breakdown for the current cart (used by checkout).</summary>
    [HttpPost("quote")]
    public Task<QuoteDto> Quote(QuoteRequest request, CancellationToken ct) => orders.QuoteAsync(request, ct);

    /// <summary>Places an order from the cart. Send a unique idempotencyKey per checkout attempt.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimits.Checkout)]
    public async Task<ActionResult<OrderDetailDto>> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        var order = await orders.PlaceOrderAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { orderNumber = order.OrderNumber }, order);
    }

    [HttpGet]
    public Task<PagedResult<OrderSummaryDto>> Mine([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default) =>
        orders.ListMineAsync(page, pageSize, ct);

    [HttpGet("{orderNumber}")]
    public Task<OrderDetailDto> Get(string orderNumber, CancellationToken ct) => orders.GetMineAsync(orderNumber, ct);

    public sealed record CancelRequest(string? Reason);

    [HttpPost("{orderNumber}/cancel")]
    public Task<OrderDetailDto> Cancel(string orderNumber, CancelRequest request, CancellationToken ct) =>
        orders.CancelMineAsync(orderNumber, request.Reason?[..Math.Min(request.Reason.Length, 200)], ct);

    [HttpPost("{orderNumber}/reorder")]
    public async Task<IActionResult> Reorder(string orderNumber, CancellationToken ct) =>
        Ok(new { added = await orders.ReorderAsync(orderNumber, ct) });

    /// <summary>Completes a simulated online payment (success or failure) for an order awaiting payment.</summary>
    [HttpPost("{orderNumber}/payment")]
    [EnableRateLimiting(RateLimits.Checkout)]
    public Task<OrderDetailDto> Pay(string orderNumber, SimulatePaymentRequest request, CancellationToken ct) =>
        payments.PayAsync(orderNumber, request, ct);
}

[Route("api/v1/account")]
[Authorize]
public sealed class AccountController(
    AddressService addresses, FavoriteService favorites, NotificationService notifications, ReviewService reviews) : ApiController
{
    [HttpGet("addresses")]
    public Task<IReadOnlyList<AddressDto>> Addresses(CancellationToken ct) => addresses.ListAsync(ct);

    [HttpPost("addresses")]
    public async Task<ActionResult<AddressDto>> CreateAddress(AddressRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await addresses.CreateAsync(request, ct));

    [HttpPut("addresses/{id:int}")]
    public Task<AddressDto> UpdateAddress(int id, AddressRequest request, CancellationToken ct) => addresses.UpdateAsync(id, request, ct);

    [HttpDelete("addresses/{id:int}")]
    public async Task<IActionResult> DeleteAddress(int id, CancellationToken ct)
    {
        await addresses.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("favorites")]
    public Task<IReadOnlyList<ProductCardDto>> Favorites(CancellationToken ct) => favorites.ListAsync(ct);

    [HttpGet("favorites/ids")]
    public Task<IReadOnlyList<int>> FavoriteIds(CancellationToken ct) => favorites.ListIdsAsync(ct);

    [HttpPut("favorites/{productId:int}")]
    public async Task<IActionResult> AddFavorite(int productId, CancellationToken ct)
    {
        await favorites.AddAsync(productId, ct);
        return NoContent();
    }

    [HttpDelete("favorites/{productId:int}")]
    public async Task<IActionResult> RemoveFavorite(int productId, CancellationToken ct)
    {
        await favorites.RemoveAsync(productId, ct);
        return NoContent();
    }

    [HttpGet("notifications")]
    public Task<NotificationFeedDto> Notifications(CancellationToken ct) => notifications.ListAsync(ct);

    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }

    [HttpGet("reviews")]
    public Task<IReadOnlyList<ReviewDto>> MyReviews(CancellationToken ct) => reviews.ListMineAsync(ct);
}
