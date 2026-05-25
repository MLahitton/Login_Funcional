using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components;
using Frontend;
using Frontend.Services.Auth;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var navigation = sp.GetRequiredService<NavigationManager>();
    var apiBaseUrl = ResolveApiBaseUrl(config, navigation.BaseUri);

    return new HttpClient
    {
        BaseAddress = new Uri(apiBaseUrl)
    };
});

builder.Services.AddScoped<AuthApiService>();

static string ResolveApiBaseUrl(IConfiguration config, string pageBaseUri)
{
    var pageScheme = Uri.TryCreate(pageBaseUri, UriKind.Absolute, out var pageUri)
        ? pageUri.Scheme
        : "http";

    var configKey = string.Equals(pageScheme, "https", StringComparison.OrdinalIgnoreCase)
        ? "Api:BaseUrlHttps"
        : "Api:BaseUrlHttp";

    var apiBaseUrl = config[configKey]
        ?? config["Api:BaseUrl"]
        ?? (string.Equals(pageScheme, "https", StringComparison.OrdinalIgnoreCase)
            ? "https://localhost:5001/api/auth/"
            : "http://localhost:5000/api/auth/");

    return apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/";
}

await builder.Build().RunAsync();
