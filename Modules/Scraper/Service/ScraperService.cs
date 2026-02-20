using JoraScraper.Modules.Scraper.Dto;
using JoraScraper.Modules.Scraper.Interface;
using System.Text.RegularExpressions;
using OfficeOpenXml;
using PuppeteerSharp;
using System.Net.Http.Headers;

namespace JoraScraper.Modules.Scraper.Service
{
    public class ScraperService : IScraperService
    {
        private readonly ILogger<ScraperService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public ScraperService(ILogger<ScraperService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task ScrapeAndSaveJobsAsync()
        {
            var jobs = new List<JobInfo>();
            var processedUrls = new HashSet<string>();

            try
            {
                _logger.LogInformation("Starting browser setup...");

                var executablePath = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");
                LaunchOptions launchOptions;

                if (!string.IsNullOrEmpty(executablePath))
                {
                    _logger.LogInformation("Using system Chromium at: {path}", executablePath);
                    launchOptions = new LaunchOptions
                    {
                        Headless = true,
                        ExecutablePath = executablePath,
                        Args = new[] { 
                            "--no-sandbox", 
                            "--disable-setuid-sandbox", 
                            "--disable-dev-shm-usage",
                            "--disable-blink-features=AutomationControlled" 
                        }
                    };
                }
                else
                {
                    _logger.LogInformation("PUPPETEER_EXECUTABLE_PATH not set. Downloading Chromium...");
                    var browserFetcher = new BrowserFetcher();
                    await browserFetcher.DownloadAsync();
                    launchOptions = new LaunchOptions
                    {
                        Headless = true,
                        Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage" }
                    };
                }

                await using var browser = await Puppeteer.LaunchAsync(launchOptions);
                await using var page = await browser.NewPageAsync();

                await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
                await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

                int pageNum = 1;
                int totalPages = 1;

                string firstUrl = "https://au.jora.com/j?sp=homepage&trigger_source=homepage&q=&l=";
                _logger.LogInformation("Navigating to Jora...");

                await page.GoToAsync(firstUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }, Timeout = 30000 });

                var exportDir = Path.Combine(Directory.GetCurrentDirectory(), "DataExports");
                if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);
                await page.ScreenshotAsync(Path.Combine(exportDir, "debug_screen.png"));

                try
                {
                    await page.WaitForSelectorAsync(".job-card.result", new WaitForSelectorOptions { Timeout = 10000 });
                    totalPages = await GetTotalPages(page);
                    _logger.LogInformation("Detected total pages: {totalPages}", totalPages);
                }
                catch
                {
                    _logger.LogWarning("No job cards found on first page. Check debug_screen.png");
                }

                while (pageNum <= totalPages)
                {
                    string url = pageNum == 1 ? firstUrl : $"https://au.jora.com/j?sp=homepage&trigger_source=homepage&q=&l=&p={pageNum}";
                    _logger.LogInformation("Processing page {pageNum}/{totalPages}: {url}", pageNum, totalPages, url);

                    if (pageNum > 1)
                    {
                        await page.GoToAsync(url, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }, Timeout = 30000 });
                    }

                    try { await page.WaitForSelectorAsync(".job-card.result", new WaitForSelectorOptions { Timeout = 10000 }); }
                    catch { _logger.LogWarning("No jobs found at page {pageNum}. Stopping.", pageNum); break; }

                    await Task.Delay(2000);
                    int jobCount = await page.EvaluateFunctionAsync<int>("() => document.querySelectorAll('.job-card.result').length");

                    if (jobCount == 0) break;
                    int newJobsOnPage = 0;

                    for (int i = 0; i < jobCount; i++)
                    {
                        try
                        {
                            var jobInfo = await page.EvaluateFunctionAsync<JobInfo>($@"(index) => {{
                                const cards = Array.from(document.querySelectorAll('.job-card.result'));
                                const card = cards[index];
                                if (!card) return null;
                                const titleLink = card.querySelector('.job-title a.job-link');
                                const company = card.querySelector('.job-company');
                                const location = card.querySelector('.job-location');
                                const badges = Array.from(card.querySelectorAll('.badge .content'));
                                const salary = badges.find(b => b.textContent.includes('$') || b.textContent.includes('hour') || b.textContent.includes('year'));
                                const posted = card.querySelector('.job-listed-date');
                                const jobAbstract = card.querySelector('.job-abstract');
                                return {{
                                    title: titleLink ? titleLink.textContent.trim() : '',
                                    company: company ? company.textContent.trim() : '',
                                    location: location ? location.textContent.trim() : '',
                                    salary: salary ? salary.textContent.trim() : 'Not specified',
                                    postedDate: posted ? posted.textContent.trim() : '',
                                    url: titleLink ? titleLink.href : '',
                                    shortDescription: jobAbstract ? jobAbstract.textContent.trim() : '',
                                    description: '',
                                    descriptionHtml: ''
                                }};
                            }}", i);

                            if (jobInfo == null || string.IsNullOrEmpty(jobInfo.Title)) continue;
                            if (processedUrls.Contains(jobInfo.Url)) continue;
                            processedUrls.Add(jobInfo.Url);

                            try
                            {
                                var clickSelector = $".job-card.result:nth-of-type({i + 1}) a.job-link.show-job-description";
                                await page.ClickAsync(clickSelector);
                                await page.WaitForSelectorAsync(".jdv-panel .job-description-container", new WaitForSelectorOptions { Timeout = 5000 });
                                await Task.Delay(1000);
                                var descData = await page.EvaluateFunctionAsync<DescriptionData>(@"() => {{
                                    const descContainer = document.querySelector('.jdv-panel .job-description-container');
                                    return {{ text: descContainer ? descContainer.innerText.trim() : '', html: descContainer ? descContainer.innerHTML.trim() : '' }};
                                }}");
                                jobInfo.Description = CleanText(descData.Text);
                                jobInfo.DescriptionHtml = descData.Html;
                            }
                            catch { jobInfo.Description = jobInfo.ShortDescription; }

                            jobs.Add(jobInfo);
                            newJobsOnPage++;
                        }
                        catch (Exception ex) { _logger.LogError(ex, "Error on job {index}", i); }
                    }
                    if (newJobsOnPage == 0) break;
                    pageNum++;
                }

                _logger.LogInformation("Scraping complete. Total jobs: {count}", jobs.Count);
                
                // 1. Save locally
                await SaveJobsToExcel(jobs);

                // 2. Upload to your backend
                var finalFilePath = Path.Combine(exportDir, "JobsFromJora.xlsx");
                await UploadFileToApiAsync(finalFilePath);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error during scraping process."); }
        }

        private async Task UploadFileToApiAsync(string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                _logger.LogInformation("🚀 Uploading Excel file to backend...");
                using var client = _httpClientFactory.CreateClient();
                
                // CHANGE THIS URL TO YOUR ACTUAL PRODUCTION DOMAIN
                var uploadUrl = "https://your-production-api.com/api/job-files/upload";

                using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                using var content = new MultipartFormDataContent();
                
                var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

                content.Add(streamContent, "file", Path.GetFileName(filePath));
                request.Content = content;

                var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    _logger.LogInformation("✅ File successfully pushed to API.");
                else
                    _logger.LogError("❌ API Upload failed: {code}", response.StatusCode);
            }
            catch (Exception ex) { _logger.LogError(ex, "Upload Exception."); }
        }

        public async Task SaveJobsToExcel(List<JobInfo> jobs, string fileName = "JobsFromJora.xlsx")
        {
            ExcelPackage.License.SetNonCommercialPersonal("Your Name");
            var exportDir = Path.Combine(Directory.GetCurrentDirectory(), "DataExports");
            if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);
            var filePath = Path.Combine(exportDir, fileName);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Jobs");
            string[] headers = { "JobPostId", "Title", "Company", "Location", "Salary", "Posted Date", "URL", "Description" };
            for (int i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];

            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                ws.Cells[i + 2, 1].Value = job.JobPostId;
                ws.Cells[i + 2, 2].Value = job.Title;
                ws.Cells[i + 2, 3].Value = job.Company;
                ws.Cells[i + 2, 4].Value = job.Location;
                ws.Cells[i + 2, 5].Value = job.Salary;
                ws.Cells[i + 2, 6].Value = job.PostedDate;
                ws.Cells[i + 2, 7].Value = job.Url;
                ws.Cells[i + 2, 8].Value = job.DescriptionHtml;
            }
            ws.Cells.AutoFitColumns();
            await package.SaveAsAsync(new FileInfo(filePath));
        }

        private async Task<int> GetTotalPages(IPage page)
        {
            try {
                return await page.EvaluateFunctionAsync<int>(@"() => {
                    const indicator = document.querySelector('.search-results-page-number');
                    if (!indicator) return 1;
                    const match = indicator.textContent.match(/of\s+(\d+)/i);
                    return match ? parseInt(match[1]) : 1;
                }");
            } catch { return 1; }
        }

        private string CleanText(string text) => string.IsNullOrEmpty(text) ? text : Regex.Replace(text, @"[ \t]+", " ").Trim();

        private class DescriptionData { public string Text { get; set; } = ""; public string Html { get; set; } = ""; }
    }
}