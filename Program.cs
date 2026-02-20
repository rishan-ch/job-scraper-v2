using JoraScraper.Modules.Scraper.Interface;
using JoraScraper.Modules.Scraper.Service;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Jora Scraper API", Version = "v1" });
});

// 2. Register Scraper Dependencies
builder.Services.AddHttpClient(); // Required for UploadFileToApiAsync
builder.Services.AddScoped<IScraperService, ScraperService>();

// --- SCRAPER CLI RUNNER LOGIC ---
// This block detects the --run-scraper flag from GitHub Actions
if (args.Contains("--run-scraper"))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();

    var appForCli = builder.Build();
    using var scope = appForCli.Services.CreateScope();
    var scraper = scope.ServiceProvider.GetRequiredService<IScraperService>();

    Console.WriteLine("------------------------------------------");
    Console.WriteLine("🚀 Starting Scraper via CLI Execution...");
    Console.WriteLine("------------------------------------------");

    try
    {
        await scraper.ScrapeAndSaveJobsAsync();
        Console.WriteLine("✅ Scraper finished successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Critical error during execution: {ex.Message}");
        Environment.Exit(1); // Exit with error code for GitHub Actions
    }

    return; // Stop here so the web server doesn't start
}
// --------------------------------

var app = builder.Build();

// 3. Configure the HTTP request pipeline (Web Server Mode)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Allow your frontend to access the API if needed
app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

app.UseAuthorization();
app.MapControllers();

app.Run();