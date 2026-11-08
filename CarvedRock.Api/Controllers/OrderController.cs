using CarvedRock.Core;
using CarvedRock.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarvedRock.Api.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
public class OrderController(IOrderLogic orderLogic,
                    ILogger<OrderController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<List<OrderModel>> GetMine() =>
        await orderLogic.GetOrdersAsync(User.GetUserId());

    // Every customer's orders, for the admin Orders tab. "all" can't clash with {id:int}.
    [HttpGet("all")]
    [Authorize(Roles = "admin")]
    public async Task<List<OrderModel>> GetAll() => await orderLogic.GetAllOrdersAsync();

    // 404 for an order that doesn't exist *and* for one that belongs to someone else, so
    // order ids can't be probed. Admins can see any order.
    [HttpGet("{id:int}")]
    [ProducesResponseType<OrderModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id)
    {
        var order = await orderLogic.GetOrderAsync(User.GetUserId(), User.IsInRole("admin"), id);
        return order == null ? NotFound() : Ok(order);
    }

    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType<OrderModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelOrderModel? request)
    {
        var result = await orderLogic.CancelOrderAsync(User.GetUserId(),
            User.IsInRole("admin"), id, request?.Reason);
        return ToActionResult(result);
    }

    // Exists so shipped orders can be created (tests, demos). Deliberately admin-only and
    // not exposed as an MCP tool, so an AI prompt can never reach it.
    [HttpPost("{id:int}/ship")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType<OrderModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Ship(int id) =>
        ToActionResult(await orderLogic.MarkShippedAsync(id));

    private IActionResult ToActionResult(OrderResult result) => result.Status switch
    {
        OrderResultStatus.Ok => Ok(result.Order),
        OrderResultStatus.NotFound => NotFound(),
        _ => Problem(detail: result.Message, statusCode: StatusCodes.Status409Conflict)
    };

    [HttpPost]
    [ProducesResponseType<OrderModel>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PlaceOrder([FromBody] NewOrderModel newOrder)
    {
        var userId = User.GetUserId();
        // The access token may not carry the email claim, so the caller (the web app,
        // which has the user's email from OIDC) passes it in the request body.
        var email = !string.IsNullOrWhiteSpace(newOrder?.Email)
            ? newOrder.Email
            : User.GetEmail() ?? "unknown@carvedrock.com";

        try
        {
            var order = await orderLogic.PlaceOrderAsync(userId, email);
            logger.LogInformation("Placed order {OrderId} for {UserId}.", order.Id, userId);
            return Created($"/order/{order.Id}", order);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
