using System.ComponentModel.DataAnnotations;

namespace AnyComic.Models
{
    public class Favorito
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UsuarioId { get; set; }

        [Required]
        public int MangaId { get; set; }

        public DateTime DataAdicao { get; set; } = DateTime.Now;

        // Relacionamentos
        public Usuario? Usuario { get; set; }
        public Manga? Manga { get; set; }
    }
}
