using ApplicationLayer.Common;
using ApplicationLayer.Exceptions;
using ApplicationLayer.Interfaces;
using DomainLayer.Entities;
using DomainLayer.Enumerations;
using DomainLayer.Exceptions;
using FluentValidation;

namespace WebApi.Middleware
{
    /// <summary>
    /// Turns known exceptions into RFC 7807 problem responses and records unexpected ones in the ExceptionLogs table
    /// with request context (tenant, user, path, IP, stack trace).
    /// </summary>
    public class ExceptionLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionLoggingMiddleware> _logger;

        public ExceptionLoggingMiddleware(RequestDelegate next, ILogger<ExceptionLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context, ILogRepository logs, ICurrentTenantService tenant, ICurrentUserService user, TimeProvider time)
        {
            try
            {
                await _next(context);
            }
            catch (ValidationException ex)
            {
                var errors = ex.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                await Results.ValidationProblem(errors, title: errors.Values.SelectMany(v => v).FirstOrDefault() ?? "Validation failed").ExecuteAsync(context);
            }
            catch (NotFoundException ex)
            {
                await Results.Problem(title: ex.Message, statusCode: StatusCodes.Status404NotFound).ExecuteAsync(context);
            }
            catch (Exception ex) when (ex is AppException or DomainException or ArgumentException)
            {
                await Results.Problem(title: ex.Message, statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
            }
            catch (UnauthorizedAccessException ex)
            {
                await Results.Problem(title: ex.Message, statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
            }
            catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
                try
                {
                    await logs.AddExceptionAsync(new ExceptionLog(Guid.NewGuid(), tenant.TenantId, LocalClock.Now(time), "UMS", ExceptionSeverity.Error,
                        $"{ex.GetType().Name}: {ex.Message}", ex.ToString(), user.UserName, $"{context.Request.Method} {context.Request.Path}{context.Request.QueryString}",
                        context.Connection.RemoteIpAddress?.ToString() ?? "", false));
                }
                catch (Exception logEx)
                {
                    _logger.LogError(logEx, "Could not write the exception log");
                }
                await Results.Problem(title: "An unexpected error occurred. It has been logged.", statusCode: StatusCodes.Status500InternalServerError)
                    .ExecuteAsync(context);
            }
        }
    }
}
