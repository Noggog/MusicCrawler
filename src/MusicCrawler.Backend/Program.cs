using MusicCrawler.Backend;
using MusicCrawler.Lib;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
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

app.MapGet("/artists", (ILibraryQuery libraryQuery) =>
    {
        return libraryQuery.QueryAllArtistMetadata();
    })
    .WithName("GetArtists");

app.MapDefaultEndpoints();

app.Run();