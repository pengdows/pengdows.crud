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
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public DateTime? DateOfBirth { get; set; }
}

public class Address
{
    public string Line1 { get; set; }
    public string Line2 { get; set; }
    public string City { get; set; }
    public string State { get; set; }
    public string PostalCode { get; set; }
}

public class ProductItem
{
    public string SKU { get; set; }
    public string Name { get; set; }
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
    public CustomerInfo Customer { get; set; }
    public Address ShippingAddress { get; set; }
    public List<ProductItem> Items { get; set; }
    public OrderSummary Summary { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime PlacedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; }
}