using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.FileProviders;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// Create separate metrics server
var metricsBuilder = WebApplication.CreateBuilder(args);
metricsBuilder.WebHost.UseUrls("http://localhost:4011");

// Add configuration options
builder.Services.Configure<PodcastOptions>(builder.Configuration.GetSection(PodcastOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));

// Add authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Name = "DaiLete.Auth";
    });

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.WebHost.UseSentry();

// Add OpenTelemetry metrics (shared between main and metrics servers)
var openTelemetryBuilder = builder.Services.AddOpenTelemetry()
    .WithMetrics(builder =>
    {
        builder
            .AddMeter("Dai_Lete.Podcast")
            .AddPrometheusExporter();
    });

// Configure metrics server
metricsBuilder.Services.AddOpenTelemetry()
    .WithMetrics(builder =>
    {
        builder
            .AddMeter("Dai_Lete.Podcast")
            .AddPrometheusExporter();
    });

// Register services
builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
builder.Services.AddSingleton<ConfigManager>();
builder.Services.AddSingleton<PodcastMetricsService>();

builder.Services.AddSingleton<PodcastServices>();
builder.Services.AddSingleton<RedirectService>();
builder.Services.AddSingleton<XmlService>();
builder.Services.AddSingleton<FeedCacheService>();
builder.Services.AddHostedService<ConvertNewEpisodes>();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Auth/Login");
    options.Conventions.AllowAnonymousToPage("/Redirect");
});
builder.Services.AddMvc()
    .AddMvcOptions(o => o.OutputFormatters.Add(new XmlDataContractSerializerOutputFormatter()));

// Add http client for redirects
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<RedirectCache>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.UseHttpsRedirection();
app.UseStaticFiles();
//add endpoint for podcast mp3s.
var podcastFolder = $"{AppDomain.CurrentDomain.BaseDirectory}Podcasts{Path.DirectorySeparatorChar}";
if (!Directory.Exists(podcastFolder))
{
    DirectoryInfo di = Directory.CreateDirectory(podcastFolder);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Podcasts")),
    RequestPath = "/Podcasts"
});
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

// Initialize database
var databaseService = app.Services.GetRequiredService<IDatabaseService>();
await databaseService.InitializeDatabaseAsync();
SqLite.Initialize(databaseService);

// Initialize FeedCache
var feedCacheService = app.Services.GetRequiredService<FeedCacheService>();
FeedCache.Initialize(feedCacheService);
await FeedCache.buildCache();

// Build and configure metrics server
var metricsApp = metricsBuilder.Build();
metricsApp.MapPrometheusScrapingEndpoint();

// Start metrics server in background
_ = Task.Run(() => metricsApp.RunAsync());

app.Run();