using CarvedRock.Data;
using CarvedRock.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarvedRock.UnitTests;

public class OrderLogicTests
{
    [Test]
    public async Task PlaceOrderAsyncThrowsWhenCartIsEmpty()
    {
        var mockRepo = ICarvedRockRepository.Mock();
        mockRepo.GetCartItemsAsync(Any()).Returns([]);

        var mockEmailSender = IOrderEmailSender.Mock();
        var orderLogic = new OrderLogic(mockRepo, mockEmailSender, NullLogger<OrderLogic>.Instance);

        await Assert.That(async () => await orderLogic.PlaceOrderAsync("user-1", "user@test.com"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Cannot place an order with an empty cart.");
    }
}
