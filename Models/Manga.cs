using System.ComponentModel.DataAnnotations;

namespace AnyComic.Models
{
    /// <summary>
    /// Represents a manga in the system.
    /// This is the main entity of the administrative CRUD.
    /// </summary>
    public class Manga
    {
        /// <summary>
        /// Unique identifier of the manga (primary key)
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Title of the manga (required, maximum 200 characters)
        /// </summary>
        [Required(ErrorMessage = "The title is required")]
        [StringLength(200)]
        public string Titulo { get; set; } = string.Empty;

        /// <summary>
        /// Name of the manga author (required, maximum 100 characters)
        /// </summary>
        [Required(ErrorMessage = "The author is required")]
        [StringLength(100)]
        public string Autor { get; set; } = string.Empty;

        /// <summary>
        /// Description or synopsis of the manga (required, maximum 1000 characters)
        /// </summary>
        [Required(ErrorMessage = "The description is required")]
        [StringLength(1000)]
        public string Descricao { get; set; } = string.Empty;

        /// <summary>
        /// Relative path of the cover image (maximum 500 characters)
        /// Example: /uploads/capas/imagem.jpg
        /// </summary>
        [StringLength(500)]
        public string ImagemCapa { get; set; } = string.Empty;

        /// <summary>
        /// Release date of the manga (provided by the administrator)
        /// </summary>
        [Required(ErrorMessage = "The release date is required")]
        public DateTime DataCriacao { get; set; }

        // Relationships (Navigation Properties)

        /// <summary>
        /// Collection of pages associated with this manga
        /// 1:N relationship (one manga has many pages)
        /// </summary>
        public ICollection<PaginaManga> Paginas { get; set; } = new List<PaginaManga>();

        /// <summary>
        /// Collection of favorites associated with this manga
        /// N:N relationship through the Favoritos table
        /// </summary>
        public ICollection<Favorito> Favoritos { get; set; } = new List<Favorito>();
    }
}
