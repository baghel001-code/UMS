using AMS.Infrastructure;
using AMS.Web.Components;
using AMS.Web.Services;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.VisibleStateDuration = 6000;
});

// AMS data (EF Core) + UMS client: sign-in and every authorisation decision come from UMS (Ums:BaseUrl)
builder.Services.AddAms(builder.Configuration);
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<Ui>();
builder.Services.AddSingleton<DemoData>();

var app = builder.Build();

await app.Services.InitialiseAmsDatabaseAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
