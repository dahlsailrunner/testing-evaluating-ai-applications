using CarvedRock.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CarvedRock.WebApp.Pages.Admin;

[Authorize(Roles = "admin")]
public class CreateModel(IProductService productService) : PageModel
{
    public IActionResult OnGet()
    {
        Product = new();
        return Page();
    }

    [BindProperty]
    public NewProductModel Product { get; set; } = null!;

    public async Task<IActionResult> OnPostAsync()
    {

        if (!ModelState.IsValid) // local validation
        {
            return Page();
        }

        var validationErrors = await productService.AddProductAsync(Product); // api validation
        if (validationErrors.Count > 0)
        {
            foreach (var error in validationErrors)
            {
                ModelState.AddModelError($"Product.{error.Key}", string.Join(",", error.Value.Split('|')));
            }
            return Page();
        }
        return RedirectToPage("./Index");
    }

    // JSON in, JSON out: the "Generate description" button on the form posts the name, category and key
    // features here. It only drafts text; nothing is saved until the admin submits the form.
    public async Task<IActionResult> OnPostDescribeAsync([FromBody] DescribeDraftRequest request,
        [FromServices] IProductDescriptionService describer, CancellationToken cancellationToken)
    {
        var result = await describer.DraftAsync(request, cancellationToken);
        return new JsonResult(result);
    }
}
