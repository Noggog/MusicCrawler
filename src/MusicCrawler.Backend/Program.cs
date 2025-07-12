using MusicCrawler.Backend;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Host.RegisterAutofacModule<MainModule>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();

app.Run();