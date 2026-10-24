using AMS.Domain.Base;
using AMS.Domain.Exceptions;
using AMS.Infrastructure.Ums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AMS.Api.Infrastructure
{
    /// <summary>
    /// Turns refusals into the standard response body (responseCode / responseMessage) with a matching HTTP status:
    /// 403 denied by UMS (message = UMS's reason), 401 session ended (X-Session-End says why), 422 business rule, 404, 409, 503 UMS down.
    /// </summary>
    public sealed class AmsExceptionFilter : IAsyncExceptionFilter
    {
        private readonly ILogger<AmsExceptionFilter> _logger;
        public AmsExceptionFilter(ILogger<AmsExceptionFilter> logger) => _logger = logger;

        private sealed class ErrorRes : BaseResponse { }

        public Task OnExceptionAsync(ExceptionContext context)
        {
            var (status, code, message) = context.Exception switch
            {
                AccessDeniedException e => (StatusCodes.Status403Forbidden, ResponseCodes.Forbidden, e.Message),
                SessionEndedException e => (StatusCodes.Status401Unauthorized, ResponseCodes.Unauthorized, e.Message),
                AuthorizationUnavailableException e => (StatusCodes.Status503ServiceUnavailable, e.Code, e.Message),
                AmsException { Code: ResponseCodes.NotFound } e => (StatusCodes.Status404NotFound, e.Code, e.Message),
                AmsException { Code: ResponseCodes.Duplicate } e => (StatusCodes.Status409Conflict, e.Code, e.Message),
                AmsException e => (StatusCodes.Status422UnprocessableEntity, e.Code, e.Message),
                UnauthorizedAccessException e => (StatusCodes.Status401Unauthorized, ResponseCodes.Unauthorized, e.Message),
                _ => (StatusCodes.Status500InternalServerError, ResponseCodes.ApiError, "Something went wrong. The error has been logged.")
            };
            if (status == StatusCodes.Status500InternalServerError)
                _logger.LogError(context.Exception, "Unhandled error in {Path}", context.HttpContext.Request.Path);
            else if (context.Exception is AccessDeniedException denied)
                _logger.LogInformation("Denied by UMS: {Permission} - {Reason}", denied.Permission, denied.Message);
            if (context.Exception is SessionEndedException ended)
                context.HttpContext.Response.Headers[UmsClient.SessionEndHeader] = ended.Reason;

            context.Result = new ObjectResult(new ErrorRes { responseCode = code, responseMessage = message }) { StatusCode = status };
            context.ExceptionHandled = true;
            return Task.CompletedTask;
        }
    }
}
