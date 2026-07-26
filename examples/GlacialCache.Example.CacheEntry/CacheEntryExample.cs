using System.Linq;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.Abstractions;
using MemoryPack;
using Microsoft.Extensions.Caching.Distributed;

namespace GlacialCache.Example.CacheEntry;

// Model classes for demonstrating complex type caching

/// <summary>
/// Simple POCO class representing a user.
/// </summary>
public class User
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Record type representing a product.
/// </summary>
public record Product
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Category { get; init; } = string.Empty;
}

/// <summary>
/// Class representing an order item.
/// </summary>
public class OrderItem
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal TotalPrice => Quantity * UnitPrice;
}

/// <summary>
/// Complex nested type representing an order with user and items.
/// </summary>
public class Order
{
    public int OrderId { get; init; }
    public User Customer { get; init; } = null!;
    public List<OrderItem> Items { get; init; } = new();
    public DateTime OrderDate { get; init; }
    public decimal TotalAmount => Items.Sum(item => item.TotalPrice);
}

/// <summary>
/// Example demonstrating how to use the new GetAsync and SetAsync methods with CacheEntry objects.
/// </summary>
public class CacheEntryExample
{
    private readonly IGlacialCache _cache;
    public CacheEntryExample(IGlacialCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Demonstrates using GetAsync and SetAsync with CacheEntry objects.
    /// </summary>
    public async Task RunExampleAsync()
    {
        Console.WriteLine("🚀 GlacialCache CacheEntry Example");
        Console.WriteLine("=================================");

        // Example 1: Using GetAsync with CacheEntry
        Console.WriteLine("\n📝 Example 1: GetAsync with CacheEntry");

        // First, set a cache entry using the traditional method
        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        };

        await _cache.SetAsync("user:123", System.Text.Encoding.UTF8.GetBytes("John Doe"), options);

        // Now retrieve it using the new GetEntryAsync method that returns CacheEntry
        CacheEntry<byte[]>? cacheEntry = await _cache.GetEntryAsync("user:123");

        if (cacheEntry != null)
        {
            Console.WriteLine($"✅ Retrieved CacheEntry:");
            Console.WriteLine($"   Key: {cacheEntry.Key}");
            Console.WriteLine($"   Value: {System.Text.Encoding.UTF8.GetString(cacheEntry.Value.ToArray())}");
            Console.WriteLine($"   AbsoluteExpiration: {cacheEntry.AbsoluteExpiration}");
            Console.WriteLine($"   SlidingExpiration: {cacheEntry.SlidingExpiration}");
        }
        else
        {
            Console.WriteLine("❌ Cache entry not found or expired");
        }

        // Example 2: Using SetAsync with CacheEntry and TimeProvider
        Console.WriteLine("\n📝 Example 2: SetAsync with CacheEntry and TimeProvider");

        // For examples, we'll use TimeProvider.System, but in production code
        // you should inject TimeProvider through dependency injection
        var timeProvider = TimeProvider.System;

        var newEntry = new CacheEntry<byte[]>()
        {
            Key = "user:456",
            Value = System.Text.Encoding.UTF8.GetBytes("Jane Smith"),
            AbsoluteExpiration = timeProvider.GetUtcNow().AddHours(2),
            SlidingExpiration = TimeSpan.FromMinutes(15)
        };

        await _cache.SetAsync(newEntry);
        Console.WriteLine($"✅ Set CacheEntry for key: {newEntry.Key}");

        // Retrieve the newly set entry
        var retrievedEntry = await _cache.GetEntryAsync("user:456");
        if (retrievedEntry != null)
        {
            Console.WriteLine($"✅ Retrieved newly set CacheEntry:");
            Console.WriteLine($"   Value: {System.Text.Encoding.UTF8.GetString(retrievedEntry.Value.ToArray())}");
        }

        // Example 3: Working with IDistributedCache interface
        Console.WriteLine("\n📝 Example 3: Using IDistributedCache interface");

        if (_cache is IDistributedCache distributedCache)
        {
            // This will work with any IDistributedCache implementation
            var entryFromDistributedCache = await distributedCache.GetAsync("user:123");
            if (entryFromDistributedCache != null)
            {
                Console.WriteLine($"✅ Retrieved from IDistributedCache:");
                Console.WriteLine($"   Value: {System.Text.Encoding.UTF8.GetString(entryFromDistributedCache)}");
            }
        }

        // Example 4: Batch operations with CacheEntry
        Console.WriteLine("\n📝 Example 4: Batch operations with CacheEntry");

        var entries = new List<CacheEntry<byte[]>>
        {
            new() { Key = "batch:1", Value = System.Text.Encoding.UTF8.GetBytes("Batch Entry 1"), AbsoluteExpiration = timeProvider.GetUtcNow().AddHours(1) },
            new() { Key = "batch:2", Value = System.Text.Encoding.UTF8.GetBytes("Batch Entry 2"), SlidingExpiration = TimeSpan.FromMinutes(30) },
            new() { Key = "batch:3", Value = System.Text.Encoding.UTF8.GetBytes("Batch Entry 3") },
            new() { Key = "batch:4", Value = System.Text.Encoding.UTF8.GetBytes("Batch Entry 4"), AbsoluteExpiration = timeProvider.GetUtcNow().AddHours(1), SlidingExpiration = TimeSpan.FromMinutes(30) },
        };

        await _cache.SetMultipleEntriesAsync(entries);
        Console.WriteLine($"✅ Set {entries.Count} cache entries in batch");

        var keys = entries.Select(e => e.Key).ToList();
        var retrievedEntries = await _cache.GetMultipleEntriesAsync(keys);

        Console.WriteLine($"✅ Retrieved {retrievedEntries.Count} cache entries:");
        foreach (var (key, entry) in retrievedEntries)
        {
            if (entry != null)
            {
                Console.WriteLine($"   {key}: {System.Text.Encoding.UTF8.GetString(entry.Value.ToArray())}");
            }
        }

        // Example 5: Simple Complex Type (POCO)
        Console.WriteLine("\n📝 Example 5: Simple Complex Type (POCO)");

        var user = new User
        {
            Id = 1001,
            Name = "Alice Johnson",
            Email = "alice@example.com",
            CreatedAt = DateTime.UtcNow.AddDays(-30)
        };



        await _cache.SetEntryAsync("user:profile:1001", user, new() { AbsoluteExpiration = timeProvider.GetUtcNow().AddHours(2) });
        Console.WriteLine($"✅ Cached User object: {user.Name} (ID: {user.Id})");

        var retrievedUserEntry = await _cache.GetEntryAsync<User>("user:profile:1001");
        if (retrievedUserEntry != null)
        {
            Console.WriteLine($"✅ Retrieved User object:");
            Console.WriteLine($"   ID: {retrievedUserEntry.Value.Id}");
            Console.WriteLine($"   Name: {retrievedUserEntry.Value.Name}");
            Console.WriteLine($"   Email: {retrievedUserEntry.Value.Email}");
            Console.WriteLine($"   Created: {retrievedUserEntry.Value.CreatedAt:yyyy-MM-dd}");
            Console.WriteLine($"   BaseType: {retrievedUserEntry.BaseType}");
            Console.WriteLine($"   Size: {retrievedUserEntry.SizeInBytes} bytes");
        }

        // Example 6: Nested Complex Types
        Console.WriteLine("\n📝 Example 6: Nested Complex Types");

        var customer = new User
        {
            Id = 2001,
            Name = "Bob Wilson",
            Email = "bob@example.com",
            CreatedAt = DateTime.UtcNow.AddDays(-60)
        };

        var order = new Order
        {
            OrderId = 5001,
            Customer = customer,
            OrderDate = DateTime.UtcNow,
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    ProductId = 1,
                    ProductName = "Laptop",
                    Quantity = 1,
                    UnitPrice = 1299.99m
                },
                new OrderItem
                {
                    ProductId = 2,
                    ProductName = "Mouse",
                    Quantity = 2,
                    UnitPrice = 29.99m
                },
                new OrderItem
                {
                    ProductId = 3,
                    ProductName = "Keyboard",
                    Quantity = 1,
                    UnitPrice = 79.99m
                }
            }
        };


        await _cache.SetEntryAsync("order:5001", order, new() { AbsoluteExpiration = timeProvider.GetUtcNow().AddHours(4) });
        Console.WriteLine($"✅ Cached Order with {order.Items.Count} items, Total: ${order.TotalAmount:F2}");

        var retrievedOrderEntry = await _cache.GetEntryAsync<Order>("order:5001");
        if (retrievedOrderEntry != null)
        {
            var retrievedOrder = retrievedOrderEntry.Value;
            Console.WriteLine($"✅ Retrieved Order object:");
            Console.WriteLine($"   Order ID: {retrievedOrder.OrderId}");
            Console.WriteLine($"   Customer: {retrievedOrder.Customer.Name} ({retrievedOrder.Customer.Email})");
            Console.WriteLine($"   Items: {retrievedOrder.Items.Count}");
            foreach (var item in retrievedOrder.Items)
            {
                Console.WriteLine($"     - {item.ProductName} x{item.Quantity} @ ${item.UnitPrice:F2} = ${item.TotalPrice:F2}");
            }
            Console.WriteLine($"   Total Amount: ${retrievedOrder.TotalAmount:F2}");
            Console.WriteLine($"   BaseType: {retrievedOrderEntry.BaseType}");
            Console.WriteLine($"   Size: {retrievedOrderEntry.SizeInBytes} bytes");
        }

        // Example 7: Batch Operations with Complex Types
        Console.WriteLine("\n📝 Example 7: Batch Operations with Complex Types");

        var products = new List<Product>
        {
            new Product { Id = 1, Name = "Wireless Headphones", Price = 199.99m, Category = "Electronics" },
            new Product { Id = 2, Name = "Smart Watch", Price = 299.99m, Category = "Electronics" },
            new Product { Id = 3, Name = "Coffee Maker", Price = 89.99m, Category = "Appliances" },
            new Product { Id = 4, Name = "Desk Chair", Price = 249.99m, Category = "Furniture" }
        };

        var productEntries = products.Select(p =>
            new CacheEntry<Product>() { Key = $"product:{p.Id}", Value = p, AbsoluteExpiration = timeProvider.GetUtcNow().AddHours(6) }
        );

        await _cache.SetMultipleEntriesAsync(productEntries);
        Console.WriteLine($"✅ Set {productEntries.Count()} Product records in batch");

        var productKeys = productEntries.Select(e => e.Key).ToList();
        var retrievedProducts = await _cache.GetMultipleEntriesAsync<Product>(productKeys);

        Console.WriteLine($"✅ Retrieved {retrievedProducts.Count} Product records:");
        foreach (var (key, entry) in retrievedProducts)
        {
            if (entry != null)
            {
                Console.WriteLine($"   {key}: {entry.Value.Name} - ${entry.Value.Price:F2} ({entry.Value.Category})");
            }
        }

        // Example 8: Type Safety Demonstration
        Console.WriteLine("\n📝 Example 8: Type Safety Demonstration");

        // Store a User object
        var testUser = new User
        {
            Id = 3001,
            Name = "Charlie Brown",
            Email = "charlie@example.com",
            CreatedAt = DateTime.UtcNow
        };

        var testUserEntry = new CacheEntry<User>()
        {
            Key = "test:user:3001",
            Value = testUser,
            AbsoluteExpiration = timeProvider.GetUtcNow().AddHours(1)
        };

        await _cache.SetEntryAsync(testUserEntry);
        Console.WriteLine($"✅ Stored User object with key: test:user:3001");

        // Try to retrieve as User (correct type) - should succeed
        var correctTypeEntry = await _cache.GetEntryAsync("test:user:3001");
        if (correctTypeEntry != null)
        {
            Console.WriteLine($"✅ Retrieved as User (correct type): {correctTypeEntry.Value}");
            Console.WriteLine($"   BaseType stored: {correctTypeEntry.BaseType}");
        }

        // Try to retrieve as Product (wrong type) - should return null due to type safety
        var wrongTypeEntry = await _cache.GetEntryAsync<Product>("test:user:3001");
        if (wrongTypeEntry == null)
        {
            Console.WriteLine($"✅ Type safety enforced: Retrieving User as Product returned null");
            Console.WriteLine($"   This prevents type confusion and ensures data integrity");
        }
        else
        {
            Console.WriteLine($"❌ Unexpected: Wrong type retrieval succeeded (this should not happen)");
        }

        // Demonstrate BaseType property
        var userEntryForTypeCheck = await _cache.GetEntryAsync<User>("test:user:3001");
        if (userEntryForTypeCheck != null)
        {
            Console.WriteLine($"✅ BaseType property: {userEntryForTypeCheck.BaseType}");
            Console.WriteLine($"   This allows the cache to enforce type safety at retrieval time");
        }

        Console.WriteLine("\n🎉 CacheEntry example completed successfully!");
    }
}




