using CarvedRock.Core;

namespace CarvedRock.Domain;

public interface IOrderLogic
{
    Task<OrderModel> PlaceOrderAsync(string userId, string email);

    Task<List<OrderModel>> GetOrdersAsync(string userId);

    /// <summary>Every customer's orders, newest first. Callers must restrict this to admins.</summary>
    Task<List<OrderModel>> GetAllOrdersAsync();

    /// <summary>Null when the order doesn't exist or belongs to someone else (unless <paramref name="isAdmin"/>).</summary>
    Task<OrderModel?> GetOrderAsync(string userId, bool isAdmin, int orderId);

    /// <summary>Owner or admin only; only <c>Placed</c> orders can be cancelled.</summary>
    Task<OrderResult> CancelOrderAsync(string userId, bool isAdmin, int orderId, string? reason);

    /// <summary>Only <c>Placed</c> orders can be marked shipped.</summary>
    Task<OrderResult> MarkShippedAsync(int orderId);
}
