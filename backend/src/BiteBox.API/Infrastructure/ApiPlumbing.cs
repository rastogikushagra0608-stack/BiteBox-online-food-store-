using System.Security.Claims;
using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using BiteBox.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BiteBox.API.Infrastructure;

/// <summary>Reads the signed-in user from the auth cookie's claims principal.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public int? UserId =>
        int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    public int RequireUserId() => UserId ?? throw new UnauthorizedException();
}

/// <summary>
/// Central exception → HTTP mapping (like @ControllerAdvice in Spring). Every error response uses the
/// RFC 7807 ProblemDetails shape with a stable "code" extension the Angular app can switch on.
/// Unexpected exceptions are logged with full detail but returned to clients as a generic 500.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, code, title, detail) = exception switch
        {
            ValidationException v => (400, "validation_failed", "Validation failed", v.Errors.FirstOrDefault()?.ErrorMessage ?? v.Message),
            BadRequestException e => (400, e.Code, "Invalid request", e.Message),
            UnauthorizedException e => (401, e.Code, "Unauthorized", e.Message),
            ForbiddenException e => (403, e.Code, "Forbidden", e.Message),
            NotFoundException e => (404, e.Code, "Not found", e.Message),
            ConflictException e => (409, e.Code, "Conflict", e.Message),
            DomainException e => (422, e.Code, "Business rule violated", e.Message),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => (499, "client_closed", "Request cancelled", "The request was cancelled."),
            _ => (500, "server_error", "Something went wrong", "An unexpected error occurred. Please try again.")
        };

        if (status == 500) logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else if (status != 499) logger.LogInformation("Request rejected with {Status} {Code}", status, code);

        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = context.Request.Path };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (exception is ValidationException ve)
            problem.Extensions["errors"] = ve.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => Camel(g.Key), g => g.Select(e => e.ErrorMessage).ToArray());

        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }

    internal static string Camel(string name) => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}

/// <summary>
/// Runs the FluentValidation validator (if one exists) for every action argument before the action executes,
/// so controllers never see invalid input. Returns 400 with per-field errors.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (!result.IsValid) throw new ValidationException(result.Errors);
        }
        await next();
    }
}

/// <summary>
/// CSRF protection for cookie-authenticated APIs: every POST/PUT/PATCH/DELETE must carry an X-XSRF-TOKEN
/// header that matches the anti-forgery cookie. Actions marked [IgnoreAntiforgeryToken] are exempt.
/// (MVC's AutoValidateAntiforgeryToken filter needs the Razor view services, which a pure API doesn't load.)
/// </summary>
public sealed class AntiforgeryValidationFilter(Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS", "TRACE"];

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (SafeMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase)) return;

        var policy = context.ActionDescriptor.EndpointMetadata.OfType<Microsoft.AspNetCore.Mvc.ViewFeatures.IAntiforgeryPolicy>().LastOrDefault();
        if (policy is IgnoreAntiforgeryTokenAttribute) return;

        if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request",
                Detail = "Your session security token is missing or expired. Please refresh and try again.",
            };
            problem.Extensions["code"] = "csrf_invalid";
            context.Result = new BadRequestObjectResult(problem);
        }
    }
}

/// <summary>Adds conservative security headers to every response.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        return next(context);
    }
}
