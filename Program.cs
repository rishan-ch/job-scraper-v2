using DotNetEnv;
using JoraScraper.Modules.Scraper.Interface;
using JoraScraper.Modules.Scraper.Service;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OfficeOpenXml;

Env.Load();

// --- GITHUB ACTIONS RUNNER LOGIC ---
// This handles the CLI command 'dotnet run -- --run-scraper'
if (args.Contains("--run-scraper"))
{
    var tempBuilder = WebApplication.CreateBuilder(args);
    
    // FIX: Register HttpClient and Logging for the ScraperService
    tempBuilder.Services.AddHttpClient(); 
    tempBuilder.Services.AddLogging(logging => {
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    });
    
    tempBuilder.Services.AddScoped<IScraperService, ScraperService>();
    
    var tempApp = tempBuilder.Build();
    
    using var scope = tempApp.Services.CreateScope();
    var scraper = scope.ServiceProvider.GetRequiredService<IScraperService>();
    
    Console.WriteLine("GitHub Action detected: Starting Jora Scraper...");
    try 
    {
        await scraper.ScrapeAndSaveJobsAsync();
        Console.WriteLine("Scrape process finished successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Critical Error during CLI scrape: {ex.Message}");
        Environment.Exit(1); // Tell GitHub Actions the job failed
    }
    return; // Exit application immediately
}
// -----------------------------------

var builder = WebApplication.CreateBuilder(args);

// EPPlus License - Global Setting
ExcelPackage.License.SetNonCommercialPersonal("Your Name");

// Add HttpClient for the ScraperService and the BackgroundService
builder.Services.AddHttpClient();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "https://www.dsailorgroup.com.au",
                "https://dsailor-vercel.vercel.app",
                "https://dsailorgroup.comm.au",
                "https://localhost:5193"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Scraper service
builder.Services.AddScoped<IScraperService, ScraperService>();

// Background service (Runs only when the Web Server is running)
builder.Services.AddHostedService<JobScraperBackgroundService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseAuthorization();
app.MapControllers();

app.Run();