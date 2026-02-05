using HtmlAgilityPack;
using AnyComic.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AnyComic.Services
{
    /// <summary>
    /// Service responsible for importing manga data from e-hentai.org
    /// </summary>
    public class EHentaiImporter
    {
        private readonly HttpClient _httpClient;
        private readonly IWebHostEnvironment _environment;

        public EHentaiImporter(IWebHostEnvironment environment)
        {
            _environment = environment;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        /// <summary>
        /// Imports a manga from e-hentai.org URL
        /// </summary>
        /// <param name="url">The e-hentai gallery URL</param>
        /// <returns>A tuple containing the manga object and list of downloaded page paths</returns>
        public async Task<(Manga manga, List<string> pagePaths)?> ImportFromUrl(string url)
        {
            try
            {
                // Validate URL
                if (!IsValidEHentaiUrl(url))
                {
                    return null;
                }

                // Fetch the gallery page
                var html = await _httpClient.GetStringAsync(url);
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                // Extract metadata
                var manga = ExtractMangaMetadata(htmlDoc);
                if (manga == null)
                {
                    return null;
                }

                // Extract page image URLs (with pagination support)
                var pageUrls = await ExtractPageUrls(htmlDoc, url);
                if (pageUrls == null || pageUrls.Count == 0)
                {
                    return null;
                }

                // Download all pages
                var pagePaths = await DownloadPages(pageUrls, manga.Titulo);

                return (manga, pagePaths);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error importing from e-hentai: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Validates if the URL is a valid e-hentai gallery URL
        /// </summary>
        private bool IsValidEHentaiUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            return url.Contains("e-hentai.org/g/") || url.Contains("exhentai.org/g/");
        }

        /// <summary>
        /// Extracts manga metadata from the gallery page
        /// </summary>
        private Manga? ExtractMangaMetadata(HtmlDocument htmlDoc)
        {
            try
            {
                // Extract title
                var titleNode = htmlDoc.DocumentNode.SelectSingleNode("//h1[@id='gn']");
                if (titleNode == null)
                {
                    titleNode = htmlDoc.DocumentNode.SelectSingleNode("//h1");
                }
                var titulo = titleNode?.InnerText?.Trim() ?? "Unknown Title";

                // Extract author/artist
                var artistNode = htmlDoc.DocumentNode.SelectSingleNode("//a[contains(@href, 'artist:')]");
                var autor = artistNode?.InnerText?.Trim() ?? "Unknown";

                // Extract upload date
                var dateNode = htmlDoc.DocumentNode.SelectSingleNode("//td[@class='gdt2' and contains(., '-')]");
                DateTime dataCriacao = DateTime.Now;

                if (dateNode != null)
                {
                    var dateText = dateNode.InnerText.Trim();
                    // Try to parse date (format: 2023-01-12 02:14)
                    if (DateTime.TryParseExact(dateText, "yyyy-MM-dd HH:mm",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    {
                        dataCriacao = parsedDate;
                    }
                }

                // Create manga object
                var manga = new Manga
                {
                    Titulo = CleanTitle(titulo),
                    Autor = autor,
                    Descricao = $"Imported from e-hentai.org",
                    DataCriacao = dataCriacao,
                    ImagemCapa = null! // Will be set from first page
                };

                return manga;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting metadata: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Cleans the title by removing excessive information
        /// </summary>
        private string CleanTitle(string title)
        {
            // Remove content in square brackets at the beginning (event/circle info)
            title = Regex.Replace(title, @"^\([^)]+\)\s*", "");
            title = Regex.Replace(title, @"^\[[^\]]+\]\s*", "");

            // Remove language tags at the end
            title = Regex.Replace(title, @"\s*\[[^\]]*(?:English|Portuguese|Spanish|Chinese|Korean|Japanese)[^\]]*\]\s*$", "", RegexOptions.IgnoreCase);

            return title.Trim();
        }

        /// <summary>
        /// Extracts page URLs from the gallery (handles pagination)
        /// </summary>
        private async Task<List<string>> ExtractPageUrls(HtmlDocument htmlDoc, string baseUrl)
        {
            var pageUrls = new List<string>();

            try
            {
                // Remove any existing query parameters from base URL
                var cleanBaseUrl = baseUrl.Split('?')[0];

                // Start with the first page (already loaded)
                ExtractPageUrlsFromDocument(htmlDoc, pageUrls);

                // Check if there are more pages (pagination)
                var paginationNode = htmlDoc.DocumentNode.SelectSingleNode("//table[@class='ptt']");

                if (paginationNode != null)
                {
                    // Find all page links
                    var pageLinks = paginationNode.SelectNodes(".//a");

                    if (pageLinks != null)
                    {
                        var pageNumbers = new HashSet<int>();

                        foreach (var link in pageLinks)
                        {
                            var href = link.GetAttributeValue("href", "");

                            // Extract page number from URL (e.g., ?p=1, ?p=2)
                            var match = Regex.Match(href, @"\?p=(\d+)");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out int pageNum))
                            {
                                pageNumbers.Add(pageNum);
                            }
                        }

                        // Fetch all additional pages
                        foreach (var pageNum in pageNumbers.OrderBy(p => p))
                        {
                            try
                            {
                                var pageUrl = $"{cleanBaseUrl}?p={pageNum}";
                                Console.WriteLine($"Fetching gallery page: {pageUrl}");

                                var pageHtml = await _httpClient.GetStringAsync(pageUrl);
                                var pageDoc = new HtmlDocument();
                                pageDoc.LoadHtml(pageHtml);

                                ExtractPageUrlsFromDocument(pageDoc, pageUrls);

                                // Small delay to avoid rate limiting
                                await Task.Delay(500);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error fetching page {pageNum}: {ex.Message}");
                            }
                        }
                    }
                }

                Console.WriteLine($"Total pages found: {pageUrls.Count}");
                return pageUrls;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting page URLs: {ex.Message}");
                return pageUrls;
            }
        }

        /// <summary>
        /// Helper method to extract page URLs from a single gallery page document
        /// </summary>
        private void ExtractPageUrlsFromDocument(HtmlDocument htmlDoc, List<string> pageUrls)
        {
            // Find all thumbnail links that lead to individual pages
            var pageLinks = htmlDoc.DocumentNode.SelectNodes("//div[@class='gdtm']//a");

            if (pageLinks == null || pageLinks.Count == 0)
            {
                // Try alternative selector
                pageLinks = htmlDoc.DocumentNode.SelectNodes("//div[@id='gdt']//a");
            }

            if (pageLinks != null)
            {
                foreach (var link in pageLinks)
                {
                    var href = link.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(href) && !pageUrls.Contains(href))
                    {
                        pageUrls.Add(href);
                    }
                }
            }
        }

        /// <summary>
        /// Downloads all pages from the gallery
        /// </summary>
        private async Task<List<string>> DownloadPages(List<string> pageUrls, string mangaTitle)
        {
            var downloadedPaths = new List<string>();

            try
            {
                int pageNumber = 1;
                foreach (var pageUrl in pageUrls)
                {
                    try
                    {
                        // Fetch the individual page
                        var pageHtml = await _httpClient.GetStringAsync(pageUrl);
                        var pageDoc = new HtmlDocument();
                        pageDoc.LoadHtml(pageHtml);

                        // Find the actual image URL
                        var imageNode = pageDoc.DocumentNode.SelectSingleNode("//img[@id='img']");
                        if (imageNode == null)
                        {
                            // Try alternative selectors
                            imageNode = pageDoc.DocumentNode.SelectSingleNode("//div[@id='i3']//img");
                        }

                        if (imageNode != null)
                        {
                            var imageUrl = imageNode.GetAttributeValue("src", "");
                            if (!string.IsNullOrEmpty(imageUrl))
                            {
                                // Download the image
                                var imagePath = await DownloadImage(imageUrl, mangaTitle, pageNumber);
                                if (imagePath != null)
                                {
                                    downloadedPaths.Add(imagePath);
                                }
                            }
                        }

                        pageNumber++;

                        // Add a small delay to avoid being rate-limited
                        await Task.Delay(500);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error downloading page {pageNumber}: {ex.Message}");
                    }
                }

                return downloadedPaths;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading pages: {ex.Message}");
                return downloadedPaths;
            }
        }

        /// <summary>
        /// Downloads a single image and saves it
        /// </summary>
        private async Task<string?> DownloadImage(string imageUrl, string mangaTitle, int pageNumber)
        {
            try
            {
                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);

                // Get file extension
                var extension = Path.GetExtension(imageUrl).Split('?')[0]; // Remove query params
                if (string.IsNullOrEmpty(extension))
                {
                    extension = ".jpg"; // Default
                }

                // Create filename
                var fileName = $"{Guid.NewGuid()}_page{pageNumber:D3}{extension}";

                // Sanitize manga title for folder name
                var sanitizedTitle = SanitizeFolderName(mangaTitle);
                var mangaFolder = Path.Combine(_environment.WebRootPath, "uploads", "paginas", sanitizedTitle);

                // Create directory if it doesn't exist
                if (!Directory.Exists(mangaFolder))
                {
                    Directory.CreateDirectory(mangaFolder);
                }

                var filePath = Path.Combine(mangaFolder, fileName);

                // Save image
                await File.WriteAllBytesAsync(filePath, imageBytes);

                // Return relative path for database
                return $"/uploads/paginas/{sanitizedTitle}/{fileName}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading image {imageUrl}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sanitizes manga title to create a safe folder name
        /// </summary>
        private string SanitizeFolderName(string titulo)
        {
            if (string.IsNullOrEmpty(titulo))
                return "manga";

            var sanitized = titulo.Replace(" ", "-");
            var invalidChars = Path.GetInvalidFileNameChars();
            sanitized = string.Join("", sanitized.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            sanitized = Regex.Replace(sanitized, @"[^\w\-]", "");

            if (sanitized.Length > 50)
                sanitized = sanitized.Substring(0, 50);

            sanitized = sanitized.TrimEnd('-');

            return string.IsNullOrEmpty(sanitized) ? "manga" : sanitized;
        }
    }
}
