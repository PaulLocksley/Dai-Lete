using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.FileProviders;
using Prometheus;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var valkeyConnectionString = NormalizeValkeyConnectionString(builder.Configuration["Valkey:ConnectionString"] ?? "localhost:6379");
var valkeyConnection = ConnectionMultiplexer.Connect(valkeyConnectionString);

// Add configuration options
builder.Services.Configure<PodcastOptions>(builder.Configuration.GetSection(PodcastOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.Configure<ValkeyOptions>(builder.Configuration.GetSection(ValkeyOptions.SectionName));

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

builder.Services.AddDataProtection()
    .SetApplicationName("Dai-Lete")
    .PersistKeysToStackExchangeRedis(valkeyConnection, "Dai-Lete:DataProtection-Keys");

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.WebHost.UseSentry();

var workerEnabled = builder.Configuration.GetValue("Worker:Enabled", true);

// Add Prometheus metrics server on port 4011 for the web frontend only.
if (!workerEnabled)
{
    builder.Services.AddMetricServer(options =>
    {
        options.Port = 4011;
    });
}

// Register services
builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
builder.Services.AddSingleton<ConfigManager>();
builder.Services.AddSingleton<PodcastMetricsService>();

builder.Services.AddSingleton<PodcastServices>();
builder.Services.AddSingleton<RedirectService>();
builder.Services.AddSingleton<XmlService>();
builder.Services.AddSingleton<FeedCacheService>();
builder.Services.AddSingleton<IConnectionMultiplexer>(valkeyConnection);
builder.Services.AddSingleton<IEpisodeJobQueue, ValkeyEpisodeJobQueue>();

if (workerEnabled)
{
    builder.Services.AddHostedService<ConvertNewEpisodes>();
}

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


if (app.Configuration.GetValue("HttpsRedirection:Enabled", false))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
var configManager = app.Services.GetRequiredService<ConfigManager>();
var podcastFolder = configManager.GetPodcastStoragePath();
if (!Directory.Exists(podcastFolder))
{
    Directory.CreateDirectory(podcastFolder);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(podcastFolder),
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

app.Run();

static string NormalizeValkeyConnectionString(string connectionString)
{
    if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) ||
        (uri.Scheme != "valkey" && uri.Scheme != "redis" && uri.Scheme != "rediss"))
    {
        return connectionString;
    }

    var normalized = $"{uri.Host}:{uri.Port}";
    if (!string.IsNullOrEmpty(uri.UserInfo))
    {
        var password = uri.UserInfo.Split(':', 2).LastOrDefault();
        if (!string.IsNullOrEmpty(password))
        {
            normalized += $",password={Uri.UnescapeDataString(password)}";
        }
    }

    if (uri.Scheme == "rediss")
    {
        normalized += ",ssl=true";
    }

    return normalized;
}
