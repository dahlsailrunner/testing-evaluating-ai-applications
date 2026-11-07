using CarvedRock.Core;
using CarvedRock.Data;
using CarvedRock.Domain;
using FluentValidation;

namespace CarvedRock.UnitTests;

public class ProductValidationTests
{
    private static ICarvedRockRepository _mockedRepo = null!;

    [Before(Class)]
    public static void SetupDatabaseMock(ClassHookContext context)
    {
        var mock = ICarvedRockRepository.Mock();
        mock.IsProductNameUniqueAsync(Any()).Returns(true);
        mock.IsProductNameUniqueAsync("duplicate").Returns(false);
        _mockedRepo = mock;
    }

    [Test]
    public async Task NameIsRequiredProductValidation()
    {
        var validator = new NewProductValidator(_mockedRepo);

        var newProduct = new NewProductModel
        {
            //Name = "",
            Category = "boots",
            Description = "really nice footwear - you'll love them!",
            ImgUrl = "https://some.place/image.png",
            Price = 59.99
        };

        var result = await validator.ValidateAsync(newProduct);

        await Assert.That(result.Errors)
                    .Contains(err => err.ErrorMessage == "Name is required.");
    }

    [Test]
    public async Task DescriptionIsRequiredProductValidation()
    {
        var validator = new NewProductValidator(_mockedRepo);

        var newProduct = new NewProductModel
        {
            Name = "Something Cool",
            Category = "boots",
            Description = "", // or omit entirely
            ImgUrl = "https://some.place/image.png",
            Price = 59.99
        };

        var result = await validator.ValidateAsync(newProduct);

        await Assert.That(result.Errors)
                    .Contains(err => err.ErrorMessage == "Description is required.");
    }

    [Test]
    public async Task PriceMustBeWithinRange()
    {
        var validator = new NewProductValidator(_mockedRepo);

        var newProduct = new NewProductModel
        {
            Name = "Something Cool",
            Category = "boots",
            Description = "", // or omit entirely
            ImgUrl = "https://some.place/image.png",
            Price = 49.99 //50 - 300
        };

        var result = await validator.ValidateAsync(newProduct);

        await Assert.That(result.Errors)
                .Contains(err => err.ErrorMessage ==
                    "Price for boots must be between $50.00 and $300.00.");
    }

    [Test]
    public async Task ValidProductPassesValidation()
    {
        var validator = new NewProductValidator(_mockedRepo);

        var newProduct = new NewProductModel
        {
            Name = "Woods Walker",
            Category = "boots",
            Description = "really nice footwear - you'll love them!",
            ImgUrl = "https://some.place/image.png",
            Price = 51.00 // 50 - 300
        };

        var result = await validator.ValidateAsync(newProduct);

        // only one await with this syntax - evaluate multiple
        //   properties of the result
        await Assert.That(result)
            .Member(r => r.Errors, errors => errors.IsEmpty())
            .And
            .Member(r => r.IsValid, valid => valid.IsTrue());

        //await Assert.That(result.Errors).IsEmpty();
        //await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task CategoryIsRequiredProductValidation()
    {
        var validator = new NewProductValidator(_mockedRepo);

        var newProduct = new NewProductModel
        {
            Name = "Woods Walker",
            //Category = "boots",
            Description = "really nice footwear - you'll love them!",
            ImgUrl = "https://some.place/image.png",
            Price = 59.99
        };

        // try
        // {
        //     validator.Validate(newProduct);
        // }
        // catch (Exception ex)
        // {

        // }

        await Assert.That(async () => await validator.ValidateAsync(newProduct))
            .Throws<ArgumentNullException>()
            .WithMessageContaining("Value cannot be null.");
    }

    [Test]
    public async Task MultipleValidationFailuresAreAllReported()
    {
        var validator = new NewProductValidator(_mockedRepo);

        var newProduct = new NewProductModel
        {
            Name = "Something Cool",
            Category = "trash",
            //Description = "", // or omit entirely
            ImgUrl = "https://some.place/image.png",
            Price = 59.99
        };

        var result = await validator.ValidateAsync(newProduct);

        await Assert.That(result.Errors)
                    .Contains(err => err.ErrorMessage == "Description is required.")
                    .And
                    .Contains(err => err.ErrorMessage.StartsWith(
                        "Category must be one of"));
    }

    [Test]
    public async Task DuplicateNameFails()
    {
        IValidator<NewProductModel> validator = new NewProductValidator(_mockedRepo);

        var newProduct = new NewProductModel
        {
            Name = "duplicate",
            Category = "boots",
            Description = "", // or omit entirely
            ImgUrl = "https://some.place/image.png",
            Price = 59.99
        };

        var result = await validator.ValidateAsync(newProduct,
            opts => opts.IncludeAllRuleSets());

        await Assert.That(result.Errors)
                    .Contains(err => err.ErrorMessage == 
                        "A product with the same name already exists.");
    }
}