# Diagrama de Classes - AnyComic

Este diagrama representa a estrutura de classes do sistema AnyComic, mostrando as entidades principais e seus relacionamentos.

```mermaid
classDiagram
    class Manga {
        +int Id
        +string Titulo
        +string Autor
        +string Descricao
        +string ImagemCapa
        +DateTime DataCriacao
        +ICollection~Capitulo~ Capitulos
        +ICollection~PaginaManga~ Paginas
        +ICollection~Favorito~ Favoritos
    }

    class Capitulo {
        +int Id
        +int MangaId
        +int NumeroCapitulo
        +string? NomeCapitulo
        +DateTime DataCriacao
        +Manga Manga
        +ICollection~PaginaManga~ Paginas
        +string NomeExibicao
    }

    class PaginaManga {
        +int Id
        +int MangaId
        +int CapituloId
        +int NumeroPagina
        +string CaminhoImagem
        +DateTime DataUpload
        +Manga Manga
        +Capitulo Capitulo
    }

    class Usuario {
        +int Id
        +string Nome
        +string Email
        +string Senha
        +DateTime DataCriacao
        +ICollection~Favorito~ Favoritos
    }

    class UsuarioAdmin {
        +int Id
        +string Nome
        +string Email
        +string Senha
        +bool IsAdmin
        +DateTime DataCriacao
    }

    class Favorito {
        +int Id
        +int UsuarioId
        +int MangaId
        +DateTime DataAdicionado
        +Usuario Usuario
        +Manga Manga
    }

    class Banner {
        +int Id
        +string Titulo
        +string? Subtitulo
        +string ImagemUrl
        +int Ordem
        +bool Ativo
        +DateTime DataCriacao
    }

    class ApplicationDbContext {
        +DbSet~Manga~ Mangas
        +DbSet~Capitulo~ Capitulos
        +DbSet~PaginaManga~ PaginasMangas
        +DbSet~Usuario~ Usuarios
        +DbSet~UsuarioAdmin~ UsuariosAdmin
        +DbSet~Favorito~ Favoritos
        +DbSet~Banner~ Banners
        +OnModelCreating()
    }

    %% Relacionamentos
    Manga "1" --> "*" Capitulo : possui
    Manga "1" --> "*" PaginaManga : contém
    Manga "1" --> "*" Favorito : tem
    Capitulo "1" --> "*" PaginaManga : possui
    Usuario "1" --> "*" Favorito : marca
    ApplicationDbContext --> Manga : gerencia
    ApplicationDbContext --> Capitulo : gerencia
    ApplicationDbContext --> PaginaManga : gerencia
    ApplicationDbContext --> Usuario : gerencia
    ApplicationDbContext --> UsuarioAdmin : gerencia
    ApplicationDbContext --> Favorito : gerencia
    ApplicationDbContext --> Banner : gerencia
```

## Descrição dos Relacionamentos:

- **Manga -> Capitulo**: Um manga possui vários capítulos (1:N)
- **Manga -> PaginaManga**: Um manga contém várias páginas (1:N, Restrict delete)
- **Capitulo -> PaginaManga**: Um capítulo possui várias páginas (1:N, Cascade delete)
- **Manga -> Favorito**: Um manga pode estar nos favoritos de vários usuários (1:N)
- **Usuario -> Favorito**: Um usuário pode ter vários mangas favoritos (1:N)
- **ApplicationDbContext**: Gerencia todas as entidades através do Entity Framework Core

## Como visualizar este diagrama:

1. **GitHub/GitLab**: Visualize este arquivo diretamente no repositório
2. **Mermaid Live Editor**: https://mermaid.live/ (cole o código)
3. **VS Code**: Instale a extensão "Markdown Preview Mermaid Support"
