using AnyComic.Models;
using System.Security.Cryptography;
using System.Text;

namespace AnyComic.Data
{
    public static class DbInitializer
    {
        public static void Initialize(ApplicationDbContext context)
        {
            context.Database.EnsureCreated();

            // Verificar se já existe um administrador
            if (context.UsuariosAdmin.Any())
            {
                return; // BD já foi inicializado
            }

            // Criar administrador padrão
            var adminPassword = HashPassword("admin123");
            var admin = new UsuarioAdmin
            {
                Nome = "Administrador",
                Email = "admin@anycomic.com",
                Senha = adminPassword,
                IsAdmin = true,
                DataCriacao = DateTime.Now
            };

            context.UsuariosAdmin.Add(admin);

            // Criar alguns usuários de teste (opcional)
            var userPassword = HashPassword("user123");
            var usuario1 = new Usuario
            {
                Nome = "João Silva",
                Email = "joao@example.com",
                Senha = userPassword,
                DataCriacao = DateTime.Now
            };

            var usuario2 = new Usuario
            {
                Nome = "Maria Santos",
                Email = "maria@example.com",
                Senha = userPassword,
                DataCriacao = DateTime.Now
            };

            context.Usuarios.Add(usuario1);
            context.Usuarios.Add(usuario2);

            context.SaveChanges();
        }

        private static string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }
    }
}
