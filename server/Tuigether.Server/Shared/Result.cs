using System.Diagnostics;

namespace Tuigether.Server.Shared;

public abstract record Result<T, TError>
{
    private Result() { }

    public sealed record Ok(T Value) : Result<T, TError>;

    public sealed record Error(TError Value) : Result<T, TError>;

    public static implicit operator Result<T, TError>(Result.OkValue<T> ok) => new Ok(ok.Value);

    public static implicit operator Result<T, TError>(Result.ErrorValue<TError> error) => new Error(error.Value);

    public TOut Match<TOut>(Func<T, TOut> ok, Func<TError, TOut> error) =>
        this switch
        {
            Ok o => ok(o.Value),
            Error e => error(e.Value),
            _ => throw new UnreachableException(),
        };

    public Result<TOut, TError> Map<TOut>(Func<T, TOut> map) =>
        Match<Result<TOut, TError>>(value => Result.Ok(map(value)), static error => Result.Error(error));

    public Result<TOut, TError> Bind<TOut>(Func<T, Result<TOut, TError>> bind) =>
        Match(bind, static error => Result.Error(error));

    public Result<T, TOut> MapError<TOut>(Func<TError, TOut> map) =>
        Match<Result<T, TOut>>(static value => Result.Ok(value), error => Result.Error(map(error)));
}

public static class Result
{
    public static OkValue<T> Ok<T>(T value) => new(value);

    public static ErrorValue<TError> Error<TError>(TError error) => new(error);

    /// <summary>
    /// Converts to any <see cref="Result{T, TError}"/> with this <typeparamref name="T"/>, so a caller writes no error type.
    /// </summary>
    public readonly record struct OkValue<T>(T Value);

    /// <summary>
    /// Converts to any <see cref="Result{T, TError}"/> with this <typeparamref name="TError"/>, so a caller writes no value type.
    /// </summary>
    public readonly record struct ErrorValue<TError>(TError Value);
}

public static class ResultTaskExtensions
{
    extension<T, TError>(Task<Result<T, TError>> task)
    {
        public async Task<TOut> Match<TOut>(Func<T, TOut> ok, Func<TError, TOut> error) =>
            (await task).Match(ok, error);

        public async Task<Result<TOut, TError>> Map<TOut>(Func<T, TOut> map) => (await task).Map(map);

        public async Task<Result<TOut, TError>> Bind<TOut>(Func<T, Task<Result<TOut, TError>>> bind) =>
            await (await task).Match(bind, static error => Task.FromResult<Result<TOut, TError>>(Result.Error(error)));

        public async Task<Result<T, TOut>> MapError<TOut>(Func<TError, TOut> map) => (await task).MapError(map);
    }
}
