using EmployeeMonitoring.Api.Data;
using EmployeeMonitoring.Api.Extensions;
using EmployeeMonitoring.Api.Hubs;
using EmployeeMonitoring.Api.GrpcServices;
using EmployeeMonitoring.Api.Jobs;
using EmployeeMonitoring.Api.Middleware;
using EmployeeMonitoring.Api.Services;
using EmployeeMonitoring.Common.Health;
using Quartz;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "EmployeeMonitoring.Api"));

// Database
builder.Services.AddDbContext<MonitoringDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.MigrationsAssembly("EmployeeMonitoring.Api")));

builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("AuditConnection"),
        npgsql => npgsql.MigrationsAssembly("EmployeeMonitoring.Api")));

// Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Authority = builder.Configuration["OpenIdConnect:Authority"];
    options.Audience = builder.Configuration["Jwt:Audience"];
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]!))
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
})
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.Authority = builder.Configuration["OpenIdConnect:Authority"];
    options.ClientId = builder.Configuration["OpenIdConnect:ClientId"];
    options.ClientSecret = builder.Configuration["OpenIdConnect:ClientSecret"];
    options.ResponseType = "code";
    options.SaveTokens = true;
    options.CallbackPath = builder.Configuration["OpenIdConnect:CallbackPath"];
    foreach (var scope in builder.Configuration.GetSection("OpenIdConnect:Scopes").Get<string[]>()!)
    {
        options.Scope.Add(scope);
    }
});

// Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
    options.AddPolicy("SecurityOnly", policy => policy.RequireRole("security", "admin"));
    options.AddPolicy("TeamLeadOrAbove", policy => policy.RequireRole("team_lead", "security", "admin"));
    options.AddPolicy("HROrAbove", policy => policy.RequireRole("hr", "security", "admin"));
});

// gRPC
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaxReceiveMessageSize = 10 * 1024 * 1024;
    options.MaxSendMessageSize = 10 * 1024 * 1024;
});

// SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
});

// Controllers & API
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Employee Monitoring API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new()
    {
        {
            new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowDashboard", policy =>
    {
        policy.WithOrigins("https://localhost:5002", "https://dashboard.company.com")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Mapster
// Mapster: register TypeAdapterConfig if needed
// builder.Services.AddMapster(); // requires Mapster.DependencyInjection

// Custom Services
builder.Services.AddScoped<IAgentRepository, AgentRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IScreenshotRepository, ScreenshotRepository>();
builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
builder.Services.AddScoped<IDlpRepository, DlpRepository>();
builder.Services.AddScoped<IAuditRepository, AuditRepository>();

builder.Services.AddSingleton<IAgentConnectionManager, AgentConnectionManager>();
builder.Services.AddSingleton<IAdminConnectionManager, AdminConnectionManager>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IReportService, ReportService>();

// Quartz jobs (IJob implementations — not IHostedService)
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();

    var healthKey = new Quartz.JobKey("AgentHealthMonitoring");
    q.AddJob<EmployeeMonitoring.Api.Jobs.AgentHealthMonitoringService>(opts => opts.WithIdentity(healthKey));
    q.AddTrigger(opts => opts.ForJob(healthKey).WithIdentity("AgentHealthMonitoring-trigger").WithCronSchedule("0 */1 * * * ?"));

    var retentionKey = new Quartz.JobKey("DataRetention");
    q.AddJob<EmployeeMonitoring.Api.Jobs.DataRetentionJob>(opts => opts.WithIdentity(retentionKey));
    q.AddTrigger(opts => opts.ForJob(retentionKey).WithIdentity("DataRetention-trigger").WithCronSchedule("0 0 2 * * ?"));

    var configKey = new Quartz.JobKey("ConfigurationDeployment");
    q.AddJob<EmployeeMonitoring.Api.Jobs.ConfigurationDeploymentJob>(opts => opts.WithIdentity(configKey));
    q.AddTrigger(opts => opts.ForJob(configKey).WithIdentity("ConfigurationDeployment-trigger").WithCronSchedule("0 */5 * * * ?"));
});
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

// Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<MonitoringDbContext>()
    .AddDbContextCheck<AuditDbContext>()
    .AddCheck<EmployeeMonitoring.Common.Health.AgentHealthCheck>("agents");

var app = builder.Build();

// Middleware Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseCors("AllowDashboard");

app.UseAuthentication();
app.UseAuthorization();

// Custom middleware
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();

// gRPC
app.MapGrpcService<AgentGrpcService>();
app.MapGrpcService<AdminGrpcService>();

// SignalR
app.MapHub<AgentHub>("/hubs/agent");
app.MapHub<AdminHub>("/hubs/admin");

// Controllers
app.MapControllers();

// Health
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new() { Predicate = _ => true });
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });

// Seed database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
    await db.Database.MigrateAsync();
    
    var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    await auditDb.Database.MigrateAsync();
}

app.Run();

public partial class Program { } // For testing