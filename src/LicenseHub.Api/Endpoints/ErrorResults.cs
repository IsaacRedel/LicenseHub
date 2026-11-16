using LicenseHub.Application.Common;

namespace LicenseHub.Api.Endpoints;

internal static class ErrorResults
{
    /// <summary>Maps a use case error to an RFC 9457 problem response that carries the stable error code.</summary>
    public static IResult ToProblem(this Error error)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        return error.Kind switch
        {
            ErrorKind.Validation => TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { [error.Field ?? "request"] = [error.Message] },
                detail: error.Message,
                extensions: extensions),
            ErrorKind.NotFound => TypedResults.Problem(
                detail: error.Message,
                statusCode: StatusCodes.Status404NotFound,
                extensions: extensions),
            ErrorKind.Conflict => TypedResults.Problem(
                detail: error.Message,
                statusCode: StatusCodes.Status409Conflict,
                extensions: extensions),
            ErrorKind.Unauthorized => TypedResults.Problem(
                detail: error.Message,
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: extensions),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Kind, "Unknown error kind."),
        };
    }
}
