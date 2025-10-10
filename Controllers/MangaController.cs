using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AnyComic.Data;
using AnyComic.Models;

namespace AnyComic.Controllers
{
    public class MangaController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MangaController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Manga/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var manga = await _context.Mangas
                .Include(m => m.Paginas.OrderBy(p => p.NumeroPagina))
                .FirstOrDefaultAsync(m => m.Id == id);

            if (manga == null)
            {
                return NotFound();
            }

            // Verificar se está nos favoritos do usuário
            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = int.Parse(User.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value);
                var isFavorito = await _context.Favoritos
                    .AnyAsync(f => f.UsuarioId == userId && f.MangaId == id);
                ViewBag.IsFavorito = isFavorito;
            }

            return View(manga);
        }

        // GET: Manga/Read/5
        public async Task<IActionResult> Read(int? id, int pagina = 1)
        {
            if (id == null)
            {
                return NotFound();
            }

            var manga = await _context.Mangas
                .Include(m => m.Paginas.OrderBy(p => p.NumeroPagina))
                .FirstOrDefaultAsync(m => m.Id == id);

            if (manga == null)
            {
                return NotFound();
            }

            if (!manga.Paginas.Any())
            {
                return RedirectToAction(nameof(Details), new { id });
            }

            var paginaAtual = manga.Paginas.FirstOrDefault(p => p.NumeroPagina == pagina);
            if (paginaAtual == null)
            {
                paginaAtual = manga.Paginas.First();
            }

            ViewBag.TotalPaginas = manga.Paginas.Count;
            ViewBag.PaginaAtual = paginaAtual.NumeroPagina;
            ViewBag.MangaId = manga.Id;
            ViewBag.MangaTitulo = manga.Titulo;

            return View(paginaAtual);
        }

        // GET: Manga/Favoritos
        [Authorize]
        public async Task<IActionResult> Favoritos()
        {
            var userId = int.Parse(User.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value);

            var favoritos = await _context.Favoritos
                .Include(f => f.Manga)
                .Where(f => f.UsuarioId == userId)
                .OrderByDescending(f => f.DataAdicao)
                .ToListAsync();

            return View(favoritos);
        }

        // POST: Manga/AddFavorito/5
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddFavorito(int id)
        {
            var userId = int.Parse(User.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value);

            var favoritoExistente = await _context.Favoritos
                .FirstOrDefaultAsync(f => f.UsuarioId == userId && f.MangaId == id);

            if (favoritoExistente == null)
            {
                var favorito = new Favorito
                {
                    UsuarioId = userId,
                    MangaId = id,
                    DataAdicao = DateTime.Now
                };

                _context.Favoritos.Add(favorito);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Manga/RemoveFavorito/5
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFavorito(int id, string? returnUrl)
        {
            var userId = int.Parse(User.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value);

            var favorito = await _context.Favoritos
                .FirstOrDefaultAsync(f => f.UsuarioId == userId && f.MangaId == id);

            if (favorito != null)
            {
                _context.Favoritos.Remove(favorito);
                await _context.SaveChangesAsync();
            }

            if (!string.IsNullOrEmpty(returnUrl) && returnUrl == "favoritos")
            {
                return RedirectToAction(nameof(Favoritos));
            }

            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
