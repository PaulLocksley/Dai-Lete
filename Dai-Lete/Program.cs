using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.Services;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<ConvertNewEpisodes>();
builder.Services.AddRazorPages();
builder.Services.AddMvc()
    .AddMvcOptions(o => o.OutputFormatters.Add(new XmlDataContractSerializerOutputFormatter()));

var app = builder.Build();

// Configure the HTTP request pipeline.
//todo: lmao.
/*if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}*/
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
//this is a silly hack to avoid fixing a bug.
//shows can contain local links to other files on that host that I'm not reflecting.
//it's hard to process because they are in the xml decorators and apparently they aren't rendered as normal nodes.
//todo: work out accessing base url and replacing all instances of local links with base url
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Podcasts")),
    RequestPath = "/global"
});

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

FeedCache.buildCache();

app.Run();