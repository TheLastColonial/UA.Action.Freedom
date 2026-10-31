using System.Text.RegularExpressions;

namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// Nothing the Convoy Leader's checklist fetches may be kept on the phone (ADR 0016, X10). The header is added before
/// the response starts, for every status, so a refusal or an error is never cached either.
/// </summary>
internal sealed partial class ChecklistNoStoreMiddleware(RequestDelegate next)
{
    private const string CacheControl = "no-store, max-age=0";

    public Task InvokeAsync(HttpContext context)
    {
        if (IsChecklistRoute(context.Request.Path.Value))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = CacheControl;
                context.Response.Headers.Pragma = "no-cache";
                return Task.CompletedTask;
            });
        }

        return next(context);
    }

    internal static bool IsChecklistRoute(string? path) => path is not null && ChecklistRoutes().IsMatch(path);

    [GeneratedRegex(
        @"^/convoys/\d+/(progress|outcomes|costs|route/\d+/(reached|crossed)|boxes/\d+/outcome|vehicles/[^/]+/(delivered|boxes))/?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChecklistRoutes();
}

internal static class ChecklistNoStoreExtensions
{
    public static IApplicationBuilder UseFreedomChecklistNoStore(this IApplicationBuilder app) =>
        app.UseMiddleware<ChecklistNoStoreMiddleware>();
}
