using JoraScraper.Modules.Scraper.Dto;
using JoraScraper.Modules.Scraper.Interface;
using System.Text.RegularExpressions;
using OfficeOpenXml;
using PuppeteerSharp;

namespace JoraScraper.Modules.Scraper.Service
{
    public class ScraperService : IScraperService
    {
        private readonly ILogger<ScraperService> _logger;

        public ScraperService(ILogger<ScraperService> logger)
        {
            _logger = logger;
        }

        public async Task ScrapeAndSaveJobsAsync()
        {
            var jobs = new List<JobInfo>();
            var processedUrls = new HashSet<string>();

            try
            {
                _logger.LogInformation("Starting browser setup...");
                var executablePath = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");
                
                var launchOptions = new LaunchOptions
                {
                    Headless = true,
                    ExecutablePath = executablePath,
                    // Anti-Detection Flags
                    Args = new[] { 
                        "--no-sandbox", 
                        "--disable-setuid-sandbox", 
                        "--disable-dev-shm-usage",
                        "--disable-blink-features=AutomationControlled" // Hides WebDriver flag
                    }
                };

                // Local fallback if path not set
                if (string.IsNullOrEmpty(executablePath))
                {
                    _logger.LogInformation("Downloading Chromium for local run...");
                    await new BrowserFetcher().DownloadAsync();
                }

                await using var browser = await Puppeteer.LaunchAsync(launchOptions);
                await using var page = await browser.NewPageAsync();

                // 1. Set realistic Viewport and UserAgent
                await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
                await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

                // 2. Navigate with human-like behavior
                string targetUrl = "https://au.jora.com/j?q=&l=";
                _logger.LogInformation("Navigating to Jora...");
                
                await page.GoToAsync(targetUrl, new NavigationOptions { 
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }, 
                    Timeout = 60000 
                });

                // Wait for dynamic content to settle
                await Task.Delay(5000);

                // 3. Take Debug Screenshot (Always helpful for GitHub Actions)
                var exportDir = Path.Combine(Directory.GetCurrentDirectory(), "DataExports");
                if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);
                await page.ScreenshotAsync(Path.Combine(exportDir, "debug_screen.png"));

                // 4. Extract Jobs
                try
                {
                    await page.WaitForSelectorAsync(".job-card", new WaitForSelectorOptions { Timeout = 15000 });
                    
                    var jobCards = await page.EvaluateFunctionAsync<List<JobInfo>>(@"() => {
                        const results = [];
                        const cards = document.querySelectorAll('.job-card');
                        cards.forEach(card => {
                            const link = card.querySelector('a.job-link');
                            const company = card.querySelector('.job-company');
                            const location = card.querySelector('.job-location');
                            const abstract = card.querySelector('.job-abstract');
                            
                            results.push({
                                title: link ? link.innerText.trim() : 'Unknown',
                                url: link ? link.href : '',
                                company: company ? company.innerText.trim() : 'N/A',
                                location: location ? location.innerText.trim() : 'N/A',
                                shortDescription: abstract ? abstract.innerText.trim() : '',
                                postedDate: card.querySelector('.job-listed-date')?.innerText.trim() || ''
                            });
                        });
                        return results;
                    }");

                    if (jobCards != null) jobs.AddRange(jobCards);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("No job cards found or selector timeout: {msg}", ex.Message);
                }

                _logger.LogInformation("Scraping complete. Collected {count} jobs.", jobs.Count);
                await SaveJobsToExcel(jobs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical failure in ScraperService.");
            }
        }

        public async Task SaveJobsToExcel(List<JobInfo> jobs, string fileName = "JobsFromJora.xlsx")
        {
            // EPPlus 8+ License Requirement
            ExcelPackage.License.SetNonCommercialPersonal("Your Name");

            var exportDir = Path.Combine(Directory.GetCurrentDirectory(), "DataExports");
            if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);

            var filePath = Path.Combine(exportDir, fileName);
            if (File.Exists(filePath)) File.Delete(filePath);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Jobs");

            // Headers
            string[] headers = { "Title", "Company", "Location", "Posted Date", "URL", "Summary" };
            for (int i = 0; i < headers.Length; i++) {
                ws.Cells[1, i + 1].Value = headers[i];
                ws.Cells[1, i + 1].Style.Font.Bold = true;
            }

            // Data
            for (int i = 0; i < jobs.Count; i++)
            {
                ws.Cells[i + 2, 1].Value = jobs[i].Title;
                ws.Cells[i + 2, 2].Value = jobs[i].Company;
                ws.Cells[i + 2, 3].Value = jobs[i].Location;
                ws.Cells[i + 2, 4].Value = jobs[i].PostedDate;
                ws.Cells[i + 2, 5].Value = jobs[i].Url;
                ws.Cells[i + 2, 6].Value = jobs[i].ShortDescription;
            }

            ws.Cells.AutoFitColumns();
            await package.SaveAsAsync(new FileInfo(filePath));
            _logger.LogInformation("✅ Excel saved to: {path}", filePath);
        }
    }
}