#region

using System;
using System.Collections.Generic;

#endregion

namespace pengdows.crud.Tests;

public enum OrderStatus
{
    Pending,
    Shipped,
    Delivered,
    Cancelled
}

public class CustomerInfo
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
}

public class Address
{
    public string Line1 { get; set; } = string.Empty;
    public string Line2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
}

public class ProductItem
{
    public string SKU { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class OrderSummary
{
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal Total => Subtotal + Shipping;
}

public class Order
{
    public Order()
    {
        Items = new List<ProductItem>();
        Metadata = new Dictionary<string, string>();
    }

    public Guid OrderId { get; set; }
    public CustomerInfo Customer { get; set; } = new();
    public Address ShippingAddress { get; set; } = new();
    public List<ProductItem> Items { get; set; }
    public OrderSummary Summary { get; set; } = new();
    public OrderStatus Status { get; set; }
    public DateTime PlacedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; }
}
