using CarvedRock.Data;
using CarvedRock.Data.Entities;
using CarvedRock.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarvedRock.UnitTests;

public class OrderCancellationTests
{
    private const string Owner = "user-1";

    private static Order PlacedOrder(OrderStatus status = OrderStatus.Placed) => new()
    {
        Id = 5,
        UserId = Owner,
        Email = "owner@test.com",
        Status = status,
        Total = 74.99,
        Details = [new OrderDetail { ProductId = 2, ProductName = "Desert Walker", Quantity = 1, UnitPrice = 74.99, LineTotal = 74.99 }]
    };

    // Takes the interface types (the mock wrappers convert implicitly); each test keeps its own
    // `var` mocks so it can use TUnit.Mocks' setup/verification methods on them.
    private static OrderLogic LogicFor(ICarvedRockRepository repo, IOrderEmailSender email) =>
        new(repo, email, NullLogger<OrderLogic>.Instance);

    [Test]
    public async Task OwnerCanCancelPlacedOrder()
    {
        var order = PlacedOrder();
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrderAsync(5).Returns(order);
        var email = IOrderEmailSender.Mock();

        var result = await LogicFor(repo, email)
            .CancelOrderAsync(Owner, isAdmin: false, 5, "  changed my mind  ");

        await Assert.That(result.Status).IsEqualTo(OrderResultStatus.Ok);
        await Assert.That(result.Order!.Status).IsEqualTo("Cancelled");
        await Assert.That(order.Status).IsEqualTo(OrderStatus.Cancelled);
        await Assert.That(order.CancelledAt).IsNotNull();
        await Assert.That(order.CancellationReason).IsEqualTo("changed my mind");
        repo.UpdateOrderAsync(order).WasCalled(Times.Once);
        email.SendOrderCancellationAsync(order).WasCalled(Times.Once);
    }

    [Test]
    public async Task AdminCanCancelAnotherUsersOrder()
    {
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrderAsync(5).Returns(PlacedOrder());
        var email = IOrderEmailSender.Mock();

        var result = await LogicFor(repo, email).CancelOrderAsync("admin-1", isAdmin: true, 5, null);

        await Assert.That(result.Status).IsEqualTo(OrderResultStatus.Ok);
        email.SendOrderCancellationAsync(Any()).WasCalled(Times.Once);
    }

    [Test]
    public async Task AnotherCustomerCannotCancelAndGetsNotFound()
    {
        var order = PlacedOrder();
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrderAsync(5).Returns(order);
        var email = IOrderEmailSender.Mock();

        var result = await LogicFor(repo, email)
            .CancelOrderAsync("someone-else", isAdmin: false, 5, null);

        await Assert.That(result.Status).IsEqualTo(OrderResultStatus.NotFound);
        await Assert.That(order.Status).IsEqualTo(OrderStatus.Placed);
        repo.UpdateOrderAsync(Any()).WasNeverCalled();
        email.SendOrderCancellationAsync(Any()).WasNeverCalled();
    }

    [Test]
    public async Task CancellingMissingOrderIsNotFound()
    {
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrderAsync(5).Returns((Order?)null);

        var result = await LogicFor(repo, IOrderEmailSender.Mock())
            .CancelOrderAsync(Owner, isAdmin: false, 5, null);

        await Assert.That(result.Status).IsEqualTo(OrderResultStatus.NotFound);
        repo.UpdateOrderAsync(Any()).WasNeverCalled();
    }

    [Test]
    [Arguments(OrderStatus.Shipped, "shipped")]
    [Arguments(OrderStatus.Cancelled, "already been cancelled")]
    public async Task OrdersThatAreNotPlacedCannotBeCancelled(OrderStatus status, string expectedMessage)
    {
        var order = PlacedOrder(status);
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrderAsync(5).Returns(order);
        var email = IOrderEmailSender.Mock();

        var result = await LogicFor(repo, email).CancelOrderAsync(Owner, isAdmin: false, 5, null);

        await Assert.That(result.Status).IsEqualTo(OrderResultStatus.Conflict);
        await Assert.That(result.Message).Contains(expectedMessage);
        await Assert.That(order.Status).IsEqualTo(status);
        repo.UpdateOrderAsync(Any()).WasNeverCalled();
        email.SendOrderCancellationAsync(Any()).WasNeverCalled();
    }

    [Test]
    public async Task CancellationStillSucceedsWhenTheEmailFails()
    {
        var order = PlacedOrder();
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrderAsync(5).Returns(order);
        var email = IOrderEmailSender.Mock();
        email.SendOrderCancellationAsync(Any()).Throws(new InvalidOperationException("smtp is down"));

        var result = await LogicFor(repo, email).CancelOrderAsync(Owner, isAdmin: false, 5, null);

        await Assert.That(result.Status).IsEqualTo(OrderResultStatus.Ok);
        await Assert.That(order.Status).IsEqualTo(OrderStatus.Cancelled);
    }

    [Test]
    public async Task OwnerCanGetOwnOrder()
    {
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrderAsync(5).Returns(PlacedOrder());

        var order = await LogicFor(repo, IOrderEmailSender.Mock()).GetOrderAsync(Owner, isAdmin: false, 5);

        await Assert.That(order).IsNotNull();
        await Assert.That(order!.Status).IsEqualTo("Placed");
        await Assert.That(order.Details.Count).IsEqualTo(1);
    }

    [Test]
    public async Task OtherCustomerGetsNullForSomeoneElsesOrder()
    {
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrderAsync(5).Returns(PlacedOrder());

        var order = await LogicFor(repo, IOrderEmailSender.Mock())
            .GetOrderAsync("someone-else", isAdmin: false, 5);

        await Assert.That(order).IsNull();
    }

    [Test]
    public async Task GetAllOrdersReturnsEveryCustomersOrdersWithTheirStatus()
    {
        var repo = ICarvedRockRepository.Mock();
        var other = PlacedOrder(OrderStatus.Shipped);
        other.Id = 6;
        other.UserId = "user-2";
        repo.GetAllOrdersAsync().Returns(new List<Order> { PlacedOrder(), other });

        var orders = await LogicFor(repo, IOrderEmailSender.Mock()).GetAllOrdersAsync();

        await Assert.That(orders.Select(o => o.Id)).IsEquivalentTo(new[] { 5, 6 });
        await Assert.That(orders.Single(o => o.Id == 6).Status).IsEqualTo("Shipped");
    }

    [Test]
    public async Task AdminCanGetAnyOrder()
    {
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrderAsync(5).Returns(PlacedOrder());

        var order = await LogicFor(repo, IOrderEmailSender.Mock()).GetOrderAsync("admin-1", isAdmin: true, 5);

        await Assert.That(order).IsNotNull();
    }

    [Test]
    public async Task GetOrdersMapsEachOrdersStatus()
    {
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrdersForUserAsync(Owner).Returns(
            [PlacedOrder(OrderStatus.Shipped), PlacedOrder(OrderStatus.Cancelled)]);

        var orders = await LogicFor(repo, IOrderEmailSender.Mock()).GetOrdersAsync(Owner);

        await Assert.That(orders.Select(o => o.Status)).IsEquivalentTo(["Shipped", "Cancelled"]);
    }

    [Test]
    public async Task PlacedOrderCanBeMarkedShipped()
    {
        var order = PlacedOrder();
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrderAsync(5).Returns(order);

        var result = await LogicFor(repo, IOrderEmailSender.Mock()).MarkShippedAsync(5);

        await Assert.That(result.Status).IsEqualTo(OrderResultStatus.Ok);
        await Assert.That(order.Status).IsEqualTo(OrderStatus.Shipped);
        repo.UpdateOrderAsync(order).WasCalled(Times.Once);
    }

    [Test]
    [Arguments(OrderStatus.Shipped)]
    [Arguments(OrderStatus.Cancelled)]
    public async Task OnlyPlacedOrdersCanBeMarkedShipped(OrderStatus status)
    {
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrderAsync(5).Returns(PlacedOrder(status));

        var result = await LogicFor(repo, IOrderEmailSender.Mock()).MarkShippedAsync(5);

        await Assert.That(result.Status).IsEqualTo(OrderResultStatus.Conflict);
        repo.UpdateOrderAsync(Any()).WasNeverCalled();
    }

    [Test]
    public async Task ShippingMissingOrderIsNotFound()
    {
        var repo = ICarvedRockRepository.Mock();
        repo.GetOrderAsync(5).Returns((Order?)null);

        var result = await LogicFor(repo, IOrderEmailSender.Mock()).MarkShippedAsync(5);

        await Assert.That(result.Status).IsEqualTo(OrderResultStatus.NotFound);
    }
}
