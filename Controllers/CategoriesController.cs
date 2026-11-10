using Microsoft.AspNetCore.Mvc;
using product_website.Data;

namespace product_website.Controllers;

[Route("api/categories")]
public class CategoriesController : AuthedApiController
{
    private readonly CategoryRepository _categories;

    public CategoriesController(CategoryRepository categories) => _categories = categories;

    // GET /api/categories
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _categories.GetActiveAsync());
}
