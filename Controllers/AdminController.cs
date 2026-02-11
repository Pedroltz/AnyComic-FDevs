using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AnyComic.Data;
using AnyComic.Models;
using AnyComic.Services;
using System.Security.Cryptography;
using System.Text;

namespace AnyComic.Controllers
{
    /// <summary>
    /// Controller responsible for managing administrative operations of the system.
    /// Allows complete CRUD of manga, page upload and administrator management.
    /// </summary>
    [Authorize] // Requires user to be authenticated
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        /// <summary>
        /// Constructor with dependency injection
        /// </summary>
        /// <param name="context">Database context for Entity Framework operations</param>
        /// <param name="environment">Environment information to manage file paths</param>
        public AdminController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        /// <summary>
        /// Checks if the current user has administrator permissions
        /// </summary>
        /// <returns>True if administrator, False otherwise</returns>
        private bool IsAdmin()
        {
            return User.Claims.FirstOrDefault(c => c.Type == "IsAdmin")?.Value == "True";
        }

        /// <summary>
        /// GET: Admin/Index - Displays the administrative panel with Manga, Admins and Banners sections
        /// </summary>
        public async Task<IActionResult> Index(string searchManga, string searchAdmin)
        {
            // Check administrator permissions
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            // Search manga with search filter
            var mangasQuery = _context.Mangas.Include(m => m.Paginas).AsQueryable();

            if (!string.IsNullOrEmpty(searchManga))
            {
                mangasQuery = mangasQuery.Where(m =>
                    m.Titulo.Contains(searchManga) ||
                    m.Autor.Contains(searchManga));
            }

            var mangas = await mangasQuery.OrderByDescending(m => m.DataCriacao).ToListAsync();

            // Search admins with search filter
            var adminsQuery = _context.UsuariosAdmin.AsQueryable();

            if (!string.IsNullOrEmpty(searchAdmin))
            {
                adminsQuery = adminsQuery.Where(a =>
                    a.Nome.Contains(searchAdmin) ||
                    a.Email.Contains(searchAdmin));
            }

            var admins = await adminsQuery.OrderByDescending(a => a.DataCriacao).ToListAsync();

            // Get all banners ordered by display order
            var banners = await _context.Banners.OrderBy(b => b.Ordem).ToListAsync();

            // Pass data to view using ViewBag
            ViewBag.Mangas = mangas;
            ViewBag.Admins = admins;
            ViewBag.Banners = banners;
            ViewBag.SearchManga = searchManga;
            ViewBag.SearchAdmin = searchAdmin;

            return View();
        }

        /// <summary>
        /// GET: Admin/CreateManga - Displays the manga creation form (CREATE)
        /// </summary>
        public IActionResult CreateManga()
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            return View();
        }

        /// <summary>
        /// POST: Admin/ImportFromEHentai - AJAX endpoint to import manga from e-hentai URL
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ImportFromEHentai([FromBody] ImportRequest request)
        {
            if (!IsAdmin())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            if (string.IsNullOrEmpty(request?.Url))
            {
                return Json(new { success = false, message = "URL is required" });
            }

            try
            {
                var importer = new EHentaiImporter(_environment);
                var result = await importer.ImportFromUrl(request.Url);

                if (result.HasValue)
                {
                    var importedManga = result.Value.manga;
                    var pagePaths = result.Value.pagePaths;

                    // Copy first page to capas folder and set as cover
                    if (pagePaths.Count > 0)
                    {
                        var firstPagePath = pagePaths[0];

                        // Get the physical path of the first page
                        var sourcePath = Path.Combine(_environment.WebRootPath, firstPagePath.TrimStart('/'));

                        if (System.IO.File.Exists(sourcePath))
                        {
                            // Create capas directory if it doesn't exist
                            var capasDir = Path.Combine(_environment.WebRootPath, "uploads", "capas");
                            if (!Directory.Exists(capasDir))
                            {
                                Directory.CreateDirectory(capasDir);
                            }

                            // Create a unique filename for the cover
                            var extension = Path.GetExtension(sourcePath);
                            var coverFileName = $"{Guid.NewGuid()}_cover{extension}";
                            var coverPath = Path.Combine(capasDir, coverFileName);

                            // Copy the first page to capas folder
                            System.IO.File.Copy(sourcePath, coverPath, true);

                            // Set the cover path in the manga object
                            importedManga.ImagemCapa = $"/uploads/capas/{coverFileName}";
                        }
                        else
                        {
                            // Fallback: use the first page path directly
                            importedManga.ImagemCapa = firstPagePath;
                        }
                    }
                    else
                    {
                        // If no pages were downloaded, use a default placeholder
                        importedManga.ImagemCapa = "/uploads/capas/default.jpg";
                    }

                    // Save manga to database
                    _context.Mangas.Add(importedManga);
                    await _context.SaveChangesAsync();

                    // Create default chapter (Chapter 1) for imported manga
                    var capitulo = new Capitulo
                    {
                        MangaId = importedManga.Id,
                        NumeroCapitulo = 1,
                        NomeCapitulo = null, // Will display as "Chapter 1"
                        DataCriacao = DateTime.Now
                    };
                    _context.Capitulos.Add(capitulo);
                    await _context.SaveChangesAsync();

                    // Create page records in database, associated with the chapter
                    int pageNumber = 1;
                    foreach (var pagePath in pagePaths)
                    {
                        var paginaManga = new PaginaManga
                        {
                            MangaId = importedManga.Id,
                            CapituloId = capitulo.Id,
                            NumeroPagina = pageNumber++,
                            CaminhoImagem = pagePath,
                            DataUpload = DateTime.Now
                        };
                        _context.PaginasMangas.Add(paginaManga);
                    }

                    await _context.SaveChangesAsync();

                    return Json(new
                    {
                        success = true,
                        mangaId = importedManga.Id,
                        titulo = importedManga.Titulo,
                        autor = importedManga.Autor,
                        descricao = importedManga.Descricao,
                        dataCriacao = importedManga.DataCriacao.ToString("yyyy-MM-dd"),
                        totalPages = pagePaths.Count,
                        message = $"Manga '{importedManga.Titulo}' imported successfully with {pagePaths.Count} pages!"
                    });
                }
                else
                {
                    return Json(new { success = false, message = "Failed to import from e-hentai.org. Please check the URL and try again." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// POST: Admin/ImportChapterFromEHentai - AJAX endpoint to import chapter pages from e-hentai URL
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ImportChapterFromEHentai([FromBody] ImportChapterRequest request)
        {
            if (!IsAdmin())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            if (string.IsNullOrEmpty(request?.Url) || request.CapituloId <= 0)
            {
                return Json(new { success = false, message = "URL and Chapter ID are required" });
            }

            try
            {
                // Find the chapter
                var capitulo = await _context.Capitulos
                    .Include(c => c.Manga)
                    .Include(c => c.Paginas)
                    .FirstOrDefaultAsync(c => c.Id == request.CapituloId);

                if (capitulo == null)
                {
                    return Json(new { success = false, message = "Chapter not found" });
                }

                // Import pages using EHentaiImporter
                var importer = new EHentaiImporter(_environment);
                var result = await importer.ImportFromUrl(request.Url);

                if (result.HasValue)
                {
                    var pagePaths = result.Value.pagePaths;

                    if (pagePaths.Count == 0)
                    {
                        return Json(new { success = false, message = "No pages were downloaded from the gallery" });
                    }

                    // Get the starting page number (continue from existing pages)
                    int startingPageNumber = capitulo.Paginas.Any()
                        ? capitulo.Paginas.Max(p => p.NumeroPagina) + 1
                        : 1;

                    // Create page records in database, associated with the chapter
                    int pageNumber = startingPageNumber;
                    foreach (var pagePath in pagePaths)
                    {
                        var paginaManga = new PaginaManga
                        {
                            MangaId = capitulo.MangaId,
                            CapituloId = capitulo.Id,
                            NumeroPagina = pageNumber++,
                            CaminhoImagem = pagePath,
                            DataUpload = DateTime.Now
                        };
                        _context.PaginasMangas.Add(paginaManga);
                    }

                    await _context.SaveChangesAsync();

                    return Json(new
                    {
                        success = true,
                        capituloId = capitulo.Id,
                        nomeCapitulo = capitulo.NomeExibicao,
                        totalPages = pagePaths.Count,
                        message = $"{pagePaths.Count} page(s) imported successfully to {capitulo.NomeExibicao}!"
                    });
                }
                else
                {
                    return Json(new { success = false, message = "Failed to import from e-hentai.org. Please check the URL and try again." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// POST: Admin/CreateManga - Processes the creation of a new manga (CREATE)
        /// </summary>
        /// <param name="manga">Manga object with form data</param>
        /// <param name="imagemCapa">Cover image file (optional)</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateManga(Manga manga, IFormFile? imagemCapa)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (ModelState.IsValid)
            {
                // If a cover image was uploaded, perform the upload
                if (imagemCapa != null && imagemCapa.Length > 0)
                {
                    // Generate unique name to avoid conflicts
                    var fileName = $"{Guid.NewGuid()}_{imagemCapa.FileName}";
                    var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "capas");

                    // Create directory if it doesn't exist
                    if (!Directory.Exists(uploadsDir))
                    {
                        Directory.CreateDirectory(uploadsDir);
                    }

                    var filePath = Path.Combine(uploadsDir, fileName);

                    // Save file to server
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await imagemCapa.CopyToAsync(stream);
                    }

                    // Set relative path to store in database
                    manga.ImagemCapa = $"/uploads/capas/{fileName}";
                }

                // DataCriacao now comes from the form (manga release date)
                _context.Mangas.Add(manga);
                await _context.SaveChangesAsync();

                // Create default chapter (Chapter 1) for the new manga
                var capitulo = new Capitulo
                {
                    MangaId = manga.Id,
                    NumeroCapitulo = 1,
                    DataCriacao = DateTime.Now
                };
                _context.Capitulos.Add(capitulo);
                await _context.SaveChangesAsync();

                // Create manga folder in uploads/paginas for organization
                var mangaFolderName = SanitizeFolderName(manga.Titulo);
                var mangaPagesDir = Path.Combine(_environment.WebRootPath, "uploads", "paginas", mangaFolderName);

                if (!Directory.Exists(mangaPagesDir))
                {
                    Directory.CreateDirectory(mangaPagesDir);
                }

                return RedirectToAction(nameof(Index));
            }

            return View(manga);
        }

        /// <summary>
        /// GET: Admin/EditManga/5 - Displays the manga editing form (UPDATE)
        /// </summary>
        /// <param name="id">ID of the manga to be edited</param>
        public async Task<IActionResult> EditManga(int? id)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var manga = await _context.Mangas.FindAsync(id);
            if (manga == null)
            {
                return NotFound();
            }

            return View(manga);
        }

        /// <summary>
        /// POST: Admin/EditManga/5 - Processes the update of a manga (UPDATE)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditManga(int id, [Bind("Id,Titulo,Autor,Descricao,ImagemCapa,DataCriacao")] Manga manga, IFormFile? imagemCapa)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (id != manga.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // If a new image was uploaded, process the upload
                    if (imagemCapa != null && imagemCapa.Length > 0)
                    {
                        // Delete old image if it exists
                        if (!string.IsNullOrEmpty(manga.ImagemCapa))
                        {
                            var oldImagePath = Path.Combine(_environment.WebRootPath, manga.ImagemCapa.TrimStart('/'));
                            if (System.IO.File.Exists(oldImagePath))
                            {
                                System.IO.File.Delete(oldImagePath);
                            }
                        }

                        var fileName = $"{Guid.NewGuid()}_{imagemCapa.FileName}";
                        var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "capas");

                        // Create directory if it doesn't exist
                        if (!Directory.Exists(uploadsDir))
                        {
                            Directory.CreateDirectory(uploadsDir);
                        }

                        var filePath = Path.Combine(uploadsDir, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await imagemCapa.CopyToAsync(stream);
                        }

                        manga.ImagemCapa = $"/uploads/capas/{fileName}";
                    }
                    // If there's no new image, manga.ImagemCapa already contains the correct value from hidden field

                    _context.Update(manga);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!MangaExists(manga.Id))
                    {
                        return NotFound();
                    }
                    throw;
                }

                return RedirectToAction(nameof(Index));
            }

            return View(manga);
        }

        // GET: Admin/DeleteManga/5
        public async Task<IActionResult> DeleteManga(int? id)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var manga = await _context.Mangas
                .Include(m => m.Paginas)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (manga == null)
            {
                return NotFound();
            }

            return View(manga);
        }

        // POST: Admin/DeleteManga/5
        [HttpPost, ActionName("DeleteManga")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMangaConfirmed(int id)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var manga = await _context.Mangas
                .Include(m => m.Capitulos)
                    .ThenInclude(c => c.Paginas)
                .Include(m => m.Paginas)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (manga != null)
            {
                // Delete cover
                if (!string.IsNullOrEmpty(manga.ImagemCapa))
                {
                    var capaPath = Path.Combine(_environment.WebRootPath, manga.ImagemCapa.TrimStart('/'));
                    if (System.IO.File.Exists(capaPath))
                    {
                        System.IO.File.Delete(capaPath);
                    }
                }

                // Delete entire manga pages folder for better cleanup
                var mangaFolderName = SanitizeFolderName(manga.Titulo);
                var mangaPagesDir = Path.Combine(_environment.WebRootPath, "uploads", "paginas", mangaFolderName);

                if (Directory.Exists(mangaPagesDir))
                {
                    Directory.Delete(mangaPagesDir, true); // true = delete recursively
                }

                // Delete all chapters first (this will cascade delete all pages through Capitulo -> PaginaManga relationship)
                // This is necessary because PaginaManga has two foreign keys to both Manga (Restrict) and Capitulo (Cascade)
                foreach (var capitulo in manga.Capitulos.ToList())
                {
                    _context.Capitulos.Remove(capitulo);
                }

                // Now delete the manga (pages are already deleted via chapter cascade)
                _context.Mangas.Remove(manga);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        // ==================== Chapter Management ====================

        // POST: Admin/CreateCapitulo
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCapitulo(int mangaId, string? nomeCapitulo)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var manga = await _context.Mangas.Include(m => m.Capitulos).FirstOrDefaultAsync(m => m.Id == mangaId);
            if (manga == null)
            {
                return NotFound();
            }

            // Get next chapter number
            var proximoNumero = manga.Capitulos.Any() ? manga.Capitulos.Max(c => c.NumeroCapitulo) + 1 : 1;

            var capitulo = new Capitulo
            {
                MangaId = mangaId,
                NumeroCapitulo = proximoNumero,
                NomeCapitulo = string.IsNullOrWhiteSpace(nomeCapitulo) ? null : nomeCapitulo.Trim(),
                DataCriacao = DateTime.Now
            };

            _context.Capitulos.Add(capitulo);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Chapter {proximoNumero} created successfully!";
            return RedirectToAction(nameof(UploadPages), new { id = mangaId });
        }

        // POST: Admin/EditCapitulo
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCapitulo(int id, string? nomeCapitulo)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var capitulo = await _context.Capitulos.FindAsync(id);
            if (capitulo == null)
            {
                return NotFound();
            }

            capitulo.NomeCapitulo = string.IsNullOrWhiteSpace(nomeCapitulo) ? null : nomeCapitulo.Trim();
            _context.Update(capitulo);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Chapter updated successfully!";
            return RedirectToAction(nameof(UploadPages), new { id = capitulo.MangaId });
        }

        // POST: Admin/DeleteCapitulo
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCapitulo(int id)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var capitulo = await _context.Capitulos
                .Include(c => c.Paginas)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (capitulo == null)
            {
                return NotFound();
            }

            var mangaId = capitulo.MangaId;

            // Check if there are pages in this chapter
            if (capitulo.Paginas.Any())
            {
                TempData["Error"] = "Cannot delete a chapter that has pages. Please delete the pages first.";
                return RedirectToAction(nameof(UploadPages), new { id = mangaId });
            }

            _context.Capitulos.Remove(capitulo);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Chapter deleted successfully!";
            return RedirectToAction(nameof(UploadPages), new { id = mangaId });
        }

        // ==================== Page Management ====================

        // GET: Admin/UploadPages/5
        public async Task<IActionResult> UploadPages(int? id)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var manga = await _context.Mangas
                .Include(m => m.Paginas)
                .Include(m => m.Capitulos.OrderBy(c => c.NumeroCapitulo))
                    .ThenInclude(c => c.Paginas.OrderBy(p => p.NumeroPagina))
                .FirstOrDefaultAsync(m => m.Id == id);
            if (manga == null)
            {
                return NotFound();
            }

            return View(manga);
        }

        // POST: Admin/UploadPages/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadPages(int id, int capituloId, List<IFormFile> paginas)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var manga = await _context.Mangas
                .Include(m => m.Paginas)
                .Include(m => m.Capitulos)
                    .ThenInclude(c => c.Paginas)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (manga == null)
            {
                return NotFound();
            }

            // Get the selected chapter
            var capitulo = manga.Capitulos.FirstOrDefault(c => c.Id == capituloId);
            if (capitulo == null)
            {
                TempData["Error"] = "Chapter not found. Please select a valid chapter.";
                return RedirectToAction(nameof(UploadPages), new { id = id });
            }

            if (paginas != null && paginas.Count > 0)
            {
                // Get the last page number for THIS chapter
                var ultimaPagina = capitulo.Paginas.Any() ? capitulo.Paginas.Max(p => p.NumeroPagina) : 0;

                // Create folder specific to this manga for better organization
                var mangaFolderName = SanitizeFolderName(manga.Titulo);
                var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "paginas", mangaFolderName);

                // Create directory if it doesn't exist
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }

                foreach (var pagina in paginas)
                {
                    if (pagina.Length > 0)
                    {
                        var fileName = $"{Guid.NewGuid()}_{pagina.FileName}";
                        var filePath = Path.Combine(uploadsDir, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await pagina.CopyToAsync(stream);
                        }

                        ultimaPagina++;

                        var paginaManga = new PaginaManga
                        {
                            MangaId = id,
                            CapituloId = capitulo.Id,
                            NumeroPagina = ultimaPagina,
                            CaminhoImagem = $"/uploads/paginas/{mangaFolderName}/{fileName}",
                            DataUpload = DateTime.Now
                        };

                        _context.PaginasMangas.Add(paginaManga);
                    }
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(UploadPages), new { id = id });
        }

        // POST: Admin/DeletePage/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePage(int id, int mangaId)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var pagina = await _context.PaginasMangas.FindAsync(id);
            if (pagina != null)
            {
                var paginaPath = Path.Combine(_environment.WebRootPath, pagina.CaminhoImagem.TrimStart('/'));
                if (System.IO.File.Exists(paginaPath))
                {
                    System.IO.File.Delete(paginaPath);
                }

                _context.PaginasMangas.Remove(pagina);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(UploadPages), new { id = mangaId });
        }

        // GET: Admin/CreateAdmin
        public IActionResult CreateAdmin()
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            return View();
        }

        // POST: Admin/CreateAdmin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAdmin(string nome, string email, string senha, string confirmarSenha)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (string.IsNullOrEmpty(nome) || string.IsNullOrEmpty(email) ||
                string.IsNullOrEmpty(senha) || string.IsNullOrEmpty(confirmarSenha))
            {
                ViewBag.Error = "All fields are required";
                return View();
            }

            if (senha != confirmarSenha)
            {
                ViewBag.Error = "Passwords do not match";
                return View();
            }

            var existingAdmin = await _context.UsuariosAdmin.FirstOrDefaultAsync(u => u.Email == email);
            if (existingAdmin != null)
            {
                ViewBag.Error = "Email already registered";
                return View();
            }

            var admin = new UsuarioAdmin
            {
                Nome = nome,
                Email = email,
                Senha = HashPassword(senha),
                IsAdmin = true,
                DataCriacao = DateTime.Now
            };

            _context.UsuariosAdmin.Add(admin);
            await _context.SaveChangesAsync();

            ViewBag.Success = "Administrator created successfully!";
            return View();
        }

        // GET: Admin/EditAdmin/5
        public async Task<IActionResult> EditAdmin(int? id)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var admin = await _context.UsuariosAdmin.FindAsync(id);
            if (admin == null)
            {
                return NotFound();
            }

            return View(admin);
        }

        // POST: Admin/EditAdmin/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAdmin(int id, string nome, string email, string? senha, string? confirmarSenha)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var admin = await _context.UsuariosAdmin.FindAsync(id);
            if (admin == null)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(nome) || string.IsNullOrEmpty(email))
            {
                ViewBag.Error = "Name and Email are required";
                return View(admin);
            }

            // If password was provided, validate and update
            if (!string.IsNullOrEmpty(senha))
            {
                if (senha != confirmarSenha)
                {
                    ViewBag.Error = "Passwords do not match";
                    return View(admin);
                }

                admin.Senha = HashPassword(senha);
            }

            // Check if email already exists for another admin
            var existingAdmin = await _context.UsuariosAdmin
                .FirstOrDefaultAsync(u => u.Email == email && u.Id != id);

            if (existingAdmin != null)
            {
                ViewBag.Error = "Email already registered for another administrator";
                return View(admin);
            }

            admin.Nome = nome;
            admin.Email = email;

            _context.Update(admin);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: Admin/DeleteAdmin/5
        public async Task<IActionResult> DeleteAdmin(int? id)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var admin = await _context.UsuariosAdmin.FindAsync(id);
            if (admin == null)
            {
                return NotFound();
            }

            return View(admin);
        }

        // POST: Admin/DeleteAdmin/5
        [HttpPost, ActionName("DeleteAdmin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAdminConfirmed(int id)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var admin = await _context.UsuariosAdmin.FindAsync(id);
            if (admin != null)
            {
                // Check if it's not the last admin
                var adminCount = await _context.UsuariosAdmin.CountAsync();
                if (adminCount <= 1)
                {
                    ViewBag.Error = "Cannot delete the last administrator of the system";
                    return View("DeleteAdmin", admin);
                }

                _context.UsuariosAdmin.Remove(admin);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        // ==================== Banner Management ====================

        // GET: Admin/CreateBanner
        public async Task<IActionResult> CreateBanner()
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            ViewBag.Mangas = await _context.Mangas.OrderBy(m => m.Titulo).ToListAsync();
            return View();
        }

        // POST: Admin/CreateBanner
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBanner(string? titulo, string? subtitulo, IFormFile? imagemFile, int ordem, bool ativo = true, int tipo = 0, int? mangaId = null)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            // For Image type, title and image are required
            if (tipo == 0)
            {
                if (string.IsNullOrEmpty(titulo))
                {
                    ViewBag.Error = "Title is required for Image banners";
                    ViewBag.Mangas = await _context.Mangas.OrderBy(m => m.Titulo).ToListAsync();
                    return View();
                }

                if (imagemFile == null || imagemFile.Length == 0)
                {
                    ViewBag.Error = "Image is required for Image banners";
                    ViewBag.Mangas = await _context.Mangas.OrderBy(m => m.Titulo).ToListAsync();
                    return View();
                }
            }

            // Validate manga selection for showcase type
            Manga? manga = null;
            if (tipo == 1)
            {
                if (mangaId == null)
                {
                    ViewBag.Error = "Please select a manga for the Manga Showcase banner";
                    ViewBag.Mangas = await _context.Mangas.OrderBy(m => m.Titulo).ToListAsync();
                    return View();
                }

                manga = await _context.Mangas.FindAsync(mangaId);
                if (manga == null)
                {
                    ViewBag.Error = "Selected manga not found";
                    ViewBag.Mangas = await _context.Mangas.OrderBy(m => m.Titulo).ToListAsync();
                    return View();
                }
            }

            string imagemUrl;

            // If an image was uploaded, save it
            if (imagemFile != null && imagemFile.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(imagemFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    ViewBag.Error = "Only image files are allowed (jpg, jpeg, png, gif, webp)";
                    ViewBag.Mangas = await _context.Mangas.OrderBy(m => m.Titulo).ToListAsync();
                    return View();
                }

                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "carousel");
                Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await imagemFile.CopyToAsync(fileStream);
                }

                imagemUrl = $"/uploads/carousel/{uniqueFileName}";
            }
            else if (tipo == 1 && manga != null)
            {
                // For showcase without uploaded image, use manga cover
                imagemUrl = manga.ImagemCapa;
            }
            else
            {
                ViewBag.Error = "Image is required";
                ViewBag.Mangas = await _context.Mangas.OrderBy(m => m.Titulo).ToListAsync();
                return View();
            }

            var banner = new Banner
            {
                Titulo = tipo == 1 && string.IsNullOrEmpty(titulo) && manga != null ? manga.Titulo : titulo ?? string.Empty,
                Subtitulo = subtitulo,
                ImagemUrl = imagemUrl,
                Ordem = ordem,
                Ativo = ativo,
                DataCriacao = DateTime.Now,
                Tipo = tipo,
                MangaId = tipo == 1 ? mangaId : null
            };

            _context.Banners.Add(banner);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: Admin/EditBanner/5
        public async Task<IActionResult> EditBanner(int? id)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var banner = await _context.Banners.Include(b => b.Manga).FirstOrDefaultAsync(b => b.Id == id);
            if (banner == null)
            {
                return NotFound();
            }

            ViewBag.Mangas = await _context.Mangas.OrderBy(m => m.Titulo).ToListAsync();
            return View(banner);
        }

        // POST: Admin/EditBanner/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditBanner(int id, string? titulo, string? subtitulo, IFormFile? imagemFile, int ordem, bool ativo, int tipo = 0, int? mangaId = null)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var banner = await _context.Banners.FindAsync(id);
            if (banner == null)
            {
                return NotFound();
            }

            // For Image type, title is required
            if (tipo == 0 && string.IsNullOrEmpty(titulo))
            {
                ViewBag.Error = "Title is required for Image banners";
                ViewBag.Mangas = await _context.Mangas.OrderBy(m => m.Titulo).ToListAsync();
                return View(banner);
            }

            // Validate manga selection for showcase type
            Manga? manga = null;
            if (tipo == 1)
            {
                if (mangaId == null)
                {
                    ViewBag.Error = "Please select a manga for the Manga Showcase banner";
                    ViewBag.Mangas = await _context.Mangas.OrderBy(m => m.Titulo).ToListAsync();
                    return View(banner);
                }

                manga = await _context.Mangas.FindAsync(mangaId);
                if (manga == null)
                {
                    ViewBag.Error = "Selected manga not found";
                    ViewBag.Mangas = await _context.Mangas.OrderBy(m => m.Titulo).ToListAsync();
                    return View(banner);
                }
            }

            // If a new image was uploaded, upload it
            if (imagemFile != null && imagemFile.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(imagemFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    ViewBag.Error = "Only image files are allowed (jpg, jpeg, png, gif, webp)";
                    ViewBag.Mangas = await _context.Mangas.OrderBy(m => m.Titulo).ToListAsync();
                    return View(banner);
                }

                // Delete old carousel image if it was uploaded (not a manga cover path)
                if (!string.IsNullOrEmpty(banner.ImagemUrl) && banner.ImagemUrl.StartsWith("/uploads/carousel/"))
                {
                    var oldImagePath = Path.Combine(_environment.WebRootPath, banner.ImagemUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldImagePath))
                    {
                        System.IO.File.Delete(oldImagePath);
                    }
                }

                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "carousel");
                Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await imagemFile.CopyToAsync(fileStream);
                }

                banner.ImagemUrl = $"/uploads/carousel/{uniqueFileName}";
            }
            else if (tipo == 1 && manga != null && string.IsNullOrEmpty(banner.ImagemUrl))
            {
                // If showcase and no existing image, use manga cover
                banner.ImagemUrl = manga.ImagemCapa;
            }

            banner.Titulo = tipo == 1 && string.IsNullOrEmpty(titulo) && manga != null ? manga.Titulo : titulo ?? string.Empty;
            banner.Subtitulo = subtitulo;
            banner.Ordem = ordem;
            banner.Ativo = ativo;
            banner.Tipo = tipo;
            banner.MangaId = tipo == 1 ? mangaId : null;

            _context.Update(banner);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: Admin/DeleteBanner/5
        public async Task<IActionResult> DeleteBanner(int? id)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var banner = await _context.Banners.FindAsync(id);
            if (banner == null)
            {
                return NotFound();
            }

            return View(banner);
        }

        // POST: Admin/DeleteBanner/5
        [HttpPost, ActionName("DeleteBanner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBannerConfirmed(int id)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var banner = await _context.Banners.FindAsync(id);
            if (banner != null)
            {
                // Delete image file if it exists
                if (!string.IsNullOrEmpty(banner.ImagemUrl))
                {
                    var imagePath = Path.Combine(_environment.WebRootPath, banner.ImagemUrl.TrimStart('/'));
                    if (System.IO.File.Exists(imagePath))
                    {
                        System.IO.File.Delete(imagePath);
                    }
                }

                _context.Banners.Remove(banner);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        private bool MangaExists(int id)
        {
            return _context.Mangas.Any(e => e.Id == id);
        }

        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        /// <summary>
        /// Sanitizes manga title to create a safe folder name
        /// Removes special characters and replaces spaces with hyphens
        /// </summary>
        /// <param name="titulo">The manga title to sanitize</param>
        /// <returns>A safe folder name string</returns>
        private string SanitizeFolderName(string titulo)
        {
            if (string.IsNullOrEmpty(titulo))
                return "manga";

            // Replace spaces with hyphens
            var sanitized = titulo.Replace(" ", "-");

            // Remove invalid file system characters
            var invalidChars = Path.GetInvalidFileNameChars();
            sanitized = string.Join("", sanitized.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

            // Remove additional special characters that might cause issues
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[^\w\-]", "");

            // Limit length to avoid path too long issues
            if (sanitized.Length > 50)
                sanitized = sanitized.Substring(0, 50);

            // Remove trailing hyphens
            sanitized = sanitized.TrimEnd('-');

            return string.IsNullOrEmpty(sanitized) ? "manga" : sanitized;
        }

        /// <summary>
        /// POST: Admin/ImportFromMangaDex - AJAX endpoint to import manga from MangaDex URL
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ImportFromMangaDex([FromBody] MangaDexImportRequest request)
        {
            if (!IsAdmin())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            if (string.IsNullOrEmpty(request?.Url))
            {
                return Json(new { success = false, message = "URL is required" });
            }

            try
            {
                var importer = new MangaDexImporter(_environment);
                var result = await importer.ImportFromUrl(
                    request.Url,
                    request.Language ?? "en",
                    request.ChapterRange ?? "all",
                    request.Quality ?? "data"
                );

                if (result.HasValue)
                {
                    var importedManga = result.Value.manga;
                    var chapters = result.Value.chapters;

                    if (chapters.Count == 0)
                    {
                        return Json(new { success = false, message = "No chapters were successfully downloaded" });
                    }

                    // Save manga to database
                    _context.Mangas.Add(importedManga);
                    await _context.SaveChangesAsync();

                    int totalPages = 0;
                    var chapterSummaries = new List<string>();

                    // Create chapters and pages in database
                    foreach (var chapterData in chapters)
                    {
                        // Parse chapter number
                        if (!decimal.TryParse(chapterData.ChapterNumber, out decimal chapterNum))
                        {
                            chapterNum = 0;
                        }

                        // Create chapter
                        var capitulo = new Capitulo
                        {
                            MangaId = importedManga.Id,
                            NumeroCapitulo = (int)Math.Floor(chapterNum),
                            NomeCapitulo = chapterData.ChapterTitle,
                            DataCriacao = DateTime.Now
                        };
                        _context.Capitulos.Add(capitulo);
                        await _context.SaveChangesAsync();

                        // Create pages for this chapter
                        int pageNumber = 1;
                        foreach (var pagePath in chapterData.PagePaths)
                        {
                            var paginaManga = new PaginaManga
                            {
                                MangaId = importedManga.Id,
                                CapituloId = capitulo.Id,
                                NumeroPagina = pageNumber++,
                                CaminhoImagem = pagePath,
                                DataUpload = DateTime.Now
                            };
                            _context.PaginasMangas.Add(paginaManga);
                        }

                        await _context.SaveChangesAsync();

                        totalPages += chapterData.PagePaths.Count;
                        chapterSummaries.Add($"Chapter {chapterData.ChapterNumber}: {chapterData.PagePaths.Count} pages");
                    }

                    return Json(new
                    {
                        success = true,
                        mangaId = importedManga.Id,
                        titulo = importedManga.Titulo,
                        autor = importedManga.Autor,
                        descricao = importedManga.Descricao,
                        totalChapters = chapters.Count,
                        totalPages = totalPages,
                        chapters = chapterSummaries,
                        message = $"Manga '{importedManga.Titulo}' imported successfully with {chapters.Count} chapter(s) and {totalPages} total pages!"
                    });
                }
                else
                {
                    return Json(new { success = false, message = "Failed to import from MangaDex. Please check the URL and try again." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// POST: Admin/ImportFromMangaLivre - AJAX endpoint to import manga from MangaLivre URL
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ImportFromMangaLivre([FromBody] MangaLivreImportRequest request)
        {
            if (!IsAdmin())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            if (string.IsNullOrEmpty(request?.Url))
            {
                return Json(new { success = false, message = "URL is required" });
            }

            try
            {
                var importer = new MangaLivreImporter(_environment);
                var result = await importer.ImportFromUrl(
                    request.Url,
                    request.ChapterRange ?? "all"
                );

                if (result.HasValue)
                {
                    var importedManga = result.Value.manga;
                    var chapters = result.Value.chapters;

                    if (chapters.Count == 0)
                    {
                        return Json(new { success = false, message = "No chapters were successfully downloaded" });
                    }

                    // Save manga to database
                    _context.Mangas.Add(importedManga);
                    await _context.SaveChangesAsync();

                    int totalPages = 0;
                    var chapterSummaries = new List<string>();

                    // Create chapters and pages in database
                    foreach (var chapterData in chapters)
                    {
                        if (!decimal.TryParse(chapterData.ChapterNumber, out decimal chapterNum))
                        {
                            chapterNum = 0;
                        }

                        var capitulo = new Capitulo
                        {
                            MangaId = importedManga.Id,
                            NumeroCapitulo = (int)Math.Floor(chapterNum),
                            NomeCapitulo = chapterData.ChapterTitle,
                            DataCriacao = DateTime.Now
                        };
                        _context.Capitulos.Add(capitulo);
                        await _context.SaveChangesAsync();

                        int pageNumber = 1;
                        foreach (var pagePath in chapterData.PagePaths)
                        {
                            var paginaManga = new PaginaManga
                            {
                                MangaId = importedManga.Id,
                                CapituloId = capitulo.Id,
                                NumeroPagina = pageNumber++,
                                CaminhoImagem = pagePath,
                                DataUpload = DateTime.Now
                            };
                            _context.PaginasMangas.Add(paginaManga);
                        }

                        await _context.SaveChangesAsync();

                        totalPages += chapterData.PagePaths.Count;
                        chapterSummaries.Add($"Chapter {chapterData.ChapterNumber}: {chapterData.PagePaths.Count} pages");
                    }

                    return Json(new
                    {
                        success = true,
                        mangaId = importedManga.Id,
                        titulo = importedManga.Titulo,
                        autor = importedManga.Autor,
                        descricao = importedManga.Descricao,
                        totalChapters = chapters.Count,
                        totalPages = totalPages,
                        chapters = chapterSummaries,
                        message = $"Manga '{importedManga.Titulo}' imported successfully with {chapters.Count} chapter(s) and {totalPages} total pages!"
                    });
                }
                else
                {
                    return Json(new { success = false, message = "Failed to import from MangaLivre. Please check the URL and try again." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// POST: Admin/ImportFromWeebCentral - AJAX endpoint to import manga from WeebCentral URL
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ImportFromWeebCentral([FromBody] WeebCentralImportRequest request)
        {
            if (!IsAdmin())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            if (string.IsNullOrEmpty(request?.Url))
            {
                return Json(new { success = false, message = "URL is required" });
            }

            try
            {
                var importer = new WeebCentralImporter(_environment, request.ProxyUrl);
                var result = await importer.ImportFromUrl(
                    request.Url,
                    request.ChapterRange ?? "all"
                );

                if (result.HasValue)
                {
                    var importedManga = result.Value.manga;
                    var chapters = result.Value.chapters;

                    if (chapters.Count == 0)
                    {
                        return Json(new { success = false, message = "No chapters were successfully downloaded" });
                    }

                    // Save manga to database
                    _context.Mangas.Add(importedManga);
                    await _context.SaveChangesAsync();

                    int totalPages = 0;
                    var chapterSummaries = new List<string>();

                    // Create chapters and pages in database
                    foreach (var chapterData in chapters)
                    {
                        if (!decimal.TryParse(chapterData.ChapterNumber, out decimal chapterNum))
                        {
                            chapterNum = 0;
                        }

                        var capitulo = new Capitulo
                        {
                            MangaId = importedManga.Id,
                            NumeroCapitulo = (int)Math.Floor(chapterNum),
                            NomeCapitulo = chapterData.ChapterTitle,
                            DataCriacao = DateTime.Now
                        };
                        _context.Capitulos.Add(capitulo);
                        await _context.SaveChangesAsync();

                        int pageNumber = 1;
                        foreach (var pagePath in chapterData.PagePaths)
                        {
                            var paginaManga = new PaginaManga
                            {
                                MangaId = importedManga.Id,
                                CapituloId = capitulo.Id,
                                NumeroPagina = pageNumber++,
                                CaminhoImagem = pagePath,
                                DataUpload = DateTime.Now
                            };
                            _context.PaginasMangas.Add(paginaManga);
                        }

                        await _context.SaveChangesAsync();

                        totalPages += chapterData.PagePaths.Count;
                        chapterSummaries.Add($"Chapter {chapterData.ChapterNumber}: {chapterData.PagePaths.Count} pages");
                    }

                    return Json(new
                    {
                        success = true,
                        mangaId = importedManga.Id,
                        titulo = importedManga.Titulo,
                        autor = importedManga.Autor,
                        descricao = importedManga.Descricao,
                        totalChapters = chapters.Count,
                        totalPages = totalPages,
                        chapters = chapterSummaries,
                        message = $"Manga '{importedManga.Titulo}' imported successfully with {chapters.Count} chapter(s) and {totalPages} total pages!"
                    });
                }
                else
                {
                    return Json(new { success = false, message = "Failed to import from WeebCentral. Please check the URL and try again." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

    }

    /// <summary>
    /// Request model for e-hentai import
    /// </summary>
    public class ImportRequest
    {
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request model for e-hentai chapter import
    /// </summary>
    public class ImportChapterRequest
    {
        public string Url { get; set; } = string.Empty;
        public int CapituloId { get; set; }
    }

    /// <summary>
    /// Request model for MangaDex import
    /// </summary>
    public class MangaDexImportRequest
    {
        public string Url { get; set; } = string.Empty;
        public string? Language { get; set; }
        public string? ChapterRange { get; set; }
        public string? Quality { get; set; }
    }

    /// <summary>
    /// Request model for MangaLivre import
    /// </summary>
    public class MangaLivreImportRequest
    {
        public string Url { get; set; } = string.Empty;
        public string? ChapterRange { get; set; }
    }

    /// <summary>
    /// Request model for WeebCentral import
    /// </summary>
    public class WeebCentralImportRequest
    {
        public string Url { get; set; } = string.Empty;
        public string? ChapterRange { get; set; }
        public string? ProxyUrl { get; set; }
    }

}
