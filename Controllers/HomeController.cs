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
        // Get newest mangas (last 10 added)
        var newest = await _context.Mangas
            .OrderByDescending(m => m.DataCriacao)
            .Take(10)
            .ToListAsync();

        // Get all mangas for the view
        var mangas = await _context.Mangas.ToListAsync();

        // Get active banners ordered by display order (include Manga for showcase type)
        var banners = await _context.Banners
            .Include(b => b.Manga)
            .Where(b => b.Ativo)
            .OrderBy(b => b.Ordem)
            .ToListAsync();

        ViewBag.Banners = banners;
        ViewBag.NewestMangas = newest;

        return View(mangas);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
