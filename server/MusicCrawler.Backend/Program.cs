using MusicCrawler.Backend;

// TODO: I'm not sure if all of these settings are strictly necessary..
var builder = WebApplication.CreateBuilder();
builder.Services.AddGraphQLServer("gql")
    .ModifyRequestOptions(o => { o.IncludeExceptionDetails = true; })
    .AddQueryType<QueryType>();
builder.Services.AddCors();

if ("DevEnv_Main" == (Environment.GetEnvironmentVariable("DevEnv") ?? ""))
{
    builder.Host.RegisterAutofacModule<FakeModule>();
}
else
{
    builder.Host.RegisterAutofacModule<MainModule>();
}

var app = builder.Build();
app.UseCors(corsPolicyBuilder =>
{
    corsPolicyBuilder.AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader();
});
app.UseRouting();
app.MapGraphQL("/gql", "gql")
    .WithOptions(new()
    {
        Tool = { Enable = true }
    });
app.MapBananaCakePop();
app.Run();