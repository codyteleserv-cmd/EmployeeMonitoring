using EmployeeMonitoring.Dashboard.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configuration
builder.Services.AddScoped(sp => new HttpClient 
{ 
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) 
});

// SignalR
builder.Services.AddSingleton<HubConnectionBuilder>(sp => new HubConnectionBuilder()
    .WithUrl($"{builder.HostEnvironment.BaseAddress}hubs/admin", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult<string?>(sp.GetRequiredService<AuthService>().GetToken());
    })
    .WithAutomaticReconnect()
    .Build());

// gRPC
builder.Services.AddGrpcClient<AdminService.AdminServiceClient>(options =>
{
    options.Address = new Uri(builder.HostEnvironment.BaseAddress);
})
.ConfigureChannel(options =>
{
    options.Credentials = ChannelCredentials.Insecure; // Use secure in production
});

// Services
builder.Services.AddMudServices();
builder.Services.AddSingleton<AdminHubService>();
builder.Services.AddSingleton<AgentStateService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ApiService>();
builder.Services.AddScoped<NotificationService>();

// Authorization
builder.Services.AddAuthorizationCore();

await builder.Build().RunAsync();