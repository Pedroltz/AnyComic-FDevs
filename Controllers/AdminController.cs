using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AnyComic.Data;
using AnyComic.Models;
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

            var manga = await _context.Mangas.Include(m => m.Paginas).FirstOrDefaultAsync(m => m.Id == id);
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

                _context.Mangas.Remove(manga);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

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

            var manga = await _context.Mangas.Include(m => m.Paginas).FirstOrDefaultAsync(m => m.Id == id);
            if (manga == null)
            {
                return NotFound();
            }

            return View(manga);
        }

        // POST: Admin/UploadPages/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadPages(int id, List<IFormFile> paginas)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var manga = await _context.Mangas.Include(m => m.Paginas).FirstOrDefaultAsync(m => m.Id == id);
            if (manga == null)
            {
                return NotFound();
            }

            if (paginas != null && paginas.Count > 0)
            {
                var ultimaPagina = manga.Paginas.Any() ? manga.Paginas.Max(p => p.NumeroPagina) : 0;

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
        public IActionResult CreateBanner()
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            return View();
        }

        // POST: Admin/CreateBanner
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBanner(string titulo, string? subtitulo, IFormFile? imagemFile, int ordem, bool ativo = true)
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (string.IsNullOrEmpty(titulo))
            {
                ViewBag.Error = "Title is required";
                return View();
            }

            if (imagemFile == null || imagemFile.Length == 0)
            {
                ViewBag.Error = "Image is required";
                return View();
            }

            // Validate file type
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var extension = Path.GetExtension(imagemFile.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                ViewBag.Error = "Only image files are allowed (jpg, jpeg, png, gif, webp)";
                return View();
            }

            // Save file
            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "carousel");
            Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await imagemFile.CopyToAsync(fileStream);
            }

            var banner = new Banner
            {
                Titulo = titulo,
                Subtitulo = subtitulo,
                ImagemUrl = $"/uploads/carousel/{uniqueFileName}",
                Ordem = ordem,
                Ativo = ativo,
                DataCriacao = DateTime.Now
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

            var banner = await _context.Banners.FindAsync(id);
            if (banner == null)
            {
                return NotFound();
            }

            return View(banner);
        }

        // POST: Admin/EditBanner/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditBanner(int id, string titulo, string? subtitulo, IFormFile? imagemFile, int ordem, bool ativo)
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

            if (string.IsNullOrEmpty(titulo))
            {
                ViewBag.Error = "Title is required";
                return View(banner);
            }

            // If a new image was uploaded, upload it
            if (imagemFile != null && imagemFile.Length > 0)
            {
                // Validate file type
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(imagemFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    ViewBag.Error = "Only image files are allowed (jpg, jpeg, png, gif, webp)";
                    return View(banner);
                }

                // Delete old image if it exists
                if (!string.IsNullOrEmpty(banner.ImagemUrl))
                {
                    var oldImagePath = Path.Combine(_environment.WebRootPath, banner.ImagemUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldImagePath))
                    {
                        System.IO.File.Delete(oldImagePath);
                    }
                }

                // Save new image
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

            banner.Titulo = titulo;
            banner.Subtitulo = subtitulo;
            banner.Ordem = ordem;
            banner.Ativo = ativo;

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
    }
}
