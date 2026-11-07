using CarvedRock.Core;

namespace CarvedRock.Domain;

public enum OrderResultStatus
{
    Ok,
    /// <summary>No such order, or it isn't visible to the caller (deliberately indistinguishable).</summary>
    NotFound,
    /// <summary>The order exists but its current status doesn't allow the operation.</summary>
    Conflict
}

public record OrderResult(OrderResultStatus Status, OrderModel? Order = null, string? Message = null);
