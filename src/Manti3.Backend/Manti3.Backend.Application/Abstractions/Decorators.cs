using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Manti3.Backend.Application.Abstractions;

// קישוטים (decorators) להתנהגות רוחבית: ולידציה ולוגים
internal static class ValidationDecorator
{
    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        IEnumerable<IValidator<TQuery>> validators) : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct)
        {
            var failure = await Validate(validators, query, ct);
            return failure is null ? await inner.Handle(query, ct) : Result<TResponse>.Failure(failure);
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct)
        {
            var failure = await Validate(validators, command, ct);
            return failure is null ? await inner.Handle(command, ct) : Result<TResponse>.Failure(failure);
        }
    }

    private static async Task<Error?> Validate<T>(IEnumerable<IValidator<T>> validators, T request, CancellationToken ct)
    {
        var context = new ValidationContext<T>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct)));
        var errors = results.SelectMany(r => r.Errors).ToList();

        return errors.Count == 0
            ? null
            : Error.Validation("Validation", string.Join("; ", errors.Select(e => e.ErrorMessage)));
    }
}

internal static class LoggingDecorator
{
    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        ILogger<QueryHandler<TQuery, TResponse>> logger) : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct)
        {
            var name = typeof(TQuery).Name;
            logger.LogInformation("Handling {Query}", name);

            var result = await inner.Handle(query, ct);

            if (result.IsSuccess)
                logger.LogInformation("Handled {Query}", name);
            else
                logger.LogWarning("{Query} failed: {ErrorCode}", name, result.Error!.Code);

            return result;
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        ILogger<CommandHandler<TCommand, TResponse>> logger) : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct)
        {
            var name = typeof(TCommand).Name;
            logger.LogInformation("Handling {Command}", name);

            var result = await inner.Handle(command, ct);

            if (result.IsSuccess)
                logger.LogInformation("Handled {Command}", name);
            else
                logger.LogWarning("{Command} failed: {ErrorCode}", name, result.Error!.Code);

            return result;
        }
    }
}
