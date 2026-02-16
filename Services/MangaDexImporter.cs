using AnyComic.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnyComic.Services
{
    /// <summary>
    /// Service responsible for importing manga data from MangaDex.org
    /// Uses MangaDex API v5: https://api.mangadex.org/docs/
    /// </summary>
    public class MangaDexImporter
    {
        private readonly HttpClient _httpClient;
        private readonly IWebHostEnvironment _environment;
        private const string BASE_URL = "https://api.mangadex.org";
        private const string UPLOADS_BASE_URL = "https://uploads.mangadex.org";

        public MangaDexImporter(IWebHostEnvironment environment)
        {
            _environment = environment;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AnyComic/1.0");
        }

        #region DTOs

        /// <summary>
        /// Manga metadata from MangaDex
        /// </summary>
        public class MangaDexMangaInfo
        {
            public string Id { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string? Author { get; set; }
            public string? Artist { get; set; }
            public string? Description { get; set; }
            public string? CoverId { get; set; }
            public string? CoverFileName { get; set; }
            public DateTime? CreatedAt { get; set; }
        }

        /// <summary>
        /// Chapter information from MangaDex
        /// </summary>
        public class MangaDexChapter
        {
            public string Id { get; set; } = string.Empty;
            public string? Chapter { get; set; }
            public string? Title { get; set; }
            public string? Volume { get; set; }
            public string Language { get; set; } = string.Empty;
            public int Pages { get; set; }
            public DateTime? PublishAt { get; set; }
        }

        /// <summary>
        /// Data for a downloaded chapter
        /// </summary>
        public class ChapterImportData
        {
            public string ChapterNumber { get; set; } = string.Empty;
            public string? ChapterTitle { get; set; }
            public List<string> PagePaths { get; set; } = new();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Imports manga from MangaDex URL with chapter selection
        /// </summary>
        /// <param name="url">MangaDex manga URL</param>
        /// <param name="language">Language code (e.g., "en", "pt-br")</param>
        /// <param name="chapterRange">Chapter range (e.g., "all", "1-5", "1,3,5")</param>
        /// <param name="quality">Image quality ("data" or "data-saver")</param>
        /// <returns>Tuple with manga metadata and list of downloaded chapters</returns>
        public async Task<(Manga manga, List<ChapterImportData> chapters)?> ImportFromUrl(
            string url,
            string language = "en",
            string chapterRange = "all",
            string quality = "data")
        {
            try
            {
                // Extract manga ID from URL
                var mangaId = ExtractMangaIdFromUrl(url);
                if (mangaId == null)
                {
                    Console.WriteLine("Invalid MangaDex URL");
                    return null;
                }

                // Get manga metadata
                var mangaInfo = await GetMangaInfo(mangaId);
                if (mangaInfo == null)
                {
                    Console.WriteLine("Failed to fetch manga information");
                    return null;
                }

                // Get all chapters in specified language
                var allChapters = await GetChapters(mangaId, language);
                if (allChapters.Count == 0)
                {
                    Console.WriteLine($"No chapters found in language: {language}");
                    return null;
                }

                Console.WriteLine($"Found {allChapters.Count} chapters in {language}");

                // Filter chapters based on range
                var selectedChapters = FilterChaptersByRange(allChapters, chapterRange);
                if (selectedChapters.Count == 0)
                {
                    Console.WriteLine($"No chapters match the range: {chapterRange}");
                    return null;
                }

                Console.WriteLine($"Selected {selectedChapters.Count} chapters to import");

                // Download cover image
                string? coverImagePath = null;
                if (!string.IsNullOrEmpty(mangaInfo.CoverFileName))
                {
                    coverImagePath = await DownloadCoverImage(mangaId, mangaInfo.CoverFileName, mangaInfo.Title);
                }

                // Create Manga object
                var manga = new Manga
                {
                    Titulo = CleanTitle(mangaInfo.Title),
                    Autor = mangaInfo.Author ?? mangaInfo.Artist ?? "Unknown",
                    Descricao = CleanDescription(mangaInfo.Description),
                    DataCriacao = mangaInfo.CreatedAt ?? DateTime.Now,
                    ImagemCapa = coverImagePath ?? "/images/placeholder.jpg"
                };

                // Download all selected chapters
                var importedChapters = new List<ChapterImportData>();
                int chapterIndex = 1;

                foreach (var chapter in selectedChapters.OrderBy(c => ParseChapterNumber(c.Chapter)))
                {
                    try
                    {
                        Console.WriteLine($"Downloading chapter {chapterIndex}/{selectedChapters.Count}: {chapter.Chapter}");

                        var pagePaths = await DownloadChapterPages(chapter.Id, manga.Titulo, chapter.Chapter ?? "0", quality);

                        if (pagePaths.Count > 0)
                        {
                            importedChapters.Add(new ChapterImportData
                            {
                                ChapterNumber = chapter.Chapter ?? "0",
                                ChapterTitle = chapter.Title,
                                PagePaths = pagePaths
                            });

                            Console.WriteLine($"  ✓ Downloaded {pagePaths.Count} pages");
                        }
                        else
                        {
                            Console.WriteLine($"  ✗ Failed to download chapter {chapter.Chapter}");
                        }

                        chapterIndex++;

                        // Small delay between chapters to avoid rate limiting
                        await Task.Delay(1000);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error downloading chapter {chapter.Chapter}: {ex.Message}");
                        // Continue with next chapter
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
                Console.WriteLine($"Error importing from MangaDex: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Private Methods - URL & Validation

        /// <summary>
        /// Extracts manga UUID from MangaDex URL
        /// </summary>
        private string? ExtractMangaIdFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            // MangaDex URL format: https://mangadex.org/title/{uuid}/manga-name
            var match = Regex.Match(url, @"mangadex\.org/title/([a-f0-9-]{36})", RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Validates if the URL is a valid MangaDex URL
        /// </summary>
        private bool IsValidMangaDexUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return url.Contains("mangadex.org/title/");
        }

        #endregion

        #region Private Methods - API Calls

        /// <summary>
        /// Gets manga metadata from MangaDex API
        /// </summary>
        private async Task<MangaDexMangaInfo?> GetMangaInfo(string mangaId)
        {
            try
            {
                var url = $"{BASE_URL}/manga/{mangaId}?includes[]=cover_art&includes[]=author&includes[]=artist";
                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);

                var data = json.RootElement.GetProperty("data");
                var attributes = data.GetProperty("attributes");

                // Get title (prefer English, fallback to first available)
                var titleObj = attributes.GetProperty("title");
                string title = GetLocalizedString(titleObj, "en");

                // Get description
                string? description = null;
                if (attributes.TryGetProperty("description", out var descObj))
                {
                    description = GetLocalizedString(descObj, "en");
                }

                // Get author and artist from relationships
                string? author = null;
                string? artist = null;
                string? coverId = null;
                string? coverFileName = null;

                if (data.TryGetProperty("relationships", out var relationships))
                {
                    foreach (var rel in relationships.EnumerateArray())
                    {
                        var type = rel.GetProperty("type").GetString();

                        if (type == "author" && rel.TryGetProperty("attributes", out var authorAttrs))
                        {
                            author = authorAttrs.GetProperty("name").GetString();
                        }
                        else if (type == "artist" && rel.TryGetProperty("attributes", out var artistAttrs))
                        {
                            artist = artistAttrs.GetProperty("name").GetString();
                        }
                        else if (type == "cover_art")
                        {
                            coverId = rel.GetProperty("id").GetString();
                            if (rel.TryGetProperty("attributes", out var coverAttrs))
                            {
                                coverFileName = coverAttrs.GetProperty("fileName").GetString();
                            }
                        }
                    }
                }

                return new MangaDexMangaInfo
                {
                    Id = mangaId,
                    Title = title,
                    Author = author,
                    Artist = artist,
                    Description = description,
                    CoverId = coverId,
                    CoverFileName = coverFileName
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching manga info: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets all chapters for a manga in specified language
        /// </summary>
        private async Task<List<MangaDexChapter>> GetChapters(string mangaId, string language)
        {
            var chapters = new List<MangaDexChapter>();
            int offset = 0;
            const int limit = 500; // Max allowed by API

            try
            {
                while (true)
                {
                    var url = $"{BASE_URL}/manga/{mangaId}/feed?translatedLanguage[]={language}&limit={limit}&offset={offset}&order[chapter]=asc&contentRating[]=safe&contentRating[]=suggestive&contentRating[]=erotica&contentRating[]=pornographic";
                    var response = await _httpClient.GetStringAsync(url);
                    var json = JsonDocument.Parse(response);

                    var data = json.RootElement.GetProperty("data");
                    var total = json.RootElement.GetProperty("total").GetInt32();

                    foreach (var chapterData in data.EnumerateArray())
                    {
                        var attributes = chapterData.GetProperty("attributes");

                        // Skip chapters with external URLs (e.g., MangaPlus) - they have no downloadable pages
                        var externalUrl = attributes.TryGetProperty("externalUrl", out var extUrl) ? extUrl.GetString() : null;
                        var pageCount = attributes.GetProperty("pages").GetInt32();
                        if (!string.IsNullOrEmpty(externalUrl) || pageCount == 0)
                        {
                            continue;
                        }

                        var chapter = new MangaDexChapter
                        {
                            Id = chapterData.GetProperty("id").GetString() ?? "",
                            Chapter = attributes.TryGetProperty("chapter", out var ch) ? ch.GetString() : null,
                            Title = attributes.TryGetProperty("title", out var t) ? t.GetString() : null,
                            Volume = attributes.TryGetProperty("volume", out var v) ? v.GetString() : null,
                            Language = attributes.GetProperty("translatedLanguage").GetString() ?? "",
                            Pages = pageCount
                        };

                        chapters.Add(chapter);
                    }

                    // Check if we've retrieved all chapters
                    if (offset + limit >= total)
                        break;

                    offset += limit;

                    // Small delay to avoid rate limiting
                    await Task.Delay(500);
                }

                // Deduplicate: keep the version with most pages for each chapter number
                var deduplicated = chapters
                    .GroupBy(c => c.Chapter ?? "0")
                    .Select(g => g.OrderByDescending(c => c.Pages).First())
                    .ToList();

                Console.WriteLine($"Found {chapters.Count} chapters, {deduplicated.Count} after deduplication");
                return deduplicated;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching chapters: {ex.Message}");
                return chapters;
            }
        }

        /// <summary>
        /// Downloads all pages for a chapter
        /// </summary>
        private async Task<List<string>> DownloadChapterPages(string chapterId, string mangaTitle, string chapterNumber, string quality)
        {
            var downloadedPaths = new List<string>();

            try
            {
                // Get at-home server URL and image list
                var url = $"{BASE_URL}/at-home/server/{chapterId}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);

                var baseUrl = json.RootElement.GetProperty("baseUrl").GetString();
                var chapter = json.RootElement.GetProperty("chapter");
                var hash = chapter.GetProperty("hash").GetString();

                // Get image array based on quality setting
                var imageArray = quality == "data-saver"
                    ? chapter.GetProperty("dataSaver")
                    : chapter.GetProperty("data");

                var images = new List<string>();
                foreach (var img in imageArray.EnumerateArray())
                {
                    images.Add(img.GetString() ?? "");
                }

                Console.WriteLine($"  Found {images.Count} pages to download");

                // Download each image
                int pageNumber = 1;
                foreach (var imageName in images)
                {
                    try
                    {
                        var imageUrl = $"{baseUrl}/{quality}/{hash}/{imageName}";
                        var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);

                        // Get file extension
                        var extension = Path.GetExtension(imageName);
                        if (string.IsNullOrEmpty(extension))
                        {
                            extension = ".jpg";
                        }

                        // Create filename
                        var fileName = $"{Guid.NewGuid()}_page{pageNumber:D3}{extension}";

                        // Sanitize manga title for folder name
                        var sanitizedTitle = SanitizeFolderName(mangaTitle);
                        var sanitizedChapter = SanitizeFolderName($"chapter-{chapterNumber}");
                        var chapterFolder = Path.Combine(_environment.WebRootPath, "uploads", "paginas", sanitizedTitle, sanitizedChapter);

                        // Create directory if it doesn't exist
                        if (!Directory.Exists(chapterFolder))
                        {
                            Directory.CreateDirectory(chapterFolder);
                        }

                        var filePath = Path.Combine(chapterFolder, fileName);

                        // Save image
                        await File.WriteAllBytesAsync(filePath, imageBytes);

                        // Return relative path for database
                        downloadedPaths.Add($"/uploads/paginas/{sanitizedTitle}/{sanitizedChapter}/{fileName}");

                        pageNumber++;

                        // Small delay to avoid rate limiting
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

        /// <summary>
        /// Downloads cover image
        /// </summary>
        private async Task<string?> DownloadCoverImage(string mangaId, string fileName, string mangaTitle)
        {
            try
            {
                // MangaDex cover URL format: https://uploads.mangadex.org/covers/{mangaId}/{fileName}
                var imageUrl = $"{UPLOADS_BASE_URL}/covers/{mangaId}/{fileName}";
                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);

                // Get file extension
                var extension = Path.GetExtension(fileName);
                if (string.IsNullOrEmpty(extension))
                {
                    extension = ".jpg";
                }

                // Create unique filename
                var uniqueFileName = $"{Guid.NewGuid()}{extension}";

                // Save to capas folder
                var capasFolder = Path.Combine(_environment.WebRootPath, "uploads", "capas");
                if (!Directory.Exists(capasFolder))
                {
                    Directory.CreateDirectory(capasFolder);
                }

                var filePath = Path.Combine(capasFolder, uniqueFileName);
                await File.WriteAllBytesAsync(filePath, imageBytes);

                // Return relative path
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

        /// <summary>
        /// Filters chapters based on user-specified range
        /// </summary>
        private List<MangaDexChapter> FilterChaptersByRange(List<MangaDexChapter> allChapters, string range)
        {
            if (range.Trim().ToLower() == "all")
            {
                return allChapters;
            }

            var selectedNumbers = ExpandChapterRange(range);
            if (selectedNumbers == null || selectedNumbers.Count == 0)
            {
                return allChapters;
            }

            // Filter chapters by the selected numbers
            var filtered = new List<MangaDexChapter>();
            foreach (var chapter in allChapters)
            {
                var chapterNum = ParseChapterNumber(chapter.Chapter);
                if (selectedNumbers.Any(num => Math.Abs(num - chapterNum) < 0.01m)) // Handle floating point comparison
                {
                    filtered.Add(chapter);
                }
            }

            return filtered;
        }

        /// <summary>
        /// Expands chapter range string into list of chapter numbers
        /// Supports: "1-5", "1,3,5", "1-5,10,15-20"
        /// </summary>
        private List<decimal>? ExpandChapterRange(string range)
        {
            if (string.IsNullOrWhiteSpace(range) || range.Trim().ToLower() == "all")
                return null;

            var chapters = new HashSet<decimal>();
            var parts = range.Split(',');

            foreach (var part in parts)
            {
                var trimmed = part.Trim();

                // Range format: "1-5"
                if (trimmed.Contains('-'))
                {
                    var rangeParts = trimmed.Split('-');
                    if (rangeParts.Length == 2 &&
                        decimal.TryParse(rangeParts[0].Trim(), out decimal start) &&
                        decimal.TryParse(rangeParts[1].Trim(), out decimal end))
                    {
                        // For ranges, only include whole numbers
                        for (decimal i = Math.Ceiling(start); i <= Math.Floor(end); i++)
                        {
                            chapters.Add(i);
                        }
                    }
                }
                // Single chapter: "3" or "3.5"
                else if (decimal.TryParse(trimmed, out decimal chapter))
                {
                    chapters.Add(chapter);
                }
            }

            return chapters.OrderBy(c => c).ToList();
        }

        /// <summary>
        /// Parses chapter number string to decimal
        /// </summary>
        private decimal ParseChapterNumber(string? chapterStr)
        {
            if (string.IsNullOrWhiteSpace(chapterStr))
                return 0;

            if (decimal.TryParse(chapterStr, out decimal result))
                return result;

            return 0;
        }

        #endregion

        #region Private Methods - String Utilities

        /// <summary>
        /// Gets localized string from JSON object, with fallback
        /// </summary>
        private string GetLocalizedString(JsonElement obj, string preferredLang)
        {
            // Try preferred language first
            if (obj.TryGetProperty(preferredLang, out var preferred))
            {
                return preferred.GetString() ?? "Unknown";
            }

            // Try common alternatives
            var alternatives = new[] { "en", "en-us", "ja-ro", "ja" };
            foreach (var alt in alternatives)
            {
                if (obj.TryGetProperty(alt, out var altValue))
                {
                    return altValue.GetString() ?? "Unknown";
                }
            }

            // Fallback to first available
            foreach (var prop in obj.EnumerateObject())
            {
                var value = prop.Value.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return "Unknown";
        }

        /// <summary>
        /// Cleans manga title
        /// </summary>
        private string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return "Unknown Manga";

            // Limit length
            if (title.Length > 200)
                title = title.Substring(0, 200);

            return title.Trim();
        }

        /// <summary>
        /// Cleans description text
        /// </summary>
        private string CleanDescription(string? description)
        {
            if (string.IsNullOrEmpty(description))
                return "Imported from MangaDex.org";

            // Limit length
            if (description.Length > 1000)
                description = description.Substring(0, 1000) + "...";

            return description.Trim();
        }

        /// <summary>
        /// Sanitizes string to create safe folder name
        /// </summary>
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
