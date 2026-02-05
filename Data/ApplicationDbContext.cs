using Microsoft.EntityFrameworkCore;
using AnyComic.Models;

namespace AnyComic.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<UsuarioAdmin> UsuariosAdmin { get; set; }
        public DbSet<Manga> Mangas { get; set; }
        public DbSet<Capitulo> Capitulos { get; set; }
        public DbSet<PaginaManga> PaginasMangas { get; set; }
        public DbSet<Favorito> Favoritos { get; set; }
        public DbSet<Banner> Banners { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configurar relacionamento Usuario - Favorito
            modelBuilder.Entity<Favorito>()
                .HasOne(f => f.Usuario)
                .WithMany(u => u.Favoritos)
                .HasForeignKey(f => f.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configurar relacionamento Manga - Favorito
            modelBuilder.Entity<Favorito>()
                .HasOne(f => f.Manga)
                .WithMany(m => m.Favoritos)
                .HasForeignKey(f => f.MangaId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configurar relacionamento Manga - Capitulo
            modelBuilder.Entity<Capitulo>()
                .HasOne(c => c.Manga)
                .WithMany(m => m.Capitulos)
                .HasForeignKey(c => c.MangaId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configurar relacionamento Manga - PaginaManga
            modelBuilder.Entity<PaginaManga>()
                .HasOne(p => p.Manga)
                .WithMany(m => m.Paginas)
                .HasForeignKey(p => p.MangaId)
                .OnDelete(DeleteBehavior.Restrict); // Changed to Restrict to avoid multiple cascade paths

            // Configurar relacionamento Capitulo - PaginaManga
            modelBuilder.Entity<PaginaManga>()
                .HasOne(p => p.Capitulo)
                .WithMany(c => c.Paginas)
                .HasForeignKey(p => p.CapituloId)
                .OnDelete(DeleteBehavior.Cascade);

            // Criar índices únicos
            modelBuilder.Entity<Usuario>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<UsuarioAdmin>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // Evitar favoritos duplicados
            modelBuilder.Entity<Favorito>()
                .HasIndex(f => new { f.UsuarioId, f.MangaId })
                .IsUnique();
        }
    }
}
