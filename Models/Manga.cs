using System.ComponentModel.DataAnnotations;

namespace AnyComic.Models
{
    public class Manga
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "O título é obrigatório")]
        [StringLength(200)]
        public string Titulo { get; set; } = string.Empty;

        [Required(ErrorMessage = "O autor é obrigatório")]
        [StringLength(100)]
        public string Autor { get; set; } = string.Empty;

        [Required(ErrorMessage = "A descrição é obrigatória")]
        [StringLength(1000)]
        public string Descricao { get; set; } = string.Empty;

        [StringLength(500)]
        public string ImagemCapa { get; set; } = string.Empty;

        public DateTime DataCriacao { get; set; } = DateTime.Now;

        // Relacionamento com Páginas e Favoritos
        public ICollection<PaginaManga> Paginas { get; set; } = new List<PaginaManga>();
        public ICollection<Favorito> Favoritos { get; set; } = new List<Favorito>();
    }
}
