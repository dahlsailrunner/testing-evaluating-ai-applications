namespace CarvedRock.Data.Entities;

// Explicit non-zero values so the CLR default (0) is never a valid status. LocalContext
// uses 0 as the "unset" sentinel for the property's database default ("Placed").
public enum OrderStatus
{
    Placed = 1,
    Shipped = 2,
    Cancelled = 3
}
