using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace WismoAI.Api.Middleware;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Eroare prinsă în WISMO AI: {Message}", exception.Message);

        var problemDetails = new ProblemDetails
        {
            Instance = httpContext.Request.Path
        };

        if (exception is ValidationException validationException)
        {
            problemDetails.Title = "Validation Failed";
            problemDetails.Status = StatusCodes.Status400BadRequest;
            problemDetails.Detail = "Datele trimise nu sunt valide.";

            var errors = validationException.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.ErrorMessage).ToArray()
                );

            problemDetails.Extensions.Add("errors", errors);
        }
        else
        {
            problemDetails.Status = StatusCodes.Status500InternalServerError;
            problemDetails.Title = "Internal Server Error";
            problemDetails.Detail = "Eroare internă la procesarea cererii. Echipa a fost notificată.";
        }

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}