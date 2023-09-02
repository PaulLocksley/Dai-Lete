using Dai_Lete.Models;
using Dai_Lete.ScheduledTasks;
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


app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

FeedCache.buildCache();

app.Run();