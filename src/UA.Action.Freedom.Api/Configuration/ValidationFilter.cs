using FluentValidation;

namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// Endpoint filter that runs the registered FluentValidation validator for
/// <typeparamref name="T"/> against the endpoint's argument of that type and returns a 400
/// <c>ValidationProblem</c> when it fails. Endpoints with no <c>T</c> argument pass through.
/// </summary>
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();

        if (argument is not null)
        {
            var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);

            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }
        }

        return await next(context);
    }
}
