using System.Text;
using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using BiteBox.Application.Features.Admin;
using BiteBox.Application.Features.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiteBox.API.Controllers;

// Every admin endpoint is protected on the server. Hiding menu items in Angular is only cosmetic.

[Route("api/v1/admin")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminDashboardController(ReportService reports) : ApiController
{
    /// <summary>Dashboard KPIs for a local-date range (defaults to the last 30 days).</summary>
    [HttpGet("dashboard")]
    public Task<DashboardDto> Dashboard([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        reports.DashboardAsync(from, to, ct);

    [HttpGet("reports/sales")]
    public Task<IReadOnlyList<DailySalesDto>> Sales([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        reports.SalesAsync(from, to, ct);

    [HttpGet("reports/products")]
    public Task<IReadOnlyList<TopProductDto>> ProductPerformance([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        reports.ProductPerformanceAsync(from, to, ct);

    [HttpGet("reports/sales.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> SalesCsv([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        File(Encoding.UTF8.GetBytes(await reports.SalesCsvAsync(from, to, ct)), "text/csv", "bitebox-sales.csv");

    [HttpGet("reports/products.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> ProductsCsv([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        File(Encoding.UTF8.GetBytes(await reports.ProductsCsvAsync(from, to, ct)), "text/csv", "bitebox-products.csv");
}

[Route("api/v1/admin/products")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminProductsController(AdminProductService products, IFileStorage storage) : ApiController
{
    [HttpGet]
    public Task<PagedResult<AdminProductDto>> List([FromQuery] AdminProductQuery query, CancellationToken ct) => products.ListAsync(query, ct);

    [HttpGet("{id:int}")]
    public Task<AdminProductDto> Get(int id, CancellationToken ct) => products.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<AdminProductDto>> Create(ProductUpsertRequest request, CancellationToken ct)
    {
        var product = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:int}")]
    public Task<AdminProductDto> Update(int id, ProductUpsertRequest request, CancellationToken ct) => products.UpdateAsync(id, request, ct);

    /// <summary>Soft delete: the dish disappears from the menu but order history is preserved.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Upload a JPG/PNG/WEBP image (max 2 MB). File signatures are verified server-side.</summary>
    [HttpPost("{id:int}/image")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<AdminProductDto> UploadImage(int id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var url = await storage.SaveImageAsync(stream, Path.GetExtension(file.FileName), ct);
        return await products.SetImageAsync(id, url, ct);
    }
}

[Route("api/v1/admin/categories")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminCategoriesController(AdminCategoryService categories) : ApiController
{
    [HttpGet]
    public Task<IReadOnlyList<AdminCategoryDto>> List(CancellationToken ct) => categories.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<AdminCategoryDto>> Create(CategoryUpsertRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await categories.CreateAsync(request, ct));

    [HttpPut("{id:int}")]
    public Task<AdminCategoryDto> Update(int id, CategoryUpsertRequest request, CancellationToken ct) => categories.UpdateAsync(id, request, ct);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }
}

[Route("api/v1/admin/inventory")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminInventoryController(InventoryService inventory) : ApiController
{
    [HttpGet]
    public Task<IReadOnlyList<InventoryItemDto>> List([FromQuery] bool lowStockOnly = false, CancellationToken ct = default) =>
        inventory.ListAsync(lowStockOnly, ct);

    [HttpPost("{productId:int}/adjust")]
    public Task<InventoryItemDto> Adjust(int productId, StockAdjustmentRequest request, CancellationToken ct) =>
        inventory.AdjustAsync(productId, request, ct);

    [HttpGet("transactions")]
    public Task<PagedResult<InventoryTransactionDto>> History([FromQuery] int? productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        inventory.HistoryAsync(productId, page, pageSize, ct);
}

[Route("api/v1/admin/coupons")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminCouponsController(CouponService coupons) : ApiController
{
    [HttpGet]
    public Task<IReadOnlyList<CouponDto>> List(CancellationToken ct) => coupons.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<CouponDto>> Create(CouponUpsertRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await coupons.CreateAsync(request, ct));

    [HttpPut("{id:int}")]
    public Task<CouponDto> Update(int id, CouponUpsertRequest request, CancellationToken ct) => coupons.UpdateAsync(id, request, ct);

    /// <summary>Deletes an unused coupon, or deactivates one that has been used.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await coupons.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Order operations for Admin, Kitchen and Delivery staff. Allowed transitions depend on role.</summary>
[Route("api/v1/admin/orders")]
[Authorize(Policy = Policies.Staff)]
public sealed class AdminOrdersController(AdminOrderService orders) : ApiController
{
    [HttpGet]
    public Task<PagedResult<OrderSummaryDto>> List([FromQuery] AdminOrderQuery query, CancellationToken ct) => orders.ListAsync(query, ct);

    [HttpGet("{orderNumber}")]
    public Task<OrderDetailDto> Get(string orderNumber, CancellationToken ct) => orders.GetAsync(orderNumber, ct);

    [HttpPost("{orderNumber}/status")]
    public Task<OrderDetailDto> ChangeStatus(string orderNumber, ChangeOrderStatusRequest request, CancellationToken ct) =>
        orders.ChangeStatusAsync(orderNumber, request, ct);

    [HttpPost("{orderNumber}/refund")]
    [Authorize(Policy = Policies.Admin)]
    public Task<OrderDetailDto> MarkRefunded(string orderNumber, CancellationToken ct) => orders.MarkRefundedAsync(orderNumber, ct);
}

[Route("api/v1/admin/users")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminUsersController(AdminUserService users) : ApiController
{
    [HttpGet]
    public Task<PagedResult<AdminUserDto>> List([FromQuery] string? search, [FromQuery] string? role, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        users.ListAsync(search, role, page, pageSize, ct);

    [HttpGet("{id:int}/orders")]
    public Task<PagedResult<OrderSummaryDto>> Orders(int id, CancellationToken ct) => users.OrdersOfAsync(id, ct);

    [HttpPost("staff")]
    public async Task<ActionResult<UserSummary>> CreateStaff(CreateStaffRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await users.CreateStaffAsync(request, ct));

    [HttpPut("{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, SetActiveRequest request, CancellationToken ct)
    {
        await users.SetActiveAsync(id, request.IsActive, ct);
        return NoContent();
    }

    [HttpPut("{id:int}/role")]
    public async Task<IActionResult> SetRole(int id, SetRoleRequest request, CancellationToken ct)
    {
        await users.SetRoleAsync(id, request.Role, ct);
        return NoContent();
    }
}
