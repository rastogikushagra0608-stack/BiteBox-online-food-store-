using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BiteBox.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;

namespace BiteBox.Tests.Integration;

/// <summary>
/// Boots the real API in memory against a real MySQL database (bitebox_test), recreated once per test run.
/// Connection string: BITEBOX_TEST_CONNECTION env var, otherwise the API's user-secret with the database
/// name swapped to bitebox_test.
/// </summary>
public sealed class BiteBoxApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DemoPassword = "BiteBox@2026";
    private static readonly Lazy<string?> ConnectionString = new(ResolveConnectionString);

    public static bool DatabaseAvailable => ConnectionString.Value is not null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BiteBox"] = ConnectionString.Value,
            ["BiteBox:Database:MigrateOnStartup"] = "true",
            ["BiteBox:Seed:Enabled"] = "true",
            ["BiteBox:Seed:DemoPassword"] = DemoPassword,
            ["BiteBox:AI:Provider"] = "Rules"
        }));
    }

    public async Task InitializeAsync()
    {
        if (!DatabaseAvailable) return;
        // Program.cs reads configuration before Build(), which WebApplicationFactory's ConfigureAppConfiguration
        // cannot influence. Environment variables are read by CreateBuilder itself, so they always win.
        Environment.SetEnvironmentVariable("ConnectionStrings__BiteBox", ConnectionString.Value);
        Environment.SetEnvironmentVariable("BiteBox__Database__MigrateOnStartup", "true");
        Environment.SetEnvironmentVariable("BiteBox__Seed__Enabled", "true");
        Environment.SetEnvironmentVariable("BiteBox__Seed__DemoPassword", DemoPassword);
        Environment.SetEnvironmentVariable("BiteBox__AI__Provider", "Rules");
        Environment.SetEnvironmentVariable("BiteBox__RateLimits__AuthPerMinute", "1000");
        Environment.SetEnvironmentVariable("BiteBox__RateLimits__CheckoutPerMinute", "1000");
        // Start from an empty database so tests are repeatable.
        var options = new DbContextOptionsBuilder<BiteBoxDbContext>().UseMySQL(ConnectionString.Value!).Options;
        await using (var db = new BiteBoxDbContext(options, new ServiceCollection().BuildServiceProvider(), TimeProvider.System))
            await db.Database.EnsureDeletedAsync();
        _ = Services; // forces host start → migrate + seed
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    public async Task<ApiClient> ClientAsync(string? email = null)
    {
        var client = new ApiClient(CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }));
        await client.RefreshCsrfAsync();
        if (email is not null) await client.LoginAsync(email, DemoPassword);
        return client;
    }

    private static string? ResolveConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("BITEBOX_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
        {
            var main = new ConfigurationBuilder().AddUserSecrets<Program>().Build().GetConnectionString("BiteBox");
            if (string.IsNullOrWhiteSpace(main)) return null;
            cs = new MySqlConnectionStringBuilder(main) { Database = "bitebox_test" }.ConnectionString;
        }

        try
        {
            using var conn = new MySqlConnection(new MySqlConnectionStringBuilder(cs) { Database = "", ConnectionTimeout = 3 }.ConnectionString);
            conn.Open();
            return cs;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Skips (rather than fails) database tests on machines without the test database.</summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (!BiteBoxApiFactory.DatabaseAvailable)
            Skip = "MySQL test database not reachable. Set BITEBOX_TEST_CONNECTION or the API user-secret (see README).";
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<BiteBoxApiFactory>
{
    public const string Name = "api";
}

/// <summary>Thin HttpClient wrapper that behaves like the Angular app: cookies + X-XSRF-TOKEN header.</summary>
public sealed class ApiClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public HttpClient Http => http;

    public async Task RefreshCsrfAsync()
    {
        var response = await http.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("XSRF-TOKEN="));
        var token = WebUtility.UrlDecode(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
        http.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        http.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
    }

    public async Task LoginAsync(string email, string password)
    {
        var response = await http.PostAsJsonAsync("/api/v1/auth/login", new { email, password, rememberMe = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await RefreshCsrfAsync(); // antiforgery tokens are bound to the signed-in identity
    }

    public Task<HttpResponseMessage> PostAsync(string url, object body) => http.PostAsJsonAsync(url, body, Json);

    public async Task<T> GetAsync<T>(string url)
    {
        var response = await http.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"GET {url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>(Json))!;
}
