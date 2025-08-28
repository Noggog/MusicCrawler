using MusicCrawler.Backend;
using MusicCrawler.Backend.Services.Singletons;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisDistributedCache("cache");
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Host.RegisterAutofacModule<MainModule>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/artists", (ILibraryProvider libraryProvider) =>
    {
        return libraryProvider.GetAllArtistMetadata();
    })
    .WithName("GetArtists");

app.MapDefaultEndpoints();

app.Run();