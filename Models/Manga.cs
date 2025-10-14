using System.ComponentModel.DataAnnotations;

namespace AnyComic.Models
{
    /// <summary>
    /// Representa um mangá no sistema.
    /// Esta é a entidade principal do CRUD administrativo.
    /// </summary>
    public class Manga
    {
        /// <summary>
        /// Identificador único do mangá (chave primária)
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Título do mangá (obrigatório, máximo 200 caracteres)
        /// </summary>
        [Required(ErrorMessage = "O título é obrigatório")]
        [StringLength(200)]
        public string Titulo { get; set; } = string.Empty;

        /// <summary>
        /// Nome do autor do mangá (obrigatório, máximo 100 caracteres)
        /// </summary>
        [Required(ErrorMessage = "O autor é obrigatório")]
        [StringLength(100)]
        public string Autor { get; set; } = string.Empty;

        /// <summary>
        /// Descrição ou sinopse do mangá (obrigatório, máximo 1000 caracteres)
        /// </summary>
        [Required(ErrorMessage = "A descrição é obrigatória")]
        [StringLength(1000)]
        public string Descricao { get; set; } = string.Empty;

        /// <summary>
        /// Caminho relativo da imagem de capa (máximo 500 caracteres)
        /// Exemplo: /uploads/capas/imagem.jpg
        /// </summary>
        [StringLength(500)]
        public string ImagemCapa { get; set; } = string.Empty;

        /// <summary>
        /// Data de criação do registro no sistema
        /// </summary>
        public DateTime DataCriacao { get; set; } = DateTime.Now;

        // Relacionamentos (Navigation Properties)

        /// <summary>
        /// Coleção de páginas associadas a este mangá
        /// Relacionamento 1:N (um mangá tem muitas páginas)
        /// </summary>
        public ICollection<PaginaManga> Paginas { get; set; } = new List<PaginaManga>();

        /// <summary>
        /// Coleção de favoritos associados a este mangá
        /// Relacionamento N:N através da tabela Favoritos
        /// </summary>
        public ICollection<Favorito> Favoritos { get; set; } = new List<Favorito>();
    }
}
