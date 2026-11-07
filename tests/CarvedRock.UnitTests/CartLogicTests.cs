using CarvedRock.Data;
using CarvedRock.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarvedRock.UnitTests;

public class CartLogicTests
{
    [Test]
    public async Task ClearCartAsyncCallsRepository()
    {
        var mockRepo = ICarvedRockRepository.Mock();
        var validator = new AddToCartValidator(mockRepo);
        var cartLogic = new CartLogic(mockRepo, validator, NullLogger<CartLogic>.Instance);

        await cartLogic.ClearCartAsync("user-1");

        mockRepo.ClearCartAsync("user-1").WasCalled();
    }
}
