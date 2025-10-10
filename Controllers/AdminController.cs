using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AnyComic.Data;
using AnyComic.Models;
using System.Security.Cryptography;
using System.Text;

namespace AnyComic.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public AdminController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        private bool IsAdmin()
        {
            return User.Claims.FirstOrDefault(c => c.Type == "IsAdmin")?.Value == "True";
        }

        // GET: Admin
        public async Task<IActionResult> Index()
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var mangas = await _context.Mangas.Include(m => m.Paginas).ToListAsync();
            return View(mangas);
        }

        // GET: Admin/CreateManga
        public IActionResult CreateManga()
        {
            if (!IsAdmin())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            return View();
        }

        // POST: Admin/CreateManga
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
                if (imagemCapa != null && imagemCapa.Length > 0)
                {
                    var fileName = $"{Guid.NewGuid()}_{imagemCapa.FileName}";
                    var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "capas");

                    // Criar diretório se não existir
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

                manga.DataCriacao = DateTime.Now;
                _context.Mangas.Add(manga);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View(manga);
        }

        // GET: Admin/EditManga/5
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

        // POST: Admin/EditManga/5
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
                    // Se uma nova imagem foi enviada, processa o upload
                    if (imagemCapa != null && imagemCapa.Length > 0)
                    {
                        // Deletar imagem antiga se existir
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

                        // Criar diretório se não existir
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
                    // Se não houver nova imagem, manga.ImagemCapa já contém o valor correto do campo hidden

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
                // Deletar capa
                if (!string.IsNullOrEmpty(manga.ImagemCapa))
                {
                    var capaPath = Path.Combine(_environment.WebRootPath, manga.ImagemCapa.TrimStart('/'));
                    if (System.IO.File.Exists(capaPath))
                    {
                        System.IO.File.Delete(capaPath);
                    }
                }

                // Deletar páginas
                foreach (var pagina in manga.Paginas)
                {
                    var paginaPath = Path.Combine(_environment.WebRootPath, pagina.CaminhoImagem.TrimStart('/'));
                    if (System.IO.File.Exists(paginaPath))
                    {
                        System.IO.File.Delete(paginaPath);
                    }
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

                var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads", "paginas");

                // Criar diretório se não existir
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
                            CaminhoImagem = $"/uploads/paginas/{fileName}",
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
                ViewBag.Error = "Todos os campos são obrigatórios";
                return View();
            }

            if (senha != confirmarSenha)
            {
                ViewBag.Error = "As senhas não coincidem";
                return View();
            }

            var existingAdmin = await _context.UsuariosAdmin.FirstOrDefaultAsync(u => u.Email == email);
            if (existingAdmin != null)
            {
                ViewBag.Error = "Email já cadastrado";
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

            ViewBag.Success = "Administrador criado com sucesso!";
            return View();
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
    }
}
