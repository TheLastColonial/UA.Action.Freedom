using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;

namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// Persists the ASP.NET Core data-protection key ring outside the container.
/// </summary>
/// <remarks>
/// This is <c>docs/recommendations.md</c> §3.2, and it is the single most common way a
/// Container Apps deployment of an ASP.NET Core app appears to randomly log people out.
/// Replicas are ephemeral: by default the key ring is generated in the container
/// filesystem, so auth cookies and antiforgery tokens break on every restart and every
/// scale-out — and with <c>minReplicas: 0</c> that means after every quiet period.
/// <para>
/// It is wired up locally, against Azurite, precisely so the behaviour is exercised long
/// before a user meets it. Azure adds <c>ProtectKeysWithAzureKeyVault</c> on top; the key
/// ring location is the half that matters for the logout symptom.
/// </para>
/// </remarks>
public static class DataProtectionExtensions
{
    private const string KeyRingBlobName = "keys.xml";

    public static IServiceCollection AddFreedomDataProtection(
        this IServiceCollection services,
        StorageOptions storage)
    {
        var builder = services.AddDataProtection().SetApplicationName("ua-action-freedom");

        if (!storage.IsConfigured)
        {
            // No storage account: fall back to the in-container key ring so the application
            // still starts. Acceptable for a developer running `dotnet run` on its own,
            // never acceptable in a deployed environment.
            return services;
        }

        // Built through StorageExtensions rather than with a bare constructor so the key
        // ring read inherits the bounded retry policy. That read happens during startup,
        // so the SDK's default backoff would otherwise become the cold-start time.
        var container = StorageExtensions
            .CreateBlobServiceClient(storage)
            .GetBlobContainerClient(storage.DataProtectionContainer);

        builder.PersistKeysToAzureBlobStorage(container.GetBlobClient(KeyRingBlobName));

        return services;
    }
}
