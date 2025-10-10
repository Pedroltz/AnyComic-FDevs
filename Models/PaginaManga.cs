using System.ComponentModel.DataAnnotations;

namespace AnyComic.Models
{
    public class PaginaManga
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int MangaId { get; set; }

        [Required]
        public int NumeroPagina { get; set; }

        [Required]
        [StringLength(500)]
        public string CaminhoImagem { get; set; } = string.Empty;

        public DateTime DataUpload { get; set; } = DateTime.Now;

        // Relacionamento com Manga
        public Manga? Manga { get; set; }
    }
}
