using HtmlAgilityPack;
using AnyComic.Models;
using System.Text.RegularExpressions;

namespace AnyComic.Services
{
    /// <summary>
    /// Service responsible for importing manga data from mangalivre.blog
    /// Uses HTML scraping via HtmlAgilityPack
    /// </summary>
    public class MangaLivreImporter
    {
        private readonly HttpClient _httpClient;
        private readonly IWebHostEnvironment _environment;

        public MangaLivreImporter(IWebHostEnvironment environment)
        {
            _environment = environment;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        #region DTOs

        public class ChapterImportData
        {
            public string ChapterNumber { get; set; } = string.Empty;
            public string? ChapterTitle { get; set; }
            public List<string> PagePaths { get; set; } = new();
        }

        private class ChapterInfo
        {
            public string Url { get; set; } = string.Empty;
            public string Number { get; set; } = string.Empty;
            public string? Title { get; set; }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Imports manga from mangalivre.blog URL with chapter selection
        /// </summary>
        public async Task<(Manga manga, List<ChapterImportData> chapters)?> ImportFromUrl(
            string url,
            string chapterRange = "all")
        {
            try
            {
                if (!IsValidMangaLivreUrl(url))
                {
                    Console.WriteLine("Invalid MangaLivre URL");
                    return null;
                }

                // Fetch the manga page
                var html = await _httpClient.GetStringAsync(url);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                // Extract metadata
                var manga = ExtractMangaMetadata(htmlDoc);
                if (manga == null)
                {
                    Console.WriteLine("Failed to extract manga metadata");
                    return null;
                }

                // Download cover image
                var coverUrl = ExtractCoverUrl(htmlDoc);
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    var coverPath = await DownloadCoverImage(coverUrl, manga.Titulo);
                    if (coverPath != null)
                    {
                        manga.ImagemCapa = coverPath;
                    }
                }

                // Extract chapter list
                var allChapters = ExtractChapterList(htmlDoc);
                if (allChapters.Count == 0)
                {
                    Console.WriteLine("No chapters found");
                    return null;
                }

                Console.WriteLine($"Found {allChapters.Count} chapters");

                // Filter chapters by range
                var selectedChapters = FilterChaptersByRange(allChapters, chapterRange);
                if (selectedChapters.Count == 0)
                {
                    Console.WriteLine($"No chapters match the range: {chapterRange}");
                    return null;
                }

                Console.WriteLine($"Selected {selectedChapters.Count} chapters to import");

                // Download each chapter
                var importedChapters = new List<ChapterImportData>();
                int chapterIndex = 1;

                foreach (var chapter in selectedChapters.OrderBy(c => ParseChapterNumber(c.Number)))
                {
                    try
                    {
                        Console.WriteLine($"Downloading chapter {chapterIndex}/{selectedChapters.Count}: {chapter.Number}");

                        var pagePaths = await DownloadChapterPages(chapter.Url, manga.Titulo, chapter.Number);

                        if (pagePaths.Count > 0)
                        {
                            importedChapters.Add(new ChapterImportData
                            {
                                ChapterNumber = chapter.Number,
                                ChapterTitle = chapter.Title,
                                PagePaths = pagePaths
                            });

                            Console.WriteLine($"  Downloaded {pagePaths.Count} pages");
                        }
                        else
                        {
                            Console.WriteLine($"  Failed to download chapter {chapter.Number}");
                        }

                        chapterIndex++;
                        await Task.Delay(1000);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error downloading chapter {chapter.Number}: {ex.Message}");
                    }
                }

                if (importedChapters.Count == 0)
                {
                    Console.WriteLine("No chapters were successfully downloaded");
                    return null;
                }

                return (manga, importedChapters);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error importing from MangaLivre: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Private Methods - Validation

        private bool IsValidMangaLivreUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return url.Contains("mangalivre.blog/manga/");
        }

        #endregion

        #region Private Methods - Metadata Extraction

        private Manga? ExtractMangaMetadata(HtmlDocument htmlDoc)
        {
            try
            {
                // Extract title from .manga-title or <h1>
                var titleNode = htmlDoc.DocumentNode.SelectSingleNode("//h1[contains(@class, 'manga-title')]");
                if (titleNode == null)
                {
                    titleNode = htmlDoc.DocumentNode.SelectSingleNode("//h1");
                }
                var titulo = titleNode?.InnerText?.Trim() ?? "Unknown Title";
                titulo = System.Net.WebUtility.HtmlDecode(titulo);

                // Extract author - look for author/artist info
                string autor = "Unknown";
                var authorNode = htmlDoc.DocumentNode.SelectSingleNode("//span[contains(text(), 'Autor') or contains(text(), 'Author')]/following-sibling::*");
                if (authorNode == null)
                {
                    // Try alternative: look for a tag with author info
                    authorNode = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'manga-info')]//a[contains(@href, 'autor') or contains(@href, 'author')]");
                }
                if (authorNode == null)
                {
                    // Try: look for artist/author in manga tags or info sections
                    var infoNodes = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'manga-tag') or contains(@class, 'info')]");
                    if (infoNodes != null)
                    {
                        foreach (var node in infoNodes)
                        {
                            var text = node.InnerText;
                            if (text.Contains("Autor") || text.Contains("Artist") || text.Contains("Author"))
                            {
                                var match = Regex.Match(text, @"(?:Autor|Author|Artist)[:\s]+(.+?)(?:\n|$)", RegexOptions.IgnoreCase);
                                if (match.Success)
                                {
                                    autor = match.Groups[1].Value.Trim();
                                    break;
                                }
                            }
                        }
                    }
                }
                else
                {
                    autor = System.Net.WebUtility.HtmlDecode(authorNode.InnerText.Trim());
                }

                // If still unknown, try broader search
                if (autor == "Unknown")
                {
                    var allText = htmlDoc.DocumentNode.InnerText;
                    var authorMatch = Regex.Match(allText, @"(?:Autor|Author|Artist|Artista)[:\s]+([^\n\r]+)", RegexOptions.IgnoreCase);
                    if (authorMatch.Success)
                    {
                        autor = authorMatch.Groups[1].Value.Trim();
                        if (autor.Length > 100)
                            autor = autor.Substring(0, 100);
                    }
                }

                // Extract description/synopsis
                string descricao = "Imported from mangalivre.blog";
                var descNode = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'synopsis') or contains(@class, 'description') or contains(@class, 'sinopse')]");
                if (descNode == null)
                {
                    descNode = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'manga-summary')]//p");
                }
                if (descNode == null)
                {
                    // Try generic paragraph after synopsis heading
                    var synopsisHeading = htmlDoc.DocumentNode.SelectSingleNode("//*[contains(text(), 'Sinopse') or contains(text(), 'Synopsis')]");
                    if (synopsisHeading != null)
                    {
                        descNode = synopsisHeading.SelectSingleNode("following-sibling::p | following-sibling::div");
                    }
                }
                if (descNode != null)
                {
                    descricao = System.Net.WebUtility.HtmlDecode(descNode.InnerText.Trim());
                    if (descricao.Length > 1000)
                        descricao = descricao.Substring(0, 1000) + "...";
                }

                var manga = new Manga
                {
                    Titulo = CleanTitle(titulo),
                    Autor = autor,
                    Descricao = descricao,
                    DataCriacao = DateTime.Now,
                    ImagemCapa = "/images/placeholder.jpg"
                };

                return manga;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting metadata: {ex.Message}");
                return null;
            }
        }

        private string? ExtractCoverUrl(HtmlDocument htmlDoc)
        {
            // Try .manga-cover img
            var coverNode = htmlDoc.DocumentNode.SelectSingleNode("//img[contains(@class, 'manga-cover')]");
            if (coverNode == null)
            {
                coverNode = htmlDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'manga-cover')]//img");
            }
            if (coverNode == null)
            {
                // Try og:image meta tag
                var ogImage = htmlDoc.DocumentNode.SelectSingleNode("//meta[@property='og:image']");
                if (ogImage != null)
                {
                    return ogImage.GetAttributeValue("content", null!);
                }
            }

            return coverNode?.GetAttributeValue("src", null!);
        }

        private List<ChapterInfo> ExtractChapterList(HtmlDocument htmlDoc)
        {
            var chapters = new List<ChapterInfo>();

            // Look for chapter links - chapters follow pattern: /capitulo/{slug}-capitulo-{number}/
            var chapterLinks = htmlDoc.DocumentNode.SelectNodes("//a[contains(@href, '/capitulo/')]");
            if (chapterLinks == null)
            {
                // Try alternative selectors
                chapterLinks = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'chapter')]//a");
            }

            if (chapterLinks == null)
                return chapters;

            var seenUrls = new HashSet<string>();

            foreach (var link in chapterLinks)
            {
                var href = link.GetAttributeValue("href", "");
                if (string.IsNullOrEmpty(href) || !href.Contains("/capitulo/"))
                    continue;

                // Make absolute URL if needed
                if (!href.StartsWith("http"))
                {
                    href = "https://mangalivre.blog" + (href.StartsWith("/") ? "" : "/") + href;
                }

                if (seenUrls.Contains(href))
                    continue;
                seenUrls.Add(href);

                // Extract chapter number from URL or text
                var chapterNumber = ExtractChapterNumber(href, link.InnerText);

                // Extract chapter title from text (excluding the chapter number part)
                string? chapterTitle = null;
                var linkText = System.Net.WebUtility.HtmlDecode(link.InnerText.Trim());
                var titleMatch = Regex.Match(linkText, @"Cap[ií]tulo\s+\d+\s*[-:]\s*(.+)", RegexOptions.IgnoreCase);
                if (titleMatch.Success)
                {
                    chapterTitle = titleMatch.Groups[1].Value.Trim();
                }

                chapters.Add(new ChapterInfo
                {
                    Url = href,
                    Number = chapterNumber,
                    Title = chapterTitle
                });
            }

            return chapters;
        }

        private string ExtractChapterNumber(string url, string linkText)
        {
            // Try to extract from URL: -capitulo-{number}/
            var urlMatch = Regex.Match(url, @"capitulo-(\d+(?:\.\d+)?)\/?$", RegexOptions.IgnoreCase);
            if (urlMatch.Success)
            {
                return urlMatch.Groups[1].Value;
            }

            // Try from link text: "Capitulo 07" or "Cap. 7"
            var textMatch = Regex.Match(linkText, @"(?:Cap[ií]tulo|Cap\.?)\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (textMatch.Success)
            {
                return textMatch.Groups[1].Value;
            }

            return "0";
        }

        #endregion

        #region Private Methods - Download

        private async Task<List<string>> DownloadChapterPages(string chapterUrl, string mangaTitle, string chapterNumber)
        {
            var downloadedPaths = new List<string>();

            try
            {
                var html = await _httpClient.GetStringAsync(chapterUrl);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                // Extract page images using .chapter-image selector
                var imageNodes = htmlDoc.DocumentNode.SelectNodes("//img[contains(@class, 'chapter-image')]");
                if (imageNodes == null)
                {
                    // Try alternative selectors
                    imageNodes = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'chapter-image-container')]//img");
                }
                if (imageNodes == null)
                {
                    // Try broader: any img inside the reader area
                    imageNodes = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'chapter-images')]//img");
                }

                if (imageNodes == null || imageNodes.Count == 0)
                {
                    Console.WriteLine($"  No images found in chapter page");
                    return downloadedPaths;
                }

                Console.WriteLine($"  Found {imageNodes.Count} pages to download");

                int pageNumber = 1;
                var sanitizedTitle = SanitizeFolderName(mangaTitle);
                var sanitizedChapter = SanitizeFolderName($"chapter-{chapterNumber}");
                var chapterFolder = Path.Combine(_environment.WebRootPath, "uploads", "paginas", sanitizedTitle, sanitizedChapter);

                if (!Directory.Exists(chapterFolder))
                {
                    Directory.CreateDirectory(chapterFolder);
                }

                foreach (var imgNode in imageNodes)
                {
                    try
                    {
                        // Try src first, then data-src for lazy loading
                        var imageUrl = imgNode.GetAttributeValue("src", "");
                        if (string.IsNullOrEmpty(imageUrl) || imageUrl.StartsWith("data:"))
                        {
                            imageUrl = imgNode.GetAttributeValue("data-src", "");
                        }
                        if (string.IsNullOrEmpty(imageUrl) || imageUrl.StartsWith("data:"))
                        {
                            imageUrl = imgNode.GetAttributeValue("data-lazy-src", "");
                        }

                        if (string.IsNullOrEmpty(imageUrl) || imageUrl.StartsWith("data:"))
                            continue;

                        // Make absolute URL if needed
                        if (!imageUrl.StartsWith("http"))
                        {
                            imageUrl = "https://mangalivre.blog" + (imageUrl.StartsWith("/") ? "" : "/") + imageUrl;
                        }

                        var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);

                        var extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
                        if (string.IsNullOrEmpty(extension))
                        {
                            extension = ".jpg";
                        }

                        var fileName = $"{Guid.NewGuid()}_page{pageNumber:D3}{extension}";
                        var filePath = Path.Combine(chapterFolder, fileName);

                        await File.WriteAllBytesAsync(filePath, imageBytes);

                        downloadedPaths.Add($"/uploads/paginas/{sanitizedTitle}/{sanitizedChapter}/{fileName}");

                        pageNumber++;
                        await Task.Delay(300);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Error downloading page {pageNumber}: {ex.Message}");
                    }
                }

                return downloadedPaths;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading chapter pages: {ex.Message}");
                return downloadedPaths;
            }
        }

        private async Task<string?> DownloadCoverImage(string coverUrl, string mangaTitle)
        {
            try
            {
                if (!coverUrl.StartsWith("http"))
                {
                    coverUrl = "https://mangalivre.blog" + (coverUrl.StartsWith("/") ? "" : "/") + coverUrl;
                }

                var imageBytes = await _httpClient.GetByteArrayAsync(coverUrl);

                var extension = Path.GetExtension(new Uri(coverUrl).AbsolutePath);
                if (string.IsNullOrEmpty(extension))
                {
                    extension = ".jpg";
                }

                var uniqueFileName = $"{Guid.NewGuid()}{extension}";
                var capasFolder = Path.Combine(_environment.WebRootPath, "uploads", "capas");

                if (!Directory.Exists(capasFolder))
                {
                    Directory.CreateDirectory(capasFolder);
                }

                var filePath = Path.Combine(capasFolder, uniqueFileName);
                await File.WriteAllBytesAsync(filePath, imageBytes);

                return $"/uploads/capas/{uniqueFileName}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading cover image: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Private Methods - Chapter Filtering

        private List<ChapterInfo> FilterChaptersByRange(List<ChapterInfo> allChapters, string range)
        {
            if (range.Trim().ToLower() == "all")
                return allChapters;

            var selectedNumbers = ExpandChapterRange(range);
            if (selectedNumbers == null || selectedNumbers.Count == 0)
                return allChapters;

            var filtered = new List<ChapterInfo>();
            foreach (var chapter in allChapters)
            {
                var chapterNum = ParseChapterNumber(chapter.Number);
                if (selectedNumbers.Any(num => Math.Abs(num - chapterNum) < 0.01m))
                {
                    filtered.Add(chapter);
                }
            }

            return filtered;
        }

        private List<decimal>? ExpandChapterRange(string range)
        {
            if (string.IsNullOrWhiteSpace(range) || range.Trim().ToLower() == "all")
                return null;

            var chapters = new HashSet<decimal>();
            var parts = range.Split(',');

            foreach (var part in parts)
            {
                var trimmed = part.Trim();

                if (trimmed.Contains('-'))
                {
                    var rangeParts = trimmed.Split('-');
                    if (rangeParts.Length == 2 &&
                        decimal.TryParse(rangeParts[0].Trim(), out decimal start) &&
                        decimal.TryParse(rangeParts[1].Trim(), out decimal end))
                    {
                        for (decimal i = Math.Ceiling(start); i <= Math.Floor(end); i++)
                        {
                            chapters.Add(i);
                        }
                    }
                }
                else if (decimal.TryParse(trimmed, out decimal chapter))
                {
                    chapters.Add(chapter);
                }
            }

            return chapters.OrderBy(c => c).ToList();
        }

        private decimal ParseChapterNumber(string? chapterStr)
        {
            if (string.IsNullOrWhiteSpace(chapterStr))
                return 0;

            if (decimal.TryParse(chapterStr, out decimal result))
                return result;

            return 0;
        }

        #endregion

        #region Private Methods - Utilities

        private string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return "Unknown Manga";

            if (title.Length > 200)
                title = title.Substring(0, 200);

            return title.Trim();
        }

        private string SanitizeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "manga";

            var sanitized = name.Replace(" ", "-");
            var invalidChars = Path.GetInvalidFileNameChars();
            sanitized = string.Join("", sanitized.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            sanitized = Regex.Replace(sanitized, @"[^\w\-\.]", "");

            if (sanitized.Length > 50)
                sanitized = sanitized.Substring(0, 50);

            sanitized = sanitized.TrimEnd('-').TrimEnd('.');

            return string.IsNullOrEmpty(sanitized) ? "manga" : sanitized;
        }

        #endregion
    }
}
