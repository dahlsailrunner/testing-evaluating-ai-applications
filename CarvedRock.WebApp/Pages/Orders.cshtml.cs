using CarvedRock.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CarvedRock.WebApp.Pages;

[ValidateAntiForgeryToken]
public class OrdersModel(IOrderService orderService) : PageModel
{
    public List<OrderModel> Orders { get; set; } = [];

    // survive the redirect after a cancel so the result can be shown on the refreshed list
    [TempData] public string? StatusMessage { get; set; }
    [TempData] public bool StatusIsError { get; set; }

    public async Task OnGetAsync()
    {
        Orders = await orderService.GetOrdersAsync();
    }

    public async Task<IActionResult> OnPostCancelAsync(int orderId, string? reason)
    {
        var result = await orderService.CancelOrderAsync(orderId, reason?.Trim());
        StatusMessage = result.Message;
        StatusIsError = !result.Success;
        return RedirectToPage();
    }

    public static string BadgeClass(string status) => status switch
    {
        "Placed" => "bg-warning text-dark",
        "Shipped" => "bg-success",
        "Cancelled" => "bg-secondary",
        _ => "bg-light text-dark"
    };
}
