using CarvedRock.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CarvedRock.WebApp.Pages.Admin;

[Authorize(Roles = "admin")]
public class IndexModel(IProductService productService, IOrderService orderService) : PageModel
{
    public List<ProductModel> Products { get; set; } = [];
    public List<OrderModel> Orders { get; set; } = [];

    // "orders" selects the Orders tab; anything else is the Products tab. It is a query string
    // value (not client-side state) so a redirect after cancel/ship lands back on the right tab.
    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    public bool ShowOrders => string.Equals(Tab, "orders", StringComparison.OrdinalIgnoreCase);

    // survive the redirect after an action so the result can be shown on the refreshed list
    [TempData] public string? StatusMessage { get; set; }
    [TempData] public bool StatusIsError { get; set; }

    public async Task OnGetAsync()
    {
        if (ShowOrders)
            Orders = await orderService.GetAllOrdersAsync();
        else
            Products = await productService.GetProductsAsync();
    }

    public async Task<IActionResult> OnPostCancelAsync(int orderId, string? reason)
    {
        var result = await orderService.CancelOrderAsync(orderId, reason?.Trim());
        return Done(result);
    }

    public async Task<IActionResult> OnPostShipAsync(int orderId)
    {
        var result = await orderService.ShipOrderAsync(orderId);
        return Done(result);
    }

    private IActionResult Done(CancelOrderResult result)
    {
        StatusMessage = result.Message;
        StatusIsError = !result.Success;
        return RedirectToPage(new { tab = "orders" });
    }

    // Only Placed orders can still change; Shipped and Cancelled ones are view-only.
    public static bool CanChange(OrderModel order) => order.Status == "Placed";

    public static string BadgeClass(string status) => status switch
    {
        "Placed" => "bg-warning text-dark",
        "Shipped" => "bg-success",
        "Cancelled" => "bg-secondary",
        _ => "bg-light text-dark"
    };
}
