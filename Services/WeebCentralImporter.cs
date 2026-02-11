using AnyComic.Models;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace AnyComic.Services
{
    public class WeebCentralImporter
    {
        private readonly HttpClient _httpClient;
        private readonly IWebHostEnvironment _environment;
        private const string BASE_URL = "https://weebcentral.com";

        public WeebCentralImporter(IWebHostEnvironment environment)
        {
            _environment = environment;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        }

        #region DTOs

        public class WeebCentralChapter
        {
            public string Id { get; set; } = string.Empty;
            public decimal ChapterNumber { get; set; }
            public string ChapterTitle { get; set; } = string.Empty;
        }

        public class ChapterImportData
        {
            public string ChapterNumber { get; set; } = string.Empty;
            public string? ChapterTitle { get; set; }
            public List<string> PagePaths { get; set; } = new();
        }

        #endregion

        #region Public Methods

        public async Task<(Manga manga, List<ChapterImportData> chapters)?> ImportFromUrl(
            string url,
            string chapterRange = "all")
        {
            try
            {
                var seriesId = ExtractSeriesId(url);
                if (seriesId == null)
                {
                    Console.WriteLine("Invalid WeebCentral URL");
                    return null;
                }

                // Get manga page
                var mangaHtml = await _httpClient.GetStringAsync(url);
                var mangaDoc = new HtmlDocument();
                mangaDoc.LoadHtml(mangaHtml);

                // Extract manga info
                var title = ExtractTitle(mangaDoc) ?? "Unknown Manga";
                var author = ExtractAuthor(mangaDoc) ?? "Unknown";
                var description = ExtractDescription(mangaDoc) ?? "Imported from WeebCentral";
                var coverUrl = ExtractCoverUrl(mangaDoc);

                Console.WriteLine($"Found manga: {title} by {author}");

                // Get full chapter list
                var allChapters = await GetFullChapterList(seriesId);
                if (allChapters.Count == 0)
                {
                    Console.WriteLine("No chapters found");
                    return null;
                }

                Console.WriteLine($"Found {allChapters.Count} chapters");

                // Filter chapters
                var selectedChapters = FilterChaptersByRange(allChapters, chapterRange);
                Console.WriteLine($"Selected {selectedChapters.Count} chapters to import");

                // Download cover
                string? coverPath = null;
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    coverPath = await DownloadCoverImage(coverUrl);
                }

                var manga = new Manga
                {
                    Titulo = CleanTitle(title),
                    Autor = author,
                    Descricao = CleanDescription(description),
                    DataCriacao = DateTime.Now,
                    ImagemCapa = coverPath ?? "/images/placeholder.jpg"
                };

                // Download chapters
                var importedChapters = new List<ChapterImportData>();
                int index = 1;

                foreach (var chapter in selectedChapters.OrderBy(c => c.ChapterNumber))
                {
                    try
                    {
                        Console.WriteLine($"Downloading chapter {index}/{selectedChapters.Count}: {chapter.ChapterNumber}");

                        var pagePaths = await DownloadChapterPages(chapter, manga.Titulo);

                        if (pagePaths.Count > 0)
                        {
                            importedChapters.Add(new ChapterImportData
                            {
                                ChapterNumber = chapter.ChapterNumber.ToString(),
                                ChapterTitle = string.IsNullOrEmpty(chapter.ChapterTitle) ? null : chapter.ChapterTitle,
                                PagePaths = pagePaths
                            });
                            Console.WriteLine($"  Downloaded {pagePaths.Count} pages");
                        }

                        index++;
                        await Task.Delay(500);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error downloading chapter {chapter.ChapterNumber}: {ex.Message}");
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
                Console.WriteLine($"Error importing from WeebCentral: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Private Methods

        private string? ExtractSeriesId(string url)
        {
            // URL format: https://weebcentral.com/series/01J76XYEMWA55C7XTZHP1HNARM/Dandadan
            var match = Regex.Match(url, @"weebcentral\.com/series/([A-Z0-9]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private string? ExtractTitle(HtmlDocument doc)
        {
            // Try og:title meta tag first
            var ogTitle = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']");
            if (ogTitle != null)
            {
                var content = ogTitle.GetAttributeValue("content", "");
                // Remove " | Weeb Central" suffix
                var pipeIndex = content.IndexOf(" | ");
                return pipeIndex > 0 ? content.Substring(0, pipeIndex).Trim() : content.Trim();
            }

            // Fallback to title tag
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
            {
                var text = titleNode.InnerText;
                var pipeIndex = text.IndexOf(" | ");
                return pipeIndex > 0 ? text.Substring(0, pipeIndex).Trim() : text.Trim();
            }

            return null;
        }

        private string? ExtractAuthor(HtmlDocument doc)
        {
            // Look for author in the page details section
            var detailNodes = doc.DocumentNode.SelectNodes("//li[contains(@class, 'flex')]//span");
            if (detailNodes != null)
            {
                foreach (var node in detailNodes)
                {
                    var text = node.InnerText.Trim();
                    if (text == "Author(s)" || text == "Author")
                    {
                        var parent = node.ParentNode;
                        var linkNode = parent?.SelectSingleNode(".//a");
                        if (linkNode != null)
                            return HtmlEntity.DeEntitize(linkNode.InnerText.Trim());
                    }
                }
            }
            return null;
        }

        private string? ExtractDescription(HtmlDocument doc)
        {
            var ogDesc = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']");
            if (ogDesc != null)
            {
                var content = ogDesc.GetAttributeValue("content", "");
                if (!string.IsNullOrEmpty(content))
                    return HtmlEntity.DeEntitize(content);
            }
            return null;
        }

        private string? ExtractCoverUrl(HtmlDocument doc)
        {
            var ogImage = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']");
            if (ogImage != null)
            {
                var content = ogImage.GetAttributeValue("content", "");
                return string.IsNullOrEmpty(content) ? null : content;
            }
            return null;
        }

        private async Task<List<WeebCentralChapter>> GetFullChapterList(string seriesId)
        {
            var chapters = new List<WeebCentralChapter>();

            try
            {
                var url = $"{BASE_URL}/series/{seriesId}/full-chapter-list";
                var html = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Find all chapter links
                var chapterLinks = doc.DocumentNode.SelectNodes("//a[contains(@href, '/chapters/')]");
                if (chapterLinks == null)
                    return chapters;

                foreach (var link in chapterLinks)
                {
                    var href = link.GetAttributeValue("href", "");
                    if (string.IsNullOrEmpty(href) || !href.Contains("/chapters/"))
                        continue;

                    // Extract chapter number from the text
                    var chapterText = link.InnerText.Trim();
                    var match = Regex.Match(chapterText, @"Chapter\s+([\d.]+)", RegexOptions.IgnoreCase);
                    if (!match.Success)
                        continue;

                    if (!decimal.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out decimal chapterNum))
                        continue;

                    // Extract chapter ID from URL
                    var idMatch = Regex.Match(href, @"/chapters/([A-Z0-9]+)", RegexOptions.IgnoreCase);
                    if (!idMatch.Success)
                        continue;

                    // Check for chapter title (text after the number)
                    var titleMatch = Regex.Match(chapterText, @"Chapter\s+[\d.]+\s*[:\-]\s*(.+)", RegexOptions.IgnoreCase);

                    chapters.Add(new WeebCentralChapter
                    {
                        Id = idMatch.Groups[1].Value,
                        ChapterNumber = chapterNum,
                        ChapterTitle = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : ""
                    });
                }

                // Remove duplicates (keep first occurrence)
                chapters = chapters
                    .GroupBy(c => c.ChapterNumber)
                    .Select(g => g.First())
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching chapter list: {ex.Message}");
            }

            return chapters;
        }

        private async Task<List<string>> DownloadChapterPages(WeebCentralChapter chapter, string mangaTitle)
        {
            var downloadedPaths = new List<string>();

            try
            {
                // Get chapter images page
                var imagesUrl = $"{BASE_URL}/chapters/{chapter.Id}/images?is_prev=False&current_page=1&reading_style=long_strip";
                var html = await _httpClient.GetStringAsync(imagesUrl);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Find all page images
                var imgNodes = doc.DocumentNode.SelectNodes("//img[@alt]");
                if (imgNodes == null)
                    return downloadedPaths;

                var imageUrls = new List<string>();
                foreach (var img in imgNodes)
                {
                    var alt = img.GetAttributeValue("alt", "");
                    if (!alt.StartsWith("Page ", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var src = img.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(src) && (src.Contains(".png") || src.Contains(".jpg") || src.Contains(".webp")))
                    {
                        imageUrls.Add(src);
                    }
                }

                Console.WriteLine($"  Found {imageUrls.Count} pages to download");

                var sanitizedTitle = SanitizeFolderName(mangaTitle);
                var sanitizedChapter = SanitizeFolderName($"chapter-{chapter.ChapterNumber}");
                var chapterFolder = Path.Combine(_environment.WebRootPath, "uploads", "paginas", sanitizedTitle, sanitizedChapter);

                if (!Directory.Exists(chapterFolder))
                    Directory.CreateDirectory(chapterFolder);

                // Download each image
                int pageNumber = 1;
                foreach (var imageUrl in imageUrls)
                {
                    try
                    {
                        var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);

                        var extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
                        if (string.IsNullOrEmpty(extension))
                            extension = ".png";

                        var fileName = $"{Guid.NewGuid()}_page{pageNumber:D3}{extension}";

                        var filePath = Path.Combine(chapterFolder, fileName);
                        await File.WriteAllBytesAsync(filePath, imageBytes);

                        downloadedPaths.Add($"/uploads/paginas/{sanitizedTitle}/{sanitizedChapter}/{fileName}");
                        pageNumber++;

                        await Task.Delay(200);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Error downloading page {pageNumber}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading chapter pages: {ex.Message}");
            }

            return downloadedPaths;
        }

        private async Task<string?> DownloadCoverImage(string imageUrl)
        {
            try
            {
                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
                var extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
                if (string.IsNullOrEmpty(extension))
                    extension = ".jpg";

                var uniqueFileName = $"{Guid.NewGuid()}{extension}";
                var capasFolder = Path.Combine(_environment.WebRootPath, "uploads", "capas");

                if (!Directory.Exists(capasFolder))
                    Directory.CreateDirectory(capasFolder);

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

        private List<WeebCentralChapter> FilterChaptersByRange(List<WeebCentralChapter> allChapters, string range)
        {
            if (range.Trim().ToLower() == "all")
                return allChapters;

            var selectedNumbers = ExpandChapterRange(range);
            if (selectedNumbers == null || selectedNumbers.Count == 0)
                return allChapters;

            return allChapters
                .Where(c => selectedNumbers.Any(num => Math.Abs(num - c.ChapterNumber) < 0.01m))
                .ToList();
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
                            chapters.Add(i);
                    }
                }
                else if (decimal.TryParse(trimmed, out decimal chapter))
                {
                    chapters.Add(chapter);
                }
            }

            return chapters.OrderBy(c => c).ToList();
        }

        private string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "Unknown Manga";
            if (title.Length > 200) title = title.Substring(0, 200);
            return title.Trim();
        }

        private string CleanDescription(string? description)
        {
            if (string.IsNullOrEmpty(description)) return "Imported from WeebCentral";
            if (description.Length > 1000) description = description.Substring(0, 1000) + "...";
            return description.Trim();
        }

        private string SanitizeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "manga";
            var sanitized = name.Replace(" ", "-");
            var invalidChars = Path.GetInvalidFileNameChars();
            sanitized = string.Join("", sanitized.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            sanitized = Regex.Replace(sanitized, @"[^\w\-\.]", "");
            if (sanitized.Length > 50) sanitized = sanitized.Substring(0, 50);
            sanitized = sanitized.TrimEnd('-').TrimEnd('.');
            return string.IsNullOrEmpty(sanitized) ? "manga" : sanitized;
        }

        #endregion
    }
}
