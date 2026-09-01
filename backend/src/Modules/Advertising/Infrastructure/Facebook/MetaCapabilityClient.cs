using System.Text.Json;

namespace Modules.Advertising.Infrastructure.Facebook;

public sealed record MetaWhatsAppPhone(string Id, string DisplayPhoneNumber, string VerifiedName, string QualityRating);
public sealed record MetaWaba(string Id, string Name, IReadOnlyList<MetaWhatsAppPhone> Phones);
public sealed record MetaCapabilityCatalog(
    IReadOnlyList<MetaResource> AdAccounts,
    IReadOnlyList<MetaResource> Pages,
    IReadOnlyList<MetaResource> Datasets,
    IReadOnlyList<MetaWaba> Wabas,
    IReadOnlyList<string> GrantedPermissions,
    MetaProviderTrace? LastTrace);
public sealed record MetaWhatsAppRuntimeProbe(bool Supported, string ObjectivesJson, string OptimizationGoalsJson,
    string BidStrategiesJson, string PlacementEligibilityJson, string ValidationSupportJson, string FailureCode, MetaProviderTrace? Trace);

public sealed class MetaCapabilityClient(MetaGraphClient graph)
{
    private const int MaximumFanOutPerDiscovery = 4;

    public async Task<MetaCapabilityCatalog> DiscoverAsync(string accessToken, string? adAccountId, CancellationToken cancellationToken = default)
    {
        var primaryResources = await LoadPrimaryResourcesAsync(accessToken, cancellationToken);
        var selectedAccountId = adAccountId ?? GetOptionalId(primaryResources.AdAccounts.FirstOrDefault());
        var secondaryResources = await LoadSecondaryResourcesAsync(
            accessToken,
            selectedAccountId,
            primaryResources.GrantedPermissions,
            cancellationToken);
        var whatsappAccounts = await LoadWhatsAppAccountsAsync(
            accessToken,
            secondaryResources.Businesses,
            cancellationToken);

        return new(
            primaryResources.AdAccounts.Select(ToMetaResource).ToArray(),
            primaryResources.Pages.Select(ToMetaResource).ToArray(),
            secondaryResources.Datasets.Select(ToMetaResource).ToArray(),
            whatsappAccounts,
            primaryResources.GrantedPermissions,
            primaryResources.Trace);
    }

    public async Task<MetaWhatsAppRuntimeProbe> ProbeAsync(string accessToken, string adAccountId, string pageId,
        string phoneNumberId, CancellationToken cancellationToken = default)
    {
        var accountTask = LoadProbeResourceAsync(
            $"{adAccountId}?fields=id,account_status,currency,timezone_name",
            accessToken,
            cancellationToken);
        var pageTask = LoadProbeResourceAsync($"{pageId}?fields=id,name", accessToken, cancellationToken);
        var phoneTask = LoadProbeResourceAsync($"{phoneNumberId}?fields=id,quality_rating", accessToken, cancellationToken);
        await Task.WhenAll(accountTask, pageTask, phoneTask);
        var account = await accountTask;
        var page = await pageTask;
        var phone = await phoneTask;
        var providerError = account.Error ?? page.Error ?? phone.Error;
        if (providerError is not null) return UnsupportedProbe(providerError);

        var eligible = account.Value.TryGetProperty("account_status", out var status) && status.GetInt32() == 1
            && page.Value.TryGetProperty("id", out _)
            && phone.Value.TryGetProperty("id", out _);
        return new(eligible, "[\"OUTCOME_ENGAGEMENT\",\"OUTCOME_LEADS\"]", "[\"CONVERSATIONS\"]",
            "[\"LOWEST_COST_WITHOUT_CAP\"]", "{\"automatic\":true,\"whatsappDestinationEligible\":true}",
            "{\"validateOnlyRequiredBeforeProvision\":true}", eligible ? string.Empty : "ADS_WHATSAPP_RESOURCES_INELIGIBLE", phone.Trace);
    }

    public async Task<MetaWhatsAppRuntimeProbe> ProbeGatewayAsync(string accessToken, string adAccountId, string pageId,
        CancellationToken cancellationToken = default)
    {
        var accountTask = LoadProbeResourceAsync(
            $"{adAccountId}?fields=id,account_status,currency,timezone_name",
            accessToken,
            cancellationToken);
        var pageTask = LoadProbeResourceAsync($"{pageId}?fields=id,name", accessToken, cancellationToken);
        await Task.WhenAll(accountTask, pageTask);
        var account = await accountTask;
        var page = await pageTask;
        var providerError = account.Error ?? page.Error;
        if (providerError is not null) return UnsupportedProbe(providerError);

        var eligible = account.Value.TryGetProperty("account_status", out var status) && status.GetInt32() == 1
            && page.Value.TryGetProperty("id", out _);
        return new(eligible, "[\"OUTCOME_ENGAGEMENT\",\"OUTCOME_LEADS\"]", "[\"CONVERSATIONS\"]",
            "[\"LOWEST_COST_WITHOUT_CAP\"]", "{\"automatic\":true,\"destination\":\"WHATSAPP_GATEWAY\"}",
            "{\"validateOnlyRequiredBeforeProvision\":true,\"businessMessagingCapi\":false}",
            eligible ? string.Empty : "ADS_META_RESOURCES_INELIGIBLE", page.Trace);
    }

    private async Task<ProbeResource> LoadProbeResourceAsync(
        string path,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var providerResponse = await graph.GetAsync(path, accessToken, cancellationToken);
            using var document = providerResponse.Value;
            return new(document.RootElement.Clone(), providerResponse.Trace, null);
        }
        catch (MetaGraphException providerError)
        {
            return new(default, providerError.Trace, providerError);
        }
    }

    private static MetaWhatsAppRuntimeProbe UnsupportedProbe(MetaGraphException providerError) =>
        new(false, "[]", "[]", "[]", "{}", "{}", providerError.Code, providerError.Trace);

    private async Task<PrimaryResources> LoadPrimaryResourcesAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var accountsTask = graph.GetAllAsync(
            "me/adaccounts?fields=id,name,currency,timezone_name,account_status&limit=100",
            accessToken,
            cancellationToken);
        var pagesTask = graph.GetAllAsync("me/accounts?fields=id,name&limit=100", accessToken, cancellationToken);
        var permissionsTask = LoadGrantedPermissionsAsync(accessToken, cancellationToken);
        await Task.WhenAll(accountsTask, pagesTask, permissionsTask);

        var permissionSnapshot = await permissionsTask;
        return new(await accountsTask, await pagesTask, permissionSnapshot.Permissions, permissionSnapshot.Trace);
    }

    private async Task<PermissionSnapshot> LoadGrantedPermissionsAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var permissionResponse = await graph.GetAsync("me/permissions", accessToken, cancellationToken);
        using var permissionsDocument = permissionResponse.Value;
        var grantedPermissions = permissionsDocument.RootElement.GetProperty("data").EnumerateArray()
            .Where(item => GetText(item, "status") == "granted")
            .Select(item => GetText(item, "permission"))
            .Where(permission => permission is not null)
            .Select(permission => permission!)
            .ToArray();
        return new(grantedPermissions, permissionResponse.Trace);
    }

    private async Task<SecondaryResources> LoadSecondaryResourcesAsync(
        string accessToken,
        string? selectedAccountId,
        IReadOnlyList<string> grantedPermissions,
        CancellationToken cancellationToken)
    {
        var datasetsPath = selectedAccountId is null
            ? null
            : $"{selectedAccountId}/adspixels?fields=id,name&limit=100";
        var businessesPath = grantedPermissions.Contains("whatsapp_business_management", StringComparer.Ordinal)
            ? "me/businesses?fields=id,name&limit=100"
            : null;
        var datasetsTask = LoadOptionalCollectionAsync(datasetsPath, accessToken, cancellationToken);
        var businessesTask = LoadOptionalCollectionAsync(businessesPath, accessToken, cancellationToken);
        await Task.WhenAll(datasetsTask, businessesTask);
        return new(await datasetsTask, await businessesTask);
    }

    private Task<IReadOnlyList<JsonElement>> LoadOptionalCollectionAsync(
        string? path,
        string accessToken,
        CancellationToken cancellationToken) =>
        path is null
            ? Task.FromResult<IReadOnlyList<JsonElement>>([])
            : graph.GetAllAsync(path, accessToken, cancellationToken);

    private async Task<IReadOnlyList<MetaWaba>> LoadWhatsAppAccountsAsync(
        string accessToken,
        IReadOnlyList<JsonElement> businesses,
        CancellationToken cancellationToken)
    {
        if (businesses.Count == 0) return [];

        using var providerConcurrency = new SemaphoreSlim(MaximumFanOutPerDiscovery);
        var ownedAccountGroups = await Task.WhenAll(businesses.Select(business => LoadBoundedCollectionAsync(
            $"{GetRequiredId(business)}/owned_whatsapp_business_accounts?fields=id,name&limit=100",
            accessToken,
            providerConcurrency,
            cancellationToken)));
        var ownedAccounts = ownedAccountGroups
            .SelectMany(group => group)
            .DistinctBy(GetRequiredId, StringComparer.Ordinal)
            .ToArray();
        var accountsWithPhones = await Task.WhenAll(ownedAccounts.Select(account =>
            LoadWhatsAppPhonesAsync(account, accessToken, providerConcurrency, cancellationToken)));
        return accountsWithPhones.Select(ToMetaWaba).ToArray();
    }

    private async Task<WhatsAppAccountWithPhones> LoadWhatsAppPhonesAsync(
        JsonElement account,
        string accessToken,
        SemaphoreSlim providerConcurrency,
        CancellationToken cancellationToken)
    {
        var phones = await LoadBoundedCollectionAsync(
            $"{GetRequiredId(account)}/phone_numbers?fields=id,display_phone_number,verified_name,quality_rating&limit=100",
            accessToken,
            providerConcurrency,
            cancellationToken);
        return new(account, phones);
    }

    private async Task<IReadOnlyList<JsonElement>> LoadBoundedCollectionAsync(
        string path,
        string accessToken,
        SemaphoreSlim providerConcurrency,
        CancellationToken cancellationToken)
    {
        await providerConcurrency.WaitAsync(cancellationToken);
        try
        {
            return await graph.GetAllAsync(path, accessToken, cancellationToken);
        }
        finally
        {
            providerConcurrency.Release();
        }
    }

    private static MetaWaba ToMetaWaba(WhatsAppAccountWithPhones account) => new(
        GetRequiredId(account.Account),
        GetText(account.Account, "name") ?? "WhatsApp Business Account",
        account.Phones.Select(phone => new MetaWhatsAppPhone(
            GetRequiredId(phone),
            GetText(phone, "display_phone_number") ?? string.Empty,
            GetText(phone, "verified_name") ?? string.Empty,
            GetText(phone, "quality_rating") ?? "UNKNOWN")).ToArray());

    private static MetaResource ToMetaResource(JsonElement value) => new(
        GetRequiredId(value),
        GetText(value, "name") ?? "Meta Resource",
        GetText(value, "currency"),
        GetText(value, "timezone_name"),
        value.TryGetProperty("account_status", out var status) ? status.GetInt32() : null);

    private static string? GetOptionalId(JsonElement value) =>
        value.ValueKind == JsonValueKind.Undefined ? null : GetText(value, "id");

    private static string GetRequiredId(JsonElement value) =>
        value.GetProperty("id").GetString()
        ?? throw new JsonException("Meta resource id cannot be null.");

    private static string? GetText(JsonElement value, string property) =>
        value.TryGetProperty(property, out var found) ? found.ToString() : null;

    private sealed record PrimaryResources(
        IReadOnlyList<JsonElement> AdAccounts,
        IReadOnlyList<JsonElement> Pages,
        IReadOnlyList<string> GrantedPermissions,
        MetaProviderTrace Trace);

    private sealed record PermissionSnapshot(IReadOnlyList<string> Permissions, MetaProviderTrace Trace);
    private sealed record SecondaryResources(IReadOnlyList<JsonElement> Datasets, IReadOnlyList<JsonElement> Businesses);
    private sealed record WhatsAppAccountWithPhones(JsonElement Account, IReadOnlyList<JsonElement> Phones);
    private sealed record ProbeResource(JsonElement Value, MetaProviderTrace Trace, MetaGraphException? Error);
}
