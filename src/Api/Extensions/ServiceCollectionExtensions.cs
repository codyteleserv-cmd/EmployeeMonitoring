using EmployeeMonitoring.Common.Health;
using EmployeeMonitoring.Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EmployeeMonitoring.Api.Extensions;

/// <summary>
/// Extension methods for service collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEmployeeMonitoringApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();

        services.AddHealthChecks()
            .AddCheck<EmployeeMonitoring.Common.Health.AgentHealthCheck>("agents");

        return services;
    }

    public static IApplicationBuilder UseEmployeeMonitoringApi(this IApplicationBuilder app)
    {
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseMiddleware<RateLimitingMiddleware>();
        return app;
    }
}

/// <summary>
/// Extension methods for HttpContext.
/// </summary>
public static class HttpContextExtensions
{
    public static string? GetCorrelationId(this HttpContext context)
    {
        return context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? context.Response.Headers["X-Correlation-ID"].FirstOrDefault();
    }

    public static string? GetUserId(this HttpContext context)
    {
        return context.User?.FindFirst("sub")?.Value
            ?? context.User?.FindFirst("user_id")?.Value;
    }

    public static string? GetUserRole(this HttpContext context)
    {
        return context.User?.FindFirst("role")?.Value;
    }
}
