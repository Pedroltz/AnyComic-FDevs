using System.ComponentModel.DataAnnotations;

namespace AnyComic.Models
{
    /// <summary>
    /// Model representing a banner for the home page carousel
    /// </summary>
    public class Banner
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "The title is required")]
        [StringLength(100, ErrorMessage = "The title must be at most 100 characters")]
        public string Titulo { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "The subtitle must be at most 200 characters")]
        public string? Subtitulo { get; set; }

        [Required(ErrorMessage = "The image URL is required")]
        [StringLength(500, ErrorMessage = "The image URL must be at most 500 characters")]
        public string ImagemUrl { get; set; } = string.Empty;

        /// <summary>
        /// Display order of the banner (lower number appears first)
        /// </summary>
        [Required]
        public int Ordem { get; set; }

        /// <summary>
        /// Indicates if the banner is active and should be displayed
        /// </summary>
        [Required]
        public bool Ativo { get; set; } = true;

        /// <summary>
        /// Creation date of the banner
        /// </summary>
        [Required]
        public DateTime DataCriacao { get; set; } = DateTime.Now;

        /// <summary>
        /// Banner type: 0 = Image (default), 1 = Manga Showcase
        /// </summary>
        public int Tipo { get; set; } = 0;

        /// <summary>
        /// Foreign key to Manga (used when Tipo = 1)
        /// </summary>
        public int? MangaId { get; set; }

        /// <summary>
        /// Navigation property to the associated Manga
        /// </summary>
        public Manga? Manga { get; set; }
    }
}
