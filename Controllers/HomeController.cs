using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AnyComic.Models;
using AnyComic.Data;

namespace AnyComic.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;

    public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var mangas = await _context.Mangas.ToListAsync();

        // Get active banners ordered by display order
        var banners = await _context.Banners
            .Where(b => b.Ativo)
            .OrderBy(b => b.Ordem)
            .ToListAsync();

        ViewBag.Banners = banners;

        return View(mangas);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
