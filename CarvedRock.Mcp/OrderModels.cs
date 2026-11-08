namespace CarvedRock.Mcp;

// What the AI model gets to see about an order. Deliberately a subset of the API's OrderModel:
// the customer's email address isn't needed to answer order questions, so it isn't exposed.
public record OrderSummary
{
    public int Id { get; set; }
    public DateTime OrderDate { get; set; }
    /// <summary>"Placed", "Shipped" or "Cancelled".</summary>
    public string Status { get; set; } = null!;
    public double Total { get; set; }
    public List<OrderLineSummary> Details { get; set; } = [];
}

public record OrderLineSummary
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public int Quantity { get; set; }
    public double UnitPrice { get; set; }
    public double LineTotal { get; set; }
}

/// <summary>Result of looking up a single order: <c>ok</c> or <c>not_found</c>.</summary>
public record OrderLookup(string Status, OrderSummary? Order = null, string? Message = null);

public record CartLine
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public string Name { get; set; } = null!;
    public double Total { get; set; }
}
