using MudBlazor;
using MudBlazor.Services;
using UMS.AdminPortal.Components;
using UMS.AdminPortal.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.VisibleStateDuration = 5000;
});

// the portal is a client of the UMS Web API (WebApi project); it holds no data itself
var apiBase = builder.Configuration["UmsApi:BaseUrl"] ?? throw new InvalidOperationException("UmsApi:BaseUrl is not configured.");
builder.Services.AddHttpClient<UmsApiClient>(c => c.BaseAddress = new Uri(apiBase.TrimEnd('/') + "/"));
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<TenantState>();
builder.Services.AddScoped<Ui>();
builder.Services.AddScoped<ClientInfo>();

var app = builder.Build();

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
