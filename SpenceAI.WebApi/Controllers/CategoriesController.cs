using Microsoft.AspNetCore.Mvc;
using SpenceAI.Application.Common.Interfaces;
using SpenceAI.Domain.Entities;

namespace SpenceAI.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryRepository _categoryRepository;

    public CategoriesController(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await _categoryRepository.GetAllAsync();
        return Ok(categories);
    }
}