using BiteBox.Application.Abstractions;
using BiteBox.Domain.Entities;
using BiteBox.Domain.Enums;
using BiteBox.Domain.Pricing;
using BiteBox.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BiteBox.Infrastructure.Persistence;

/// <summary>
/// Applies migrations and loads DEMO data for local development and the college demonstration.
/// All people are fictional and use the reserved example.com domain; all orders are generated.
/// Runs only when BiteBox:Seed:Enabled is true and the database has no categories yet.
/// </summary>
public sealed class DatabaseSeeder(
    BiteBoxDbContext db,
    UserManager<AppUser> users,
    RoleManager<AppRole> roles,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<DatabaseSeeder> logger)
{
    private static string Img(string id) => $"https://images.unsplash.com/{id}?auto=format&fit=crop&w=800&q=75";

    public async Task MigrateAndSeedAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new AppRole(role));

        if (!configuration.GetValue<bool>("BiteBox:Seed:Enabled")) return;
        if (await db.Categories.AnyAsync(ct)) return;

        var password = configuration["BiteBox:Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Seeding skipped: BiteBox:Seed:DemoPassword is not configured.");
            return;
        }

        logger.LogInformation("Seeding BiteBox demo data…");
        var now = clock.GetUtcNow().UtcDateTime;

        await CreateUserAsync("BiteBox Admin", "admin@bitebox.dev", "9000000001", password, Roles.Admin, now.AddDays(-60));
        await CreateUserAsync("Kitchen Team", "kitchen@bitebox.dev", "9000000002", password, Roles.Kitchen, now.AddDays(-60));
        await CreateUserAsync("Delivery Partner", "delivery@bitebox.dev", "9000000003", password, Roles.Delivery, now.AddDays(-60));

        var customers = new List<AppUser>();
        (string Name, string Email, string Phone)[] people =
        [
            ("Priya Sharma", "priya@example.com", "9876500001"),
            ("Rahul Verma", "rahul@example.com", "9876500002"),
            ("Ananya Iyer", "ananya@example.com", "9876500003"),
            ("Arjun Mehta", "arjun@example.com", "9876500004"),
            ("Meera Nair", "meera@example.com", "9876500005"),
            ("Vikram Singh", "vikram@example.com", "9876500006"),
        ];
        var joined = 50;
        foreach (var p in people)
            customers.Add(await CreateUserAsync(p.Name, p.Email, p.Phone, password, Roles.Customer, now.AddDays(-(joined -= 6))));

        var categories = SeedCategories();
        db.Categories.AddRange(categories.Values);
        await db.SaveChangesAsync(ct);

        var products = SeedProducts(categories);
        db.Products.AddRange(products);
        await db.SaveChangesAsync(ct);

        foreach (var p in products)
        {
            db.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = p.Id, QuantityChange = p.StockQuantity, StockAfter = p.StockQuantity,
                Reason = InventoryReason.InitialStock, Note = "Opening stock", CreatedAt = now.AddDays(-45)
            });
        }

        db.Coupons.AddRange(
            new Coupon { Code = "WELCOME50", Description = "₹50 off your first order above ₹299", DiscountType = DiscountType.Flat, Value = 50, MinOrderAmount = 299, PerUserLimit = 1, StartsAt = now.AddDays(-60), EndsAt = now.AddDays(90), IsActive = true },
            new Coupon { Code = "BITE20", Description = "20% off up to ₹120 on orders above ₹399", DiscountType = DiscountType.Percentage, Value = 20, MaxDiscountAmount = 120, MinOrderAmount = 399, PerUserLimit = 3, UsageLimit = 500, StartsAt = now.AddDays(-30), EndsAt = now.AddDays(60), IsActive = true },
            new Coupon { Code = "WEEKEND15", Description = "15% off up to ₹100 (weekend special)", DiscountType = DiscountType.Percentage, Value = 15, MaxDiscountAmount = 100, MinOrderAmount = 249, PerUserLimit = 2, StartsAt = now.AddDays(-90), EndsAt = now.AddDays(-5), IsActive = true });

        var addresses = customers.Select((c, i) => new Address
        {
            UserId = c.Id, Label = "Home", RecipientName = c.FullName, Phone = c.PhoneNumber!,
            Line1 = $"{12 + i * 7}, {new[] { "MG Road", "Indiranagar 2nd Stage", "Koramangala 5th Block", "HSR Layout Sector 3", "Jayanagar 4th Block", "Whitefield Main Road" }[i]}",
            City = "Bengaluru", Pincode = $"5600{30 + i:00}", IsDefault = true
        }).ToList();
        db.Addresses.AddRange(addresses);
        await db.SaveChangesAsync(ct);

        await SeedOrderHistoryAsync(customers, addresses, products, now, ct);
        logger.LogInformation("Demo data ready: {Products} dishes, {Customers} customers.", products.Count, customers.Count);
    }

    private async Task<AppUser> CreateUserAsync(string name, string email, string phone, string password, string role, DateTime createdAt)
    {
        var user = new AppUser { UserName = email, Email = email, FullName = name, PhoneNumber = phone, EmailConfirmed = true, CreatedAt = createdAt, IsActive = true };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) throw new InvalidOperationException($"Seeding user {email} failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        await users.AddToRoleAsync(user, role);
        return user;
    }

    private static Dictionary<string, Category> SeedCategories()
    {
        Category C(string name, string slug, string description, string image, int order) =>
            new() { Name = name, Slug = slug, Description = description, ImageUrl = Img(image), DisplayOrder = order, IsActive = true };

        return new Dictionary<string, Category>
        {
            ["starters"] = C("Starters & Street Food", "starters", "Crispy, tangy and made for sharing.", "photo-1601050690597-df0568f70950", 1),
            ["curries"] = C("Curries & Thalis", "curries", "Slow-cooked gravies, served with love.", "photo-1631452180519-c014fe946bc7", 2),
            ["biryani-rice"] = C("Biryani", "biryani-rice", "Fragrant dum biryanis, sealed and slow-cooked.", "photo-1589302168068-964664d93dc0", 3),
            ["south-indian"] = C("South Indian", "south-indian", "Dosas, idlis and filter-coffee mornings.", "photo-1668236543090-82eba5ee5976", 4),
            ["chinese"] = C("Chinese & Momos", "chinese", "Wok-tossed Indo-Chinese favourites.", "photo-1585032226651-759b368d7246", 5),
            ["pizzas-pasta"] = C("Pizza & Pasta", "pizzas-pasta", "Stone-baked pizzas and saucy pastas.", "photo-1574071318508-1cdbab80d002", 6),
            ["burgers"] = C("Burgers & Sandwiches", "burgers", "Stacked high, grilled right.", "photo-1568901346375-23c9450c58cd", 7),
            ["bowls-salads"] = C("Bowls & Salads", "bowls-salads", "Fresh, balanced and protein-packed.", "photo-1512621776951-a57141f2eefd", 8),
            ["desserts"] = C("Desserts", "desserts", "Because every meal deserves a sweet ending.", "photo-1578985545062-69928b1d9587", 9),
            ["beverages"] = C("Beverages", "beverages", "Shakes, coolers and coffee.", "photo-1572490122747-3968b75cc699", 10),
        };
    }

    private static List<Product> SeedProducts(Dictionary<string, Category> c)
    {
        var list = new List<Product>();
        void P(string cat, string name, string desc, decimal price, string image, bool veg, SpiceLevel spice, string tags,
            int stock = 60, int prep = 15, int serves = 1, int? kcal = null, bool featured = false)
        {
            var slug = BiteBox.Application.Common.Slug.From(name);
            list.Add(new Product(c[cat].Id, name, slug, desc, price, stock)
            {
                ImageUrl = Img(image), IsVegetarian = veg, SpiceLevel = spice, Tags = tags, PrepTimeMinutes = prep,
                ServesCount = serves, Calories = kcal, IsFeatured = featured, IsAvailable = true, LowStockThreshold = 10
            });
        }

        // Starters & street food
        P("starters", "Punjabi Samosa (2 pcs)", "Flaky pastry stuffed with spiced potato and peas, with mint and tamarind chutneys.", 79, "photo-1601050690597-df0568f70950", true, SpiceLevel.Medium, "snack,fried,street-food,tea-time", 80, 10, 1, 320);
        P("starters", "Mumbai Pav Bhaji", "Buttery mashed-vegetable bhaji with two toasted pav, onions and lemon.", 159, "photo-1606491956689-2ea866880c84", true, SpiceLevel.Medium, "street-food,buttery,snack,dinner", 50, 15, 1, 540, true);
        P("starters", "Chicken Seekh Kebab", "Char-grilled minced chicken skewers with onion rings and green chutney.", 279, "photo-1599487488170-d11ec9c172f0", false, SpiceLevel.Medium, "grilled,protein,tandoor,party", 40, 20, 1, 380);
        P("starters", "Tandoori Grill Platter", "Chicken tikka, malai tikka and seekh kebab with grilled potatoes and dips. Built for sharing.", 449, "photo-1555939594-58d7cb561ad1", false, SpiceLevel.Hot, "grilled,protein,tandoor,party,sharing", 25, 25, 2, 880, true);
        P("starters", "Bombay Masala Grilled Sandwich", "Triple-decker with spiced potato, veggies and cheese, grilled crisp.", 149, "photo-1528735602780-2552fd46c7af", true, SpiceLevel.Mild, "snack,cheesy,street-food,breakfast", 60, 12, 1, 450);

        // Curries & thalis
        P("curries", "Paneer Butter Masala", "Soft paneer in a velvety tomato-cashew gravy finished with butter and kasuri methi.", 269, "photo-1631452180519-c014fe946bc7", true, SpiceLevel.Mild, "paneer,creamy,dinner,lunch,north-indian", 45, 20, 1, 620, true);
        P("curries", "Chicken Tikka Masala", "Smoky tandoori chicken tikka simmered in a spiced onion-tomato masala.", 319, "photo-1565557623262-b51c2513a641", false, SpiceLevel.Medium, "chicken,protein,dinner,north-indian", 45, 22, 1, 680, true);
        P("curries", "Paneer Makhani Rice Bowl", "Paneer makhani over steamed basmati with cucumber. A complete meal in a bowl.", 229, "photo-1588166524941-3bf61a9c41db", true, SpiceLevel.Mild, "paneer,lunch,rice,comfort", 40, 15, 1, 650);
        P("curries", "BiteBox Veg Thali", "Dal, two sabzis, paneer curry, rice, two rotis, salad and a sweet. The full feast.", 299, "photo-1626777552726-4a6b54c97e46", true, SpiceLevel.Medium, "thali,lunch,dinner,value,wholesome", 30, 25, 1, 900, true);
        P("curries", "Classic Chicken Curry & Rice", "Home-style chicken curry with jeera rice — the Sunday-lunch classic.", 289, "photo-1585937421612-70a008356fbe", false, SpiceLevel.Medium, "chicken,lunch,rice,comfort,home-style", 35, 20, 1, 720);

        // Biryani
        P("biryani-rice", "Hyderabadi Chicken Dum Biryani", "Long-grain basmati and marinated chicken, dum-cooked with saffron, fried onions and mint. With raita.", 299, "photo-1589302168068-964664d93dc0", false, SpiceLevel.Hot, "biryani,chicken,rice,dinner,bestseller", 50, 25, 1, 780, true);
        P("biryani-rice", "Veg Dum Biryani", "Seasonal vegetables and basmati slow-cooked with whole spices. With raita and salan.", 229, "photo-1563379091339-03b21ab4a4f8", true, SpiceLevel.Medium, "biryani,rice,dinner,lunch", 45, 22, 1, 640);
        P("biryani-rice", "Family Biryani Handi", "Our chicken dum biryani in a sharing handi with raita, salan and two gulab jamuns.", 749, "photo-1589302168068-964664d93dc0", false, SpiceLevel.Hot, "biryani,chicken,family,party,sharing", 15, 35, 4, 2900);

        // South Indian
        P("south-indian", "Masala Dosa", "Crisp rice-lentil crepe with potato masala, coconut chutney and sambar.", 149, "photo-1668236543090-82eba5ee5976", true, SpiceLevel.Mild, "dosa,breakfast,light,south-indian", 60, 15, 1, 420, true);
        P("south-indian", "Idli Vada Sambar", "Two fluffy idlis and a crispy medu vada with sambar and chutneys.", 119, "photo-1630383249896-424e482df921", true, SpiceLevel.Mild, "idli,breakfast,light,healthy,south-indian", 60, 12, 1, 360);

        // Chinese
        P("chinese", "Veg Hakka Noodles", "Wok-tossed noodles with crunchy vegetables, soy and spring onion.", 179, "photo-1585032226651-759b368d7246", true, SpiceLevel.Mild, "noodles,wok,indo-chinese,dinner", 50, 15, 1, 520);
        P("chinese", "Veg Fried Rice", "Smoky wok-fried rice with vegetables and spring onions.", 169, "photo-1603133872878-684f208fb84b", true, SpiceLevel.Mild, "rice,wok,indo-chinese,lunch", 50, 12, 1, 480);
        P("chinese", "Steamed Momos Basket (8 pcs)", "Veg and paneer momos with fiery red chutney and clear soup.", 159, "photo-1534422298391-e4f8c172dddb", true, SpiceLevel.Hot, "momos,steamed,snack,indo-chinese", 8, 15, 1, 380, true);

        // Pizza & pasta
        P("pizzas-pasta", "Margherita Napoletana", "San Marzano tomato, fresh mozzarella and basil on a blistered sourdough base.", 249, "photo-1574071318508-1cdbab80d002", true, SpiceLevel.None, "pizza,cheesy,classic,kids", 40, 18, 2, 820);
        P("pizzas-pasta", "Smoky Chicken BBQ Pizza", "BBQ chicken, caramelised onion, mozzarella and smoked paprika.", 349, "photo-1513104890138-7c749659a591", false, SpiceLevel.Mild, "pizza,chicken,cheesy,party", 35, 20, 2, 1050);
        P("pizzas-pasta", "Spinach & Burrata Pizza", "Garlic-spinach base, creamy burrata and chilli oil.", 329, "photo-1593560708920-61dd98c46a4e", true, SpiceLevel.Mild, "pizza,cheesy,gourmet", 6, 20, 2, 900);
        P("pizzas-pasta", "Pesto Farfalle", "Bow-tie pasta in basil-walnut pesto with cherry tomatoes and greens.", 259, "photo-1473093295043-cdd812d0e601", true, SpiceLevel.None, "pasta,healthy,fresh,lunch", 30, 15, 1, 560);
        P("pizzas-pasta", "Penne Arrabbiata", "Penne in a spicy garlic-chilli tomato sauce with parmesan.", 239, "photo-1621996346565-e3dbc646d9a9", true, SpiceLevel.Hot, "pasta,spicy,dinner", 30, 15, 1, 610);

        // Burgers & sandwiches
        P("burgers", "Aloo Tikki Crunch Burger", "Crispy spiced potato patty, tomato, lettuce and tangy mayo.", 129, "photo-1571091718767-18b5b1457add", true, SpiceLevel.Mild, "burger,fried,value,kids", 60, 10, 1, 480);
        P("burgers", "Classic Chicken Smash Burger", "Two smashed chicken patties, cheddar, pickles and house sauce in a brioche bun.", 249, "photo-1568901346375-23c9450c58cd", false, SpiceLevel.Mild, "burger,chicken,cheesy,bestseller", 45, 12, 1, 720, true);
        P("burgers", "Peri Peri Chicken Burger", "Grilled peri-peri chicken, rocket and garlic aioli on a toasted sesame bun.", 219, "photo-1550547660-d9450f859349", false, SpiceLevel.Hot, "burger,chicken,grilled,spicy", 40, 12, 1, 610);
        P("burgers", "Double Trouble Burger", "Double chicken patty, smoked chicken rashers, double cheese. Not for the faint-hearted.", 319, "photo-1553979459-d2229ba7433b", false, SpiceLevel.Medium, "burger,chicken,cheesy,indulgent", 4, 15, 1, 980);

        // Bowls & salads
        P("bowls-salads", "Rainbow Buddha Bowl", "Quinoa, chickpeas, avocado, roasted sweet potato, slaw and tahini dressing.", 239, "photo-1512621776951-a57141f2eefd", true, SpiceLevel.None, "healthy,vegan,high-fibre,fresh,lunch,low-cal", 30, 12, 1, 450, true);
        P("bowls-salads", "Grilled Paneer Protein Bowl", "Tandoori paneer, brown rice, corn, edamame, egg-free and packed with protein.", 279, "photo-1546069901-ba9599a7e63c", true, SpiceLevel.Mild, "healthy,protein,paneer,grilled,lunch", 30, 15, 1, 520);
        P("bowls-salads", "Garden Crunch Salad", "Mixed greens, seeds, olives, feta and a citrus vinaigrette.", 189, "photo-1540189549336-e6e99c3679fe", true, SpiceLevel.None, "healthy,salad,low-cal,fresh", 30, 8, 1, 280);
        P("bowls-salads", "Avocado Pomegranate Salad", "Avocado, pomegranate, rocket and toasted almonds with honey-lime dressing.", 259, "photo-1511690743698-d9d85f2fbf38", true, SpiceLevel.None, "healthy,salad,fresh,low-cal", 25, 8, 1, 310);
        P("bowls-salads", "Herb-Grilled Chicken & Greens", "Lemon-herb grilled chicken thigh on sautéed kale with cranberries.", 299, "photo-1610057099443-fde8c4d50f91", false, SpiceLevel.Mild, "healthy,protein,grilled,chicken,low-cal,dinner", 30, 18, 1, 430);
        P("bowls-salads", "Avocado Egg Toast", "Sourdough, smashed avocado, sunny-side-up egg and chilli flakes.", 169, "photo-1525351484163-7529414344d8", false, SpiceLevel.Mild, "breakfast,healthy,egg,protein", 30, 10, 1, 390);

        // Desserts
        P("desserts", "Chocolate Truffle Cake Slice", "Rich dark-chocolate sponge layered with ganache.", 179, "photo-1578985545062-69928b1d9587", true, SpiceLevel.None, "chocolate,cake,sweet,party", 30, 5, 1, 480, true);
        P("desserts", "Fudgy Walnut Brownie", "Dense, gooey brownie with toasted walnuts and a chocolate drizzle.", 129, "photo-1606313564200-e75d5e30476c", true, SpiceLevel.None, "chocolate,brownie,sweet", 40, 5, 1, 410);
        P("desserts", "Raspberry Cocoa Brownie", "Cocoa brownie studded with tart raspberries.", 149, "photo-1570145820259-b5b80c5c8bd6", true, SpiceLevel.None, "chocolate,brownie,sweet,berry", 25, 5, 1, 380);
        P("desserts", "Sprinkle Doughnuts (2 pcs)", "Chocolate-glazed doughnuts with rainbow sprinkles.", 149, "photo-1551024601-bec78aea704b", true, SpiceLevel.None, "doughnut,sweet,kids,fried", 30, 5, 1, 520);
        P("desserts", "Strawberry Ice Cream Cone", "Two scoops of strawberry ice cream in a crisp waffle cone.", 99, "photo-1497034825429-c343d7c6a68f", true, SpiceLevel.None, "ice-cream,sweet,kids,summer", 50, 3, 1, 260);
        P("desserts", "Honey Banana Pancakes", "Fluffy stack with banana, honey and a dusting of cinnamon.", 199, "photo-1567620905732-2d1ec7ab7445", true, SpiceLevel.None, "pancakes,breakfast,sweet", 30, 12, 1, 560);
        P("desserts", "Death by Chocolate Sundae", "Chocolate ice cream, brownie bits, cookie crumble and hot fudge.", 199, "photo-1563805042-7684c019e1cb", true, SpiceLevel.None, "chocolate,ice-cream,sweet,indulgent", 25, 5, 1, 640);

        // Beverages
        P("beverages", "Oreo Thick Shake", "Thick vanilla shake blended with Oreo cookies and chocolate sauce.", 169, "photo-1572490122747-3968b75cc699", true, SpiceLevel.None, "shake,chocolate,sweet,cold", 60, 5, 1, 520, true);
        P("beverages", "Classic Cold Coffee", "Chilled coffee whipped with milk and a touch of cream.", 129, "photo-1461023058943-07fcbe16d735", true, SpiceLevel.None, "coffee,cold,caffeine", 60, 5, 1, 230);
        P("beverages", "Lemon Iced Tea", "Brewed black tea, fresh lemon and mint over ice.", 99, "photo-1556679343-c7306c1976bc", true, SpiceLevel.None, "tea,cold,refreshing,low-cal", 60, 3, 1, 90);
        P("beverages", "Fresh Orange Juice", "Freshly squeezed oranges. Nothing added.", 119, "photo-1600271886742-f049cd451bba", true, SpiceLevel.None, "juice,fresh,healthy,cold", 40, 4, 1, 120);
        P("beverages", "Guava Chilli Mocktail", "Pink guava, lime and a chilli-salt rim. Sweet, tangy, a little fiery.", 139, "photo-1544145945-f90425340c7e", true, SpiceLevel.Mild, "mocktail,cold,refreshing,party", 40, 5, 1, 140);

        return list;
    }

    /// <summary>Generates a realistic 45-day order history so the dashboard and reports have real data.</summary>
    private async Task SeedOrderHistoryAsync(List<AppUser> customers, List<Address> addresses, List<Product> products, DateTime now, CancellationToken ct)
    {
        var rng = new Random(2026);
        var weighted = products.SelectMany(p => Enumerable.Repeat(p, p.IsFeatured ? 4 : 1)).ToList();
        var orders = new List<Order>();
        var counter = 0;

        for (var daysAgo = 45; daysAgo >= 0; daysAgo--)
        {
            var ordersToday = rng.Next(2, 6) + (daysAgo < 15 ? 1 : 0);
            for (var n = 0; n < ordersToday; n++)
            {
                var customerIndex = rng.Next(customers.Count);
                var customer = customers[customerIndex];
                // IST lunch (12-15h) or dinner (19-23h) → UTC.
                var localHour = rng.Next(2) == 0 ? rng.Next(12, 15) : rng.Next(19, 23);
                var placedAt = now.Date.AddDays(-daysAgo).AddHours(localHour - 5.5).AddMinutes(rng.Next(60));
                if (placedAt > now.AddMinutes(-20)) continue;

                var lines = weighted.OrderBy(_ => rng.Next()).DistinctBy(p => p.Id).Take(rng.Next(1, 4))
                    .Select(p => new PriceLine(p.Id, p.Name, p.Price, p.Category?.Slug == "beverages" ? rng.Next(1, 3) : 1))
                    .ToList();

                var pickup = rng.NextDouble() < 0.2;
                var fee = pickup ? 0 : lines.Sum(l => l.LineTotal) >= 499 ? 0 : 40;
                var price = PriceCalculator.Calculate(lines, 0, fee, 0.05m);
                var a = addresses[customerIndex];
                var method = rng.NextDouble() < 0.45 ? PaymentMethod.CashOnDelivery : PaymentMethod.OnlineSimulated;

                var order = Order.Create($"BB-{placedAt:yyMMdd}-D{++counter:0000}", customer.Id, customer.FullName, customer.PhoneNumber!,
                    pickup ? FulfillmentType.Pickup : FulfillmentType.Delivery,
                    pickup ? null : new DeliveryAddress(a.RecipientName, a.Phone, a.Line1, a.Line2, a.City, a.Pincode, a.Landmark),
                    method, lines, price, null, null, Guid.NewGuid().ToString("N"), placedAt);

                var t = placedAt;
                if (method == PaymentMethod.OnlineSimulated) order.ConfirmOnlinePayment($"SIM_SEED_{counter:0000}", t = t.AddMinutes(1));

                var roll = rng.NextDouble();
                OrderStatus[] path;
                if (daysAgo == 0 && placedAt > now.AddHours(-3))
                    path = roll switch
                    {
                        < 0.25 => [],
                        < 0.5 => [OrderStatus.Confirmed],
                        < 0.75 => [OrderStatus.Confirmed, OrderStatus.Preparing],
                        _ => [OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.Ready]
                    };
                else if (roll < 0.07)
                    path = [OrderStatus.Cancelled];
                else
                    path = pickup
                        ? [OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.PickedUp]
                        : [OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.OutForDelivery, OrderStatus.Delivered];

                foreach (var status in path)
                {
                    var actor = status switch
                    {
                        OrderStatus.Cancelled => OrderActor.Customer,
                        OrderStatus.OutForDelivery or OrderStatus.Delivered => OrderActor.Delivery,
                        _ => OrderActor.Kitchen
                    };
                    order.ChangeStatus(status, actor, null, status == OrderStatus.Cancelled ? "Changed my plans" : null, t = t.AddMinutes(rng.Next(4, 15)));
                }
                if (order.Status == OrderStatus.Cancelled && order.Payment.Status == PaymentStatus.RefundPending) order.MarkRefunded(t.AddHours(4));

                order.ClearDomainEvents(); // historical data: don't spam notifications
                orders.Add(order);
            }
        }

        db.Orders.AddRange(orders);
        await db.SaveChangesAsync(ct);

        // Reviews from customers who actually received the dish.
        string[] comments =
        [
            "Absolutely loved it, will order again!", "Hot, fresh and on time.", "Great portion size for the price.",
            "Tasted just like home.", "Perfect spice level.", "Packaging was neat and nothing spilled.", "My new go-to comfort food.", null!, null!
        ];
        var delivered = orders.Where(o => o.Status is OrderStatus.Delivered or OrderStatus.PickedUp);
        var reviewed = new HashSet<(int, int)>();
        foreach (var order in delivered)
        {
            foreach (var item in order.Items)
            {
                if (rng.NextDouble() > 0.45 || !reviewed.Add((order.CustomerId, item.ProductId))) continue;
                var customer = customers.First(c => c.Id == order.CustomerId);
                var parts = customer.FullName.Split(' ');
                var review = new Review { ProductId = item.ProductId, UserId = order.CustomerId, AuthorName = $"{parts[0]} {parts[^1][0]}.", Comment = comments[rng.Next(comments.Length)] };
                review.SetRating(rng.NextDouble() < 0.75 ? 5 : rng.NextDouble() < 0.8 ? 4 : 3);
                db.Reviews.Add(review);
            }
        }
        await db.SaveChangesAsync(ct);

        var stats = await db.Reviews.GroupBy(r => r.ProductId)
            .Select(g => new { g.Key, Avg = g.Average(r => (decimal)r.Rating), Count = g.Count() }).ToListAsync(ct);
        foreach (var s in stats) products.First(p => p.Id == s.Key).SetRatingSummary(s.Avg, s.Count);
        await db.SaveChangesAsync(ct);
    }
}
