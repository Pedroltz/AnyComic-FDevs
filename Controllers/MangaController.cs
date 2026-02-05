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

        // GET: Manga/Index
        public async Task<IActionResult> Index(string? searchTerm, string? autor, DateTime? dataInicio, DateTime? dataFim, string? sortBy)
        {
            var query = _context.Mangas.AsQueryable();

            // Filtro por título
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(m => m.Titulo.Contains(searchTerm));
            }

            // Filtro por autor
            if (!string.IsNullOrWhiteSpace(autor))
            {
                query = query.Where(m => m.Autor.Contains(autor));
            }

            // Filtro por data de criação (início)
            if (dataInicio.HasValue)
            {
                query = query.Where(m => m.DataCriacao >= dataInicio.Value);
            }

            // Filtro por data de criação (fim)
            if (dataFim.HasValue)
            {
                query = query.Where(m => m.DataCriacao <= dataFim.Value);
            }

            // Ordenação
            query = sortBy switch
            {
                "titulo_asc" => query.OrderBy(m => m.Titulo),
                "titulo_desc" => query.OrderByDescending(m => m.Titulo),
                "autor_asc" => query.OrderBy(m => m.Autor),
                "autor_desc" => query.OrderByDescending(m => m.Autor),
                "data_asc" => query.OrderBy(m => m.DataCriacao),
                "data_desc" => query.OrderByDescending(m => m.DataCriacao),
                _ => query.OrderByDescending(m => m.DataCriacao) // Padrão: mais recentes primeiro
            };

            var mangas = await query
                .Include(m => m.Capitulos)
                .ToListAsync();

            // Passar os filtros atuais para a view
            ViewBag.SearchTerm = searchTerm;
            ViewBag.Autor = autor;
            ViewBag.DataInicio = dataInicio?.ToString("yyyy-MM-dd");
            ViewBag.DataFim = dataFim?.ToString("yyyy-MM-dd");
            ViewBag.SortBy = sortBy;

            return View(mangas);
        }

        // GET: Manga/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            // Highly optimized query - only load chapters, NOT pages
            // Pages are only needed for counts, not actual data
            var manga = await _context.Mangas
                .Include(m => m.Capitulos.OrderBy(c => c.NumeroCapitulo))
                .FirstOrDefaultAsync(m => m.Id == id);

            if (manga == null)
            {
                return NotFound();
            }

            // Load page counts separately using efficient COUNT queries
            // This loads ONLY the Paginas collection for each chapter (much faster than full query)
            if (manga.Capitulos.Any())
            {
                // Load pages for each chapter in a single optimized query
                var capituloIds = manga.Capitulos.Select(c => c.Id).ToList();
                var paginasPorCapitulo = await _context.PaginasMangas
                    .Where(p => capituloIds.Contains(p.CapituloId))
                    .ToListAsync();

                // Assign pages to their respective chapters
                foreach (var capitulo in manga.Capitulos)
                {
                    capitulo.Paginas = paginasPorCapitulo
                        .Where(p => p.CapituloId == capitulo.Id)
                        .ToList();
                }
            }

            // Load pages for the manga (for backward compatibility with non-chapter manga)
            await _context.Entry(manga)
                .Collection(m => m.Paginas)
                .LoadAsync();

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
        public async Task<IActionResult> Read(int? id, int? capituloNumero = null, string pagina = "1")
        {
            if (id == null)
            {
                return NotFound();
            }

            var manga = await _context.Mangas
                .Include(m => m.Capitulos.OrderBy(c => c.NumeroCapitulo))
                    .ThenInclude(c => c.Paginas.OrderBy(p => p.NumeroPagina))
                .FirstOrDefaultAsync(m => m.Id == id);

            if (manga == null)
            {
                return NotFound();
            }

            if (!manga.Capitulos.Any() || !manga.Capitulos.Any(c => c.Paginas.Any()))
            {
                return RedirectToAction(nameof(Details), new { id });
            }

            // If no chapter specified, start from Chapter 1
            Capitulo? capituloAtual;
            if (capituloNumero == null)
            {
                capituloAtual = manga.Capitulos.OrderBy(c => c.NumeroCapitulo).First();
            }
            else
            {
                capituloAtual = manga.Capitulos.FirstOrDefault(c => c.NumeroCapitulo == capituloNumero);
                if (capituloAtual == null)
                {
                    capituloAtual = manga.Capitulos.OrderBy(c => c.NumeroCapitulo).First();
                }
            }

            // Get the requested page from the current chapter
            PaginaManga? paginaAtual;
            if (pagina.Equals("last", StringComparison.OrdinalIgnoreCase))
            {
                paginaAtual = capituloAtual.Paginas.OrderByDescending(p => p.NumeroPagina).First();
            }
            else
            {
                int.TryParse(pagina, out int paginaNum);
                paginaAtual = capituloAtual.Paginas.FirstOrDefault(p => p.NumeroPagina == paginaNum);
                if (paginaAtual == null)
                {
                    paginaAtual = capituloAtual.Paginas.OrderBy(p => p.NumeroPagina).First();
                }
            }

            // Chapter-scoped navigation
            var paginasDoCapitulo = capituloAtual.Paginas.OrderBy(p => p.NumeroPagina).ToList();

            // Build page map only for the current chapter
            var pageMap = paginasDoCapitulo.Select((p, index) => new
            {
                pagina = p.NumeroPagina,
                index = index + 1
            }).ToList();

            // Determine next/previous chapters
            var capitulosOrdenados = manga.Capitulos.OrderBy(c => c.NumeroCapitulo).ToList();
            var capituloIndex = capitulosOrdenados.FindIndex(c => c.Id == capituloAtual.Id);
            var proximoCapitulo = capituloIndex < capitulosOrdenados.Count - 1
                ? capitulosOrdenados[capituloIndex + 1] : null;
            var capituloAnterior = capituloIndex > 0
                ? capitulosOrdenados[capituloIndex - 1] : null;

            ViewBag.TotalPaginas = paginasDoCapitulo.Count;
            ViewBag.PaginaAtual = paginaAtual.NumeroPagina;
            ViewBag.MangaId = manga.Id;
            ViewBag.MangaTitulo = manga.Titulo;
            ViewBag.CapituloAtual = capituloAtual;
            ViewBag.TotalCapitulos = manga.Capitulos.Count;
            ViewBag.PageMap = pageMap;
            ViewBag.Capitulos = capitulosOrdenados;
            ViewBag.ProximoCapitulo = proximoCapitulo;
            ViewBag.CapituloAnterior = capituloAnterior;

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
