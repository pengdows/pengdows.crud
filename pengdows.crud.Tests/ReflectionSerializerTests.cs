#region

using System;
using System.Collections.Generic;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ReflectionSerializerTests
{
    [Fact]
    public void SerializeAndDeserialize_Order_RoundTrips()
    {
        var order = new Order
        {
            OrderId = Guid.NewGuid(),
            Customer = new CustomerInfo
            {
                FirstName = "Jane",
                LastName = "Doe",
                Email = "jane@example.com",
                DateOfBirth = new DateTime(1990, 1, 1)
            },
            ShippingAddress = new Address
            {
                Line1 = "123 Main St",
                Line2 = "Apt 4",
                City = "Townsville",
                State = "TS",
                PostalCode = "12345"
            },
            Items =
            [
                new ProductItem { SKU = "ABC", Name = "Widget", UnitPrice = 1.5m, Quantity = 2 },
                new ProductItem { SKU = "DEF", Name = "Gadget", UnitPrice = 2.0m, Quantity = 1 }
            ],
            Summary = new OrderSummary { Subtotal = 5.0m, Shipping = 1.0m },
            Status = OrderStatus.Pending,
            PlacedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string> { { "foo", "bar" } }
        };

        var serialized = ReflectionSerializer.Serialize(order);
        Assert.NotNull(serialized);

        var roundTrip = ReflectionSerializer.Deserialize<Order>(serialized!);

        Assert.Equal(order.Customer.FirstName, roundTrip.Customer.FirstName);
        Assert.Equal(order.Items.Count, roundTrip.Items.Count);
        Assert.Equal(order.Metadata["foo"], roundTrip.Metadata["foo"]);
    }
}