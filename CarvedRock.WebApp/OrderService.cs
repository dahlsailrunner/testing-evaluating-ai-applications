using CarvedRock.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Http.Headers;

namespace CarvedRock.WebApp;

/// <param name="Success">Whether the order is now cancelled.</param>
/// <param name="Message">Text that is safe to show the user either way.</param>
public record CancelOrderResult(bool Success, string Message);

public interface IOrderService
{
    Task<List<OrderModel>> GetOrdersAsync();
    Task<CancelOrderResult> CancelOrderAsync(int orderId, string? reason);
}

public class OrderService : IOrderService
{
    private readonly IHttpContextAccessor _httpCtxAccessor;

    private HttpClient Client { get; }

    public OrderService(HttpClient client, IConfiguration config, IHttpContextAccessor httpCtxAccessor)
    {
        client.BaseAddress = new Uri(
            config.GetValue<string>("CarvedRock:ApiBaseUrl") ?? "https://api"
        );

        Client = client;
        _httpCtxAccessor = httpCtxAccessor;
    }

    private async Task SetAuthorizationHeader()
    {
        var httpCtx = _httpCtxAccessor.HttpContext;
        if (httpCtx != null)
        {
            var accessToken = await httpCtx.GetTokenAsync("access_token");
            Client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        }
    }

    public async Task<List<OrderModel>> GetOrdersAsync()
    {
        await SetAuthorizationHeader();
        return await Client.GetFromJsonAsync<List<OrderModel>>("Order") ?? [];
    }

    public async Task<CancelOrderResult> CancelOrderAsync(int orderId, string? reason)
    {
        await SetAuthorizationHeader();
        var response = await Client.PostAsJsonAsync($"Order/{orderId}/cancel",
            new CancelOrderModel { Reason = reason });

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return new CancelOrderResult(true, $"Order #{orderId} has been cancelled.");
            case HttpStatusCode.NotFound:
                return new CancelOrderResult(false, $"Order #{orderId} could not be found.");
            case HttpStatusCode.Conflict: // already shipped or already cancelled; the API says which
                var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
                return new CancelOrderResult(false,
                    problem?.Detail ?? $"Order #{orderId} can no longer be cancelled.");
            case HttpStatusCode.BadRequest: // e.g. reason too long
                return new CancelOrderResult(false, "That cancellation request wasn't valid. Please shorten the reason and try again.");
            default:
                response.EnsureSuccessStatusCode();
                return new CancelOrderResult(false, "Something went wrong cancelling the order.");
        }
    }
}
