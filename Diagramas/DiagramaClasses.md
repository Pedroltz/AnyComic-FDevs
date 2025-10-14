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
        +ICollection~PaginaManga~ Paginas
        +ICollection~Favorito~ Favoritos
    }

    class PaginaManga {
        +int Id
        +int MangaId
        +int NumeroPagina
        +string CaminhoImagem
        +DateTime DataUpload
        +Manga Manga
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

    class ApplicationDbContext {
        +DbSet~Manga~ Mangas
        +DbSet~PaginaManga~ PaginasMangas
        +DbSet~Usuario~ Usuarios
        +DbSet~UsuarioAdmin~ UsuariosAdmin
        +DbSet~Favorito~ Favoritos
        +OnModelCreating()
    }

    %% Relacionamentos
    Manga "1" --> "*" PaginaManga : possui
    Manga "1" --> "*" Favorito : tem
    Usuario "1" --> "*" Favorito : marca
    ApplicationDbContext --> Manga : gerencia
    ApplicationDbContext --> PaginaManga : gerencia
    ApplicationDbContext --> Usuario : gerencia
    ApplicationDbContext --> UsuarioAdmin : gerencia
    ApplicationDbContext --> Favorito : gerencia
```

## Descrição dos Relacionamentos:

- **Manga → PaginaManga**: Um mangá possui várias páginas (1:N)
- **Manga → Favorito**: Um mangá pode estar nos favoritos de vários usuários (1:N)
- **Usuario → Favorito**: Um usuário pode ter vários mangás favoritos (1:N)
- **ApplicationDbContext**: Gerencia todas as entidades através do Entity Framework Core

## Como visualizar este diagrama:

1. **GitHub/GitLab**: Visualize este arquivo diretamente no repositório
2. **Mermaid Live Editor**: https://mermaid.live/ (cole o código)
3. **VS Code**: Instale a extensão "Markdown Preview Mermaid Support"
