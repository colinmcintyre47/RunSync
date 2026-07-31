// Middleware/ExceptionHandlingMiddleware.cs
// Global exception handler that wraps the entire request pipeline.
// Catches any unhandled exception and returns a structured JSON error response
// so the client never receives an HTML error page or a raw stack trace.
//
// Response shape:
//   { "statusCode": 500, "message": "An unexpected error occurred.", "traceId": "..." }
//
// Registered as the FIRST middleware in Program.cs so it catches exceptions from
// all subsequent middleware and from MVC controllers.
//
// In production, the full exception details are logged to ILogger (forwarded to
// AWS CloudWatch via the runtime's logging provider). The client only sees a safe message.
//
// → Registered in Program.cs before all other middleware
// → Known exceptions (InvalidOperationException, UnauthorizedAccessException) map to
//   specific HTTP status codes. Everything else becomes a 500.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace RunSync.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Log with full exception details — these appear in CloudWatch but not in the response
        _logger.LogError(exception, "Unhandled exception on {Method} {Path}",
            context.Request.Method, context.Request.Path);

        (HttpStatusCode statusCode, string message) = exception switch
        {
            // StravaCredentialException derives from InvalidOperationException, so it lands here
            // and keeps its message — those messages tell the user how to fix their Strava app setup.
            InvalidOperationException => (HttpStatusCode.BadRequest, exception.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "You are not authorized to perform this action."),
            KeyNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            // Strava unreachable or returning an unexpected status — upstream's fault, not the caller's.
            HttpRequestException => (HttpStatusCode.BadGateway, "Strava could not be reached. Please try again in a moment."),
            // Never let a decryption failure reach the client verbatim — it would leak key state.
            CryptographicException => (HttpStatusCode.InternalServerError, "A stored secret could not be read."),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        object errorResponse = new
        {
            statusCode = (int)statusCode,
            message,
            // TraceIdentifier lets support teams correlate a user-reported error with a log entry
            traceId = context.TraceIdentifier
        };

        string json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

// Extension method for clean registration in Program.cs
public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionHandlingMiddleware>();
}
