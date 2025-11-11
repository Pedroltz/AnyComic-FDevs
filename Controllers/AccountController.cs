using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AnyComic.Data;
using AnyComic.Models;

namespace AnyComic.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Account/Login
        public IActionResult Login()
        {
            return View();
        }

        // POST: Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string senha, bool isAdmin = false)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(senha))
            {
                ViewBag.Error = "Email and password are required";
                return View();
            }

            var senhaHash = HashPassword(senha);

            if (isAdmin)
            {
                var admin = await _context.UsuariosAdmin
                    .FirstOrDefaultAsync(u => u.Email == email && u.Senha == senhaHash);

                if (admin == null)
                {
                    ViewBag.Error = "Invalid email or password";
                    return View();
                }

                await SignInUser(admin.Id.ToString(), admin.Nome, admin.Email, true);
                return RedirectToAction("Index", "Admin");
            }
            else
            {
                var usuario = await _context.Usuarios
                    .FirstOrDefaultAsync(u => u.Email == email && u.Senha == senhaHash);

                if (usuario == null)
                {
                    ViewBag.Error = "Invalid email or password";
                    return View();
                }

                await SignInUser(usuario.Id.ToString(), usuario.Nome, usuario.Email, false);
                return RedirectToAction("Index", "Home");
            }
        }

        // GET: Account/Register
        public IActionResult Register()
        {
            return View();
        }

        // POST: Account/Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string nome, string email, string senha, string confirmarSenha)
        {
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

            var existingUser = await _context.Usuarios.FirstOrDefaultAsync(u => u.Email == email);
            if (existingUser != null)
            {
                ViewBag.Error = "Email already registered";
                return View();
            }

            var usuario = new Usuario
            {
                Nome = nome,
                Email = email,
                Senha = HashPassword(senha),
                DataCriacao = DateTime.Now
            };

            _context.Usuarios.Add(usuario);
            await _context.SaveChangesAsync();

            await SignInUser(usuario.Id.ToString(), usuario.Nome, usuario.Email, false);
            return RedirectToAction("Index", "Home");
        }

        // POST: Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        // GET: Account/AccessDenied
        public IActionResult AccessDenied()
        {
            return View();
        }

        private async Task SignInUser(string userId, string nome, string email, bool isAdmin)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, nome),
                new Claim(ClaimTypes.Email, email),
                new Claim("IsAdmin", isAdmin.ToString())
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);
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
