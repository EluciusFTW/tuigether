using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Tuigether.Server;

public static class Problems
{
    /// <summary>
    /// Continues the request on Ok. Rejects the Command with 409 and the rule's reason on Error.
    /// </summary>
    public static ProblemDetails FromRule<T>(Result<T, string> rule) =>
        rule.Match(
            static _ => WolverineContinue.NoProblems,
            static reason => new ProblemDetails { Status = StatusCodes.Status409Conflict, Detail = reason });
}
