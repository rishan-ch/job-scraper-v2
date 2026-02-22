using DotNetEnv;
using JoraScraper.Modules.Scraper.Interface;
using JoraScraper.Modules.Scraper.Service;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

Env.Load();
OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("Your Name");
// --- GITHUB ACTIONS RUNNER LOGIC ---
// This block runs BEFORE builder.Build() to handle CLI commands
if (args.Contains("--run-scraper"))
{
    var tempBuilder = WebApplication.CreateBuilder(args);
    
    tempBuilder.Services.AddLogging();
    
    // --- MISSING LINE BELOW ---
    tempBuilder.Services.AddHttpClient(); 
    // ---------------------------

    tempBuilder.Services.AddScoped<IScraperService, ScraperService>();
    
    var tempApp = tempBuilder.Build();
    
    using var scope = tempApp.Services.CreateScope();
    var scraper = scope.ServiceProvider.GetRequiredService<IScraperService>();
    
    Console.WriteLine("🚀 GitHub Action detected: Starting Jora Scraper...");
    await scraper.ScrapeAndSaveJobsAsync();
    Console.WriteLine("✅ Scrape process finished.");
    return; 
}
// -----------------------------------

var builder = WebApplication.CreateBuilder(args);

// EPPlus License

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

// Background service (Keep this for Render/Local, but GitHub bypasses it)
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