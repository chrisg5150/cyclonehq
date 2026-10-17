using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using CycloneHQ.App;
using CycloneHQ.App.Services;

// Numbers in SVG attributes must use "." decimals whatever the phone's language.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddSingleton<AppState>();
builder.Services.AddScoped<LeagueData>();
builder.Services.AddScoped<CycloneHQ.App.Services.Leagues.LeagueExtras>();
builder.Services.AddScoped<CycloneHQ.App.Services.Leagues.ManualLeagues>();
builder.Services.AddScoped<CycloneHQ.App.Services.Nfl.EspnScoreboard>();

await builder.Build().RunAsync();
