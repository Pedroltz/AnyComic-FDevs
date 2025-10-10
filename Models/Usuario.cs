using System.ComponentModel.DataAnnotations;

namespace AnyComic.Models
{
    public class Usuario
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "O nome é obrigatório")]
        [StringLength(100)]
        public string Nome { get; set; } = string.Empty;

        [Required(ErrorMessage = "O email é obrigatório")]
        [EmailAddress(ErrorMessage = "Email inválido")]
        [StringLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "A senha é obrigatória")]
        [StringLength(255)]
        public string Senha { get; set; } = string.Empty;

        public DateTime DataCriacao { get; set; } = DateTime.Now;

        // Relacionamento com Favoritos
        public ICollection<Favorito> Favoritos { get; set; } = new List<Favorito>();
    }
}
