using Microsoft.EntityFrameworkCore;

namespace Sample.OrderService;

public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
}

public sealed class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
}

public sealed class OrderDb(DbContextOptions<OrderDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public static async Task SeedAsync(OrderDb db)
    {
        await db.Database.EnsureCreatedAsync();
        if (await db.Orders.AnyAsync()) return;

        for (var i = 1; i <= 50; i++)
        {
            db.Orders.Add(new Order { Id = i, Customer = $"Müşteri {i}", Total = i * 125.5m });
            for (var j = 0; j < 8; j++)
                db.OrderLines.Add(new OrderLine { OrderId = i, Product = $"Ürün {j}", Quantity = j + 1 });
        }
        await db.SaveChangesAsync();
    }
}
