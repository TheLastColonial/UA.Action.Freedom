using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// Who is making this request's changes. Filled once per request by
/// <see cref="ChangeAttributionMiddleware"/> from the login, and read by the repositories that
/// stamp <c>LastChangedBy</c>. Nothing a caller sends can set it.
/// </summary>
internal sealed class RequestChangeAttribution : IChangeAttribution
{
    public Guid? PersonId { get; set; }
}

/// <summary>
/// Marks the few write endpoints an unlinked login may call: creating a volunteer and linking a
/// login to one are how a login becomes linked, so refusing them would leave a new deployment
/// with no way to link the first Administrator. Their changes are stamped with no person.
/// </summary>
internal sealed class AllowsUnlinkedLogin
{
    public static readonly AllowsUnlinkedLogin Instance = new();

    private AllowsUnlinkedLogin()
    {
    }
}

internal static class ChangeAttributionExtensions
{
    public static TBuilder AllowUnlinkedLogin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(AllowsUnlinkedLogin.Instance);

    public static IServiceCollection AddFreedomChangeAttribution(this IServiceCollection services)
    {
        services.AddScoped<RequestChangeAttribution>();
        services.AddScoped<IChangeAttribution>(provider => provider.GetRequiredService<RequestChangeAttribution>());
        return services;
    }

    public static IApplicationBuilder UseFreedomChangeAttribution(this IApplicationBuilder app) =>
        app.UseMiddleware<ChangeAttributionMiddleware>();
}

/// <summary>
/// Every write is recorded against the person who made it (ADR 0017), so a write from a login
/// no Administrator has linked to a volunteer is refused here with <c>403 login-not-linked</c>
/// before any handler runs — never stored against an "unknown" identity. Runs after
/// authorization, so a caller who lacks the role is told that first. Reads are untouched.
/// </summary>
internal sealed class ChangeAttributionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context, ICurrentPerson currentPerson, RequestChangeAttribution attribution)
    {
        if (RecordsAChange(context))
        {
            var caller = await currentPerson.ResolveAsync(context.RequestAborted);

            if (caller is CurrentPerson.Linked(var personId))
            {
                attribution.PersonId = personId;
            }
            else if (context.GetEndpoint()?.Metadata.GetMetadata<AllowsUnlinkedLogin>() is null)
            {
                await LoginNotLinked.Problem().ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }

    private static bool RecordsAChange(HttpContext context) =>
        context.GetEndpoint() is not null
        && context.User.Identity?.IsAuthenticated == true
        && !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method)
        && !HttpMethods.IsOptions(context.Request.Method);
}
