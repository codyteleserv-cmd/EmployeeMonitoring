using EmployeeMonitoring.Dashboard.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<EmployeeMonitoring.Dashboard.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configuration
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

// SignalR
builder.Services.AddSingleton<HubConnection>(sp =>
{
    var hubBuilder = new HubConnectionBuilder()
        .WithUrl($"{builder.HostEnvironment.BaseAddress}hubs/admin", options =>
        {
            options.AccessTokenProvider = () => Task.FromResult<string?>(sp.GetRequiredService<AuthService>().GetToken());
        })
        .WithAutomaticReconnect();
    return hubBuilder.Build();
});

// Services
builder.Services.AddMudServices();
builder.Services.AddSingleton<AdminHubService>();
builder.Services.AddSingleton<AgentStateService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<ApiService>();
builder.Services.AddSingleton<NotificationService>();

// Authorization
builder.Services.AddAuthorizationCore();

await builder.Build().RunAsync();