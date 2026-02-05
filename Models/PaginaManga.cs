using System.ComponentModel.DataAnnotations;

namespace AnyComic.Models
{
    public class PaginaManga
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int MangaId { get; set; }

        /// <summary>
        /// Foreign key to the chapter this page belongs to
        /// </summary>
        [Required]
        public int CapituloId { get; set; }

        [Required]
        public int NumeroPagina { get; set; }

        [Required]
        [StringLength(500)]
        public string CaminhoImagem { get; set; } = string.Empty;

        public DateTime DataUpload { get; set; } = DateTime.Now;

        // Relationships

        /// <summary>
        /// The manga this page belongs to
        /// </summary>
        public Manga? Manga { get; set; }

        /// <summary>
        /// The chapter this page belongs to
        /// </summary>
        public Capitulo? Capitulo { get; set; }
    }
}
