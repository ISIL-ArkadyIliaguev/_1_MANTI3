using Manti3.Backend.Application.Abstractions;

namespace Manti3.Backend.Api.Extensions;

public static class ResultExtensions
{
    // הצלחה -> 200, כישלון צפוי -> ProblemDetails (RFC 9457)
    public static IResult ToHttpResult<T>(this Result<T> result)
        => result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();

    private static IResult ToProblem(this Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Problem(title: error.Code, detail: error.Description, statusCode: status);
    }
}
