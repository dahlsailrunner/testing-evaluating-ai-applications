using CarvedRock.Core;
using CarvedRock.Data;
using CarvedRock.Domain;
using FluentValidation;

namespace CarvedRock.UnitTests;

public class ProductValidation_DataDrivenTests
{
    private static ICarvedRockRepository _mockedRepo = null!;
    private static IValidator<NewProductModel> _validator = null!;

    [Before(Class)]
    public static void SetupDatabaseMock(ClassHookContext context)
    {
        var mock = ICarvedRockRepository.Mock();
        mock.IsProductNameUniqueAsync(Any()).Returns(true);
        mock.IsProductNameUniqueAsync("duplicate").Returns(false);
        _mockedRepo = mock;

        _validator = new NewProductValidator(_mockedRepo);
    }

    [Test]
    [Arguments(null, "boots", "really nice footwear - you'll love them!",
                    "https://some.place/image.png", 59.99,
                    "Name is required.",
                    DisplayName = "Inline - missing product name")]
    [Arguments("Nice Boot", "boots", "",
                    "https://some.place/image.png", 59.99,
                    "Description is required.",
                    DisplayName = "Inline - empty product description")]
    [Arguments("Fancy Boot", "boots", "really nice footwear - you'll love them!",
                    "https://some.place/image.png", 49.99,
                    "Price for boots must be between $50.00 and $300.00.",
                    DisplayName = "Inline - price too low")]
    [Arguments("duplicate", "boots", "really nice footwear - you'll love them!",
                    "https://some.place/image.png", 59.99,
                    "A product with the same name already exists.",
                    DisplayName = "Inline - duplicate product name")]
    public async Task LongSingleValidationFailures(string? name, string? category,
        string? description, string? imageUrl, double price, string expectedMessage)
    {
        var productToValidate = new NewProductModel
        {
            Name = name!,
            Category = category!,
            Description = description!,
            ImgUrl = imageUrl!,
            Price = price
        };

        var result = await _validator.ValidateAsync(productToValidate,
            opts => opts.IncludeAllRuleSets());

        await Assert.That(result.Errors)
            .Contains(err => err.ErrorMessage == expectedMessage);
    }

    [Test]
    //[MethodDataSource(nameof(SingleFailureDataSource))]
    [MethodDataSource(nameof(SingleFailureDataSourceWithNames))]
    public async Task SingleValidationFailures(NewProductModel productToValidate,
            string expectedMessage)
    {
        var result = await _validator.ValidateAsync(productToValidate,
            opts => opts.IncludeAllRuleSets());

        await Assert.That(result.Errors)
            .Contains(err => err.ErrorMessage == expectedMessage);
    }

    public static IEnumerable<Func<(NewProductModel Product,
            string ExpectedMessage)>> SingleFailureDataSource()
    {
        yield return () => (new NewProductModel
        {
            Category = "boots",
            Description = "really nice footwear - you'll love them!",
            ImgUrl = "https://some.place/image.png",
            Price = 59.99
        }, "Name is required.");

        yield return () => (new NewProductModel
        {
            Name = "Woods Walker",
            Category = "boots",
            Description = "really nice footwear - you'll love them!",
            ImgUrl = "https://some.place/image.png",
            Price = 49.99 // 50 - 300
        }, "Price for boots must be between $50.00 and $300.00.");

        yield return () => (new NewProductModel
        {
            Name = "duplicate",
            Category = "boots",
            Description = "really nice footwear - you'll love them!",
            ImgUrl = "https://some.place/image.png",
            Price = 49.99 // 50 - 300
        }, "A product with the same name already exists.");
    }

    public static IEnumerable<Func<TestDataRow<(NewProductModel Product,
                string ExpectedMessage)>>> SingleFailureDataSourceWithNames()
    {
        yield return () => new((new NewProductModel
        {
            Category = "boots",
            Description = "really nice footwear - you'll love them!",
            ImgUrl = "https://some.place/image.png",
            Price = 59.99
        }, "Name is required."),
        DisplayName: "Missing product name");

        yield return () => new((new NewProductModel
        {
            Name = "Woods Walker",
            Category = "boots",
            Description = "really nice footwear - you'll love them!",
            ImgUrl = "https://some.place/image.png",
            Price = 49.99 // 50 - 300
        }, "Price for boots must be between $50.00 and $300.00."),
        DisplayName: "Price too low");

        yield return () => new((new NewProductModel
        {
            Name = "duplicate",
            Category = "boots",
            Description = "really nice footwear - you'll love them!",
            ImgUrl = "https://some.place/image.png",
            Price = 49.99 // 50 - 300
        }, "A product with the same name already exists."),
        DisplayName: "Duplicate product name");
    }

    [Test]
    [Arguments("Fancy Boot", "boots", "really nice footwear - you'll love them!",
                    "https://some.place/image.png", 50.00)]
    [Arguments("Fancy Boot", "boots", "really nice footwear - you'll love them!",
                    "https://some.place/image.png", 300.00)]
    [Arguments("Ki-Yay Kayak", "kayak", "glides so nicely!",
                    "https://some.place/image.png", 100.00)]
    [Arguments("Ki-Yay Kayak", "kayak", "glides so nicely!",
                    "https://some.place/image.png", 500.00)]
    public async Task PassProductValidation(string? name, string? category,
        string? description, string? imageUrl, double price)
    {
        var productToValidate = new NewProductModel
        {
            Name = name!,
            Category = category!,
            Description = description!,
            ImgUrl = imageUrl!,
            Price = price
        };
        var result = await _validator.ValidateAsync(productToValidate,
            opts => opts.IncludeAllRuleSets());

        // only one await with this syntax - evaluate multiple
        //   properties of the result
        await Assert.That(result)
            .Member(r => r.Errors, errors => errors.IsEmpty())
            .And
            .Member(r => r.IsValid, valid => valid.IsTrue());
    }
}