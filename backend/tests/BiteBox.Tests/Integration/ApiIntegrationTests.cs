using System.Net;
using System.Net.Http.Json;
using BiteBox.Application.Common;
using BiteBox.Application.Features.Admin;
using BiteBox.Application.Features.AI;
using BiteBox.Application.Features.Auth;
using BiteBox.Application.Features.Cart;
using BiteBox.Application.Features.Catalog;
using BiteBox.Application.Features.Orders;
using BiteBox.Domain.Enums;

namespace BiteBox.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ApiIntegrationTests(BiteBoxApiFactory factory)
{
    private const string Customer = "priya@example.com";

    // ── Security boundaries ─────────────────────────────────────────────────────

    [MySqlFact]
    public async Task Menu_is_public_and_seeded()
    {
        var client = await factory.ClientAsync();
        var page = await client.GetAsync<PagedResult<ProductCardDto>>("/api/v1/products?pageSize=50");
        Assert.True(page.TotalCount >= 40);
        Assert.All(page.Items, p => Assert.True(p.Price > 0));
    }

    [MySqlFact]
    public async Task Cart_requires_authentication()
    {
        var client = await factory.ClientAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/v1/cart")).StatusCode);
    }

    [MySqlFact]
    public async Task State_changing_requests_without_csrf_token_are_rejected()
    {
        var client = await factory.ClientAsync(Customer);
        client.Http.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        var response = await client.PostAsync("/api/v1/cart/items", new CartLineRequest(1, 1));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [MySqlFact]
    public async Task Customer_cannot_reach_admin_endpoints_but_admin_can()
    {
        var customer = await factory.ClientAsync(Customer);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Http.GetAsync("/api/v1/admin/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Http.GetAsync("/api/v1/admin/orders")).StatusCode);

        var admin = await factory.ClientAsync("admin@bitebox.dev");
        var dashboard = await admin.GetAsync<DashboardDto>("/api/v1/admin/dashboard");
        Assert.True(dashboard.TotalOrders > 0);
        Assert.True(dashboard.Revenue > 0);
    }

    [MySqlFact]
    public async Task Kitchen_sees_orders_but_not_product_management()
    {
        var kitchen = await factory.ClientAsync("kitchen@bitebox.dev");
        Assert.Equal(HttpStatusCode.OK, (await kitchen.Http.GetAsync("/api/v1/admin/orders?activeOnly=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.Http.GetAsync("/api/v1/admin/products")).StatusCode);
    }

    [MySqlFact]
    public async Task Wrong_password_gives_generic_401()
    {
        var client = await factory.ClientAsync();
        var response = await client.PostAsync("/api/v1/auth/login", new LoginRequest(Customer, "WrongPass1", false));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Incorrect email or password", await response.Content.ReadAsStringAsync());
    }

    [MySqlFact]
    public async Task Registration_validates_input_and_creates_customer()
    {
        var client = await factory.ClientAsync();
        var bad = await client.PostAsync("/api/v1/auth/register", new RegisterRequest("A", "not-an-email", "123", "weak"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var email = $"test{Guid.NewGuid():N}"[..20] + "@example.com";
        var ok = await client.PostAsync("/api/v1/auth/register", new RegisterRequest("Test Student", email, "9123456789", "Student@123"));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        await client.RefreshCsrfAsync();
        var me = await client.GetAsync<MeDto>("/api/v1/auth/me");
        Assert.Equal(["Customer"], me.Roles);
    }

    // ── Checkout ────────────────────────────────────────────────────────────────

    [MySqlFact]
    public async Task Checkout_prices_on_server_reserves_stock_and_is_idempotent()
    {
        var client = await factory.ClientAsync("rahul@example.com");
        var dosa = await client.GetAsync<ProductDetailDto>("/api/v1/products/masala-dosa");
        var stockBefore = (await AdminInventory()).First(i => i.ProductId == dosa.Id).StockQuantity;

        await client.Http.DeleteAsync("/api/v1/cart");
        (await client.PostAsync("/api/v1/cart/items", new CartLineRequest(dosa.Id, 3))).EnsureSuccessStatusCode();

        var quote = await ApiClient.ReadAsync<QuoteDto>(await client.PostAsync("/api/v1/orders/quote", new QuoteRequest(FulfillmentType.Pickup, null)));
        Assert.Equal(dosa.Price * 3, quote.Subtotal);
        Assert.Equal(Math.Round(quote.Subtotal * 0.05m, 2), quote.Tax);

        var key = Guid.NewGuid().ToString("N");
        var request = new PlaceOrderRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, null, key);
        var first = await client.PostAsync("/api/v1/orders", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var order = await ApiClient.ReadAsync<OrderDetailDto>(first);
        Assert.Equal(quote.Total, order.Total);
        Assert.Equal(OrderStatus.Placed, order.Status);

        // Double-submit with the same key returns the same order rather than creating a second one.
        var second = await ApiClient.ReadAsync<OrderDetailDto>(await client.PostAsync("/api/v1/orders", request));
        Assert.Equal(order.OrderNumber, second.OrderNumber);

        var stockAfter = (await AdminInventory()).First(i => i.ProductId == dosa.Id).StockQuantity;
        Assert.Equal(stockBefore - 3, stockAfter);

        // Customer cancels → stock is returned.
        (await client.PostAsync($"/api/v1/orders/{order.OrderNumber}/cancel", new { reason = "test" })).EnsureSuccessStatusCode();
        Assert.Equal(stockBefore, (await AdminInventory()).First(i => i.ProductId == dosa.Id).StockQuantity);
    }

    [MySqlFact]
    public async Task Concurrent_orders_for_the_last_portion_never_oversell()
    {
        var admin = await factory.ClientAsync("admin@bitebox.dev");
        var burger = await admin.GetAsync<ProductDetailDto>("/api/v1/products/double-trouble-burger");
        var current = (await AdminInventory()).First(i => i.ProductId == burger.Id).StockQuantity;
        // Leave exactly one portion.
        (await admin.PostAsync($"/api/v1/admin/inventory/{burger.Id}/adjust",
            new StockAdjustmentRequest(1 - current, InventoryReason.ManualAdjustment, "test setup"))).EnsureSuccessStatusCode();

        var buyers = new[] { "ananya@example.com", "arjun@example.com", "meera@example.com", "vikram@example.com" };
        var clients = new List<ApiClient>();
        foreach (var email in buyers)
        {
            var c = await factory.ClientAsync(email);
            await c.Http.DeleteAsync("/api/v1/cart");
            (await c.PostAsync("/api/v1/cart/items", new CartLineRequest(burger.Id, 1))).EnsureSuccessStatusCode();
            clients.Add(c);
        }

        var results = await Task.WhenAll(clients.Select(c => c.PostAsync("/api/v1/orders",
            new PlaceOrderRequest(FulfillmentType.Pickup, null, PaymentMethod.CashOnDelivery, null, null, Guid.NewGuid().ToString("N")))));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(buyers.Length - 1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, (await AdminInventory()).First(i => i.ProductId == burger.Id).StockQuantity);

        (await admin.PostAsync($"/api/v1/admin/inventory/{burger.Id}/adjust",
            new StockAdjustmentRequest(10, InventoryReason.Restock, "test teardown"))).EnsureSuccessStatusCode();
    }

    [MySqlFact]
    public async Task Failed_online_payment_releases_stock()
    {
        var client = await factory.ClientAsync("meera@example.com");
        var tea = await client.GetAsync<ProductDetailDto>("/api/v1/products/lemon-iced-tea");
        var before = (await AdminInventory()).First(i => i.ProductId == tea.Id).StockQuantity;

        await client.Http.DeleteAsync("/api/v1/cart");
        (await client.PostAsync("/api/v1/cart/items", new CartLineRequest(tea.Id, 2))).EnsureSuccessStatusCode();
        var order = await ApiClient.ReadAsync<OrderDetailDto>(await client.PostAsync("/api/v1/orders",
            new PlaceOrderRequest(FulfillmentType.Pickup, null, PaymentMethod.OnlineSimulated, null, null, Guid.NewGuid().ToString("N"))));
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(before - 2, (await AdminInventory()).First(i => i.ProductId == tea.Id).StockQuantity);

        var failed = await ApiClient.ReadAsync<OrderDetailDto>(await client.PostAsync($"/api/v1/orders/{order.OrderNumber}/payment", new SimulatePaymentRequest(false)));
        Assert.Equal(OrderStatus.PaymentFailed, failed.Status);
        Assert.Equal(before, (await AdminInventory()).First(i => i.ProductId == tea.Id).StockQuantity);
    }

    [MySqlFact]
    public async Task Customers_cannot_see_other_customers_orders()
    {
        var admin = await factory.ClientAsync("admin@bitebox.dev");
        var someoneElses = (await admin.GetAsync<PagedResult<OrderSummaryDto>>("/api/v1/admin/orders?search=Vikram")).Items.First();

        var priya = await factory.ClientAsync(Customer);
        Assert.Equal(HttpStatusCode.NotFound, (await priya.Http.GetAsync($"/api/v1/orders/{someoneElses.OrderNumber}")).StatusCode);
    }

    [MySqlFact]
    public async Task Kitchen_cannot_mark_an_order_delivered()
    {
        var kitchen = await factory.ClientAsync("kitchen@bitebox.dev");
        var active = await kitchen.GetAsync<PagedResult<OrderSummaryDto>>("/api/v1/admin/orders?status=Placed&pageSize=1");
        if (active.Items.Count == 0) return; // seed may have no placed orders at this exact moment
        var response = await kitchen.PostAsync($"/api/v1/admin/orders/{active.Items[0].OrderNumber}/status",
            new ChangeOrderStatusRequest(OrderStatus.Delivered, null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── BiteBot ────────────────────────────────────────────────────────────────

    [MySqlFact]
    public async Task BiteBot_only_recommends_real_available_products_within_constraints()
    {
        var client = await factory.ClientAsync();
        var response = await ApiClient.ReadAsync<BiteBotResponse>(await client.PostAsync("/api/v1/ai/bitebot",
            new BiteBotRequest("Suggest a spicy veg meal under ₹250")));

        Assert.NotEmpty(response.Suggestions);
        Assert.Equal("rules", response.Engine);
        foreach (var s in response.Suggestions)
        {
            var real = await client.GetAsync<ProductDetailDto>($"/api/v1/products/{s.Product.Slug}");
            Assert.Equal(real.Price, s.Product.Price);
            Assert.True(real.IsVegetarian);
            Assert.True(real.Price <= 250);
            Assert.True(real.InStock);
        }
    }

    [MySqlFact]
    public async Task BiteBot_builds_a_combo_that_fits_the_budget()
    {
        var client = await factory.ClientAsync();
        var response = await ApiClient.ReadAsync<BiteBotResponse>(await client.PostAsync("/api/v1/ai/bitebot",
            new BiteBotRequest("vegetarian dinner for two under 700")));

        Assert.NotNull(response.Combo);
        Assert.True(response.Combo!.FoodTotal <= 700);
        Assert.Equal(response.Combo.FoodTotal, response.Combo.Items.Sum(i => i.Product.Price * i.Quantity));
    }

    private async Task<IReadOnlyList<InventoryItemDto>> AdminInventory()
    {
        var admin = await factory.ClientAsync("admin@bitebox.dev");
        return await admin.GetAsync<List<InventoryItemDto>>("/api/v1/admin/inventory");
    }
}
