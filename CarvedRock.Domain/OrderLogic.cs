using CarvedRock.Core;
using CarvedRock.Data;
using CarvedRock.Data.Entities;
using Microsoft.Extensions.Logging;

namespace CarvedRock.Domain;

public class OrderLogic(ICarvedRockRepository repo,
            IOrderEmailSender emailSender,
            ILogger<OrderLogic> logger) : IOrderLogic
{
    public async Task<OrderModel> PlaceOrderAsync(string userId, string email)
    {
        using var scope = logger.BeginScope(
            new Dictionary<string, object> { ["userId"] = userId });

        var cartItems = await repo.GetCartItemsAsync(userId);
        if (cartItems.Count == 0)
        {
            throw new InvalidOperationException("Cannot place an order with an empty cart.");
        }

        var products = await repo.GetProductsByIdsAsync(cartItems.Select(i => i.ProductId));
        var productsById = products.ToDictionary(p => p.Id);

        var order = new Order
        {
            UserId = userId,
            Email = email,
            OrderDate = DateTime.UtcNow,
            Details = []
        };

        foreach (var item in cartItems)
        {
            if (!productsById.TryGetValue(item.ProductId, out var product)) continue;

            order.Details.Add(new OrderDetail
            {
                ProductId = item.ProductId,
                ProductName = product.Name,
                Quantity = item.Quantity,
                UnitPrice = product.Price,
                LineTotal = product.Price * item.Quantity
            });
        }
        order.Total = order.Details.Sum(d => d.LineTotal);

        var created = await repo.CreateOrderAsync(order);
        logger.LogInformation("Created order {OrderId} with {LineCount} line(s).",
            created.Id, created.Details.Count);

        await repo.ClearCartAsync(userId);

        await emailSender.SendOrderConfirmationAsync(created);

        return MapToModel(created);
    }

    public async Task<List<OrderModel>> GetOrdersAsync(string userId)
    {
        var orders = await repo.GetOrdersForUserAsync(userId);
        return orders.Select(MapToModel).ToList();
    }

    public async Task<List<OrderModel>> GetAllOrdersAsync()
    {
        var orders = await repo.GetAllOrdersAsync();
        return orders.Select(MapToModel).ToList();
    }

    public async Task<OrderModel?> GetOrderAsync(string userId, bool isAdmin, int orderId)
    {
        var order = await repo.GetOrderAsync(orderId);
        return CanAccess(order, userId, isAdmin) ? MapToModel(order!) : null;
    }

    public async Task<OrderResult> CancelOrderAsync(string userId, bool isAdmin, int orderId, string? reason)
    {
        var order = await repo.GetOrderAsync(orderId);
        if (!CanAccess(order, userId, isAdmin))
            return new OrderResult(OrderResultStatus.NotFound);

        if (order!.Status == OrderStatus.Cancelled)
            return new OrderResult(OrderResultStatus.Conflict,
                Message: $"Order {orderId} has already been cancelled.");

        if (order.Status == OrderStatus.Shipped)
            return new OrderResult(OrderResultStatus.Conflict,
                Message: $"Order {orderId} has already shipped and can no longer be cancelled; it would need to be returned instead.");

        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = DateTime.UtcNow;
        order.CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await repo.UpdateOrderAsync(order);
        logger.LogInformation("Cancelled order {OrderId} (requested by {UserId}, admin: {IsAdmin}).",
            orderId, userId, isAdmin);

        try
        {
            await emailSender.SendOrderCancellationAsync(order);
        }
        catch (Exception ex)
        {
            // The cancellation is already saved; a mail problem shouldn't turn it into an error.
            logger.LogWarning(ex, "Order {OrderId} was cancelled but the cancellation email failed.", orderId);
        }

        return new OrderResult(OrderResultStatus.Ok, MapToModel(order));
    }

    public async Task<OrderResult> MarkShippedAsync(int orderId)
    {
        var order = await repo.GetOrderAsync(orderId);
        if (order == null)
            return new OrderResult(OrderResultStatus.NotFound);

        if (order.Status != OrderStatus.Placed)
            return new OrderResult(OrderResultStatus.Conflict,
                Message: $"Order {orderId} is {order.Status} and cannot be marked as shipped.");

        order.Status = OrderStatus.Shipped;
        await repo.UpdateOrderAsync(order);
        logger.LogInformation("Marked order {OrderId} as shipped.", orderId);
        return new OrderResult(OrderResultStatus.Ok, MapToModel(order));
    }

    // Another user's order is reported exactly like a missing one, so ids can't be probed.
    private static bool CanAccess(Order? order, string userId, bool isAdmin) =>
        order != null && (isAdmin || order.UserId == userId);

    private static OrderModel MapToModel(Order order) => new()
    {
        Id = order.Id,
        Email = order.Email,
        OrderDate = order.OrderDate,
        Total = order.Total,
        Status = order.Status.ToString(),
        Details = order.Details.Select(d => new OrderDetailModel
        {
            ProductId = d.ProductId,
            ProductName = d.ProductName,
            Quantity = d.Quantity,
            UnitPrice = d.UnitPrice,
            LineTotal = d.LineTotal
        }).ToList()
    };
}
