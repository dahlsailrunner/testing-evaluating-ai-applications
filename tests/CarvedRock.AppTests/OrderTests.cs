using System.Net;
using CarvedRock.Data;
using TUnit.Core.Logging;

namespace CarvedRock.AppTests;

[ClassDataSource<AppFixture>(Shared = SharedType.PerTestSession)]
public class OrderTests(AppFixture fixture)
{
    protected static DefaultLogger TestLogger => TestContext.Current!.GetDefaultLogger();

    [Test]
    public async Task PlacingOrderWorksCompletely()
    {
        var client = await fixture.GetCustomerApiClient();

        var productsToOrder = fixture.GeneralFaker
                .PickRandom(fixture
                    .InitialProducts.Where(p =>
                           p.Id != 20 && p.Id != 23 // deleted by webapptest
                        && p.Id != 22),             // updated by mcp test
                    3)
                .ToList(); // important!  this locks the list

        // add products to cart
        foreach (var product in productsToOrder)
        {
            var response = await client.PostAsJsonAsync("/cart",
                                new CartItem(product.Id, 1));

            await Assert.That(response.StatusCode)
                    .IsEqualTo(HttpStatusCode.NoContent);
        }

        // place order
        var orderResult = await client.PostAsJsonAsync("/order",
                                new NewOrder(null));
        await Assert.That(orderResult.StatusCode)
                                .IsEqualTo(HttpStatusCode.Created);
        //-------------------------------------------------------
        // order was placed -- the rest is all of the assertions
        //    and checks to verify the order completely 
        //-------------------------------------------------------

        var placedOrder = await orderResult.Content.ReadFromJsonAsync<PlacedOrder>();
        await Assert.That(placedOrder).IsNotNull();

        // check order records in database (order and details)
        var connStr = await fixture.App.GetConnectionStringAsync("CarvedRockPostgres");
        var options = new DbContextOptionsBuilder<LocalContext>()
                            .UseNpgsql(connStr)
                            .Options;
        await using var context = new LocalContext(options);

        var savedOrder = await context.Orders
            .Include(o => o.Details)
            .SingleAsync(o => o.Id == placedOrder!.Id);

        await Assert.That(savedOrder).IsNotNull()
            .And.Member(o => o.Email, e => e.IsEqualTo(placedOrder!.Email))
            .And.Member(o => o.Total, t =>
                    t.IsEqualTo(savedOrder.Details.Sum(d => d.LineTotal)))
            .And.Member(o => o.Details.Count, c => c.IsEqualTo(productsToOrder.Count));

        foreach (var ordered in savedOrder.Details)
        {
            TestLogger.LogInformation($"ACTUAL: [ {ordered.ProductId}, {ordered.ProductName}, {ordered.Quantity}, {ordered.UnitPrice} ]");
        }
        foreach (var product in productsToOrder)
        {
            TestLogger.LogInformation($"EXPECTED: {product}");
        }

        foreach (var product in productsToOrder)
        {
            await Assert.That(savedOrder.Details).Contains(d =>
                d.ProductId == product.Id &&
                d.ProductName == product.Name &&
                d.Quantity == 1 &&
                d.UnitPrice == product.Price);
        }

        // verify cart is empty
        var cartAfterOrder = await client.GetFromJsonAsync<List<CartLine>>("/cart");
        await Assert.That(cartAfterOrder).IsEmpty();

        var emailApiEndpoint = fixture.App.GetEndpoint("smtp", "http");
        // verify email exists, and contains each ordered product
        using var mailClient = new HttpClient { BaseAddress = emailApiEndpoint };

        var messages = await mailClient
                .GetFromJsonAsync<MailPitMessageList>("/api/v1/messages");
        var sentMessage = messages!.Messages
            .Where(m => m.To.Any(to => to.Address == placedOrder!.Email))
            .OrderByDescending(m => m.Created)
            .FirstOrDefault();

        await Assert.That(sentMessage).IsNotNull();
        await Assert.That(sentMessage!.Subject).IsEqualTo("Your CarvedRock Order");

        var fullMessage = await mailClient
                .GetFromJsonAsync<MailPitMessage>($"/api/v1/message/{sentMessage.ID}");

        foreach (var product in productsToOrder)
        {
            await Assert.That(fullMessage!.HTML).Contains(product.Name);
        }
    }
}

public record CartItem(int ProductId, int Quantity);
public record NewOrder(string? Email);

public record PlacedOrder(int Id, string Email, DateTime OrderDate, double Total, List<PlacedOrderDetail> Details);
public record PlacedOrderDetail(int ProductId, string ProductName, int Quantity, double UnitPrice, double LineTotal);

public record CartLine(int ProductId, int Quantity, string Name, string Category, double Price, double Total);

public record MailPitMessageList(List<MailPitMessageSummary> Messages);
public record MailPitMessageSummary(string ID, List<MailPitAddress> To, string Subject, DateTimeOffset Created);
public record MailPitAddress(string Name, string Address);
public record MailPitMessage(string HTML, string Text);
public record CartItemModel(int ProductId, int Quantity,
    string Name, string Category, double Price, double Total);