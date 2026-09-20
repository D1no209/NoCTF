using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace NoCTF.Bot.Configuration;

public sealed class NoCtfBotOptions
{
    public const string SectionName = "NoCtf";

    [Required]
    public Uri BaseUrl { get; init; } = null!;

    [Required]
    public Uri PublicBaseUrl { get; init; } = null!;

    [Required]
    public string AccessToken { get; init; } = string.Empty;

    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; init; } = 15;
}

public sealed class RelayOptions
{
    public const string SectionName = "Bot";

    [Required]
    public string StatePath { get; init; } = "/data/noctf-bot.sqlite3";

    [Range(15, 3600)]
    public int SnapshotPollSeconds { get; init; } = 60;

    [Range(500, 1000)]
    public int RefreshCoalesceMilliseconds { get; init; } = 750;

    [Required]
    public string TimeZoneId { get; init; } = "Asia/Shanghai";

    [Required]
    public string Provider { get; init; } = "milky";

    [Required]
    public string MasterUserId { get; init; } = string.Empty;

    public string[] AllowedGroupIds { get; init; } = [];
}

internal sealed class NoCtfBotOptionsValidator : IValidateOptions<NoCtfBotOptions>
{
    public ValidateOptionsResult Validate(string? name, NoCtfBotOptions options)
    {
        var failures = new List<string>();
        ValidateHttpsOrigin(options.BaseUrl, nameof(options.BaseUrl), failures);
        ValidateHttpsOrigin(options.PublicBaseUrl, nameof(options.PublicBaseUrl), failures);
        if (string.IsNullOrWhiteSpace(options.AccessToken))
            failures.Add("NoCtf:AccessToken is required.");
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateHttpsOrigin(
        Uri? value,
        string name,
        ICollection<string> failures)
    {
        if (value is null
            || !value.IsAbsoluteUri
            || value.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(value.UserInfo)
            || value.AbsolutePath != "/"
            || !string.IsNullOrEmpty(value.Query)
            || !string.IsNullOrEmpty(value.Fragment))
        {
            failures.Add($"NoCtf:{name} must be an HTTPS origin without credentials, path, query, or fragment.");
        }
    }
}

internal sealed class RelayOptionsValidator : IValidateOptions<RelayOptions>
{
    public ValidateOptionsResult Validate(string? name, RelayOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.StatePath))
            failures.Add("Bot:StatePath is required.");
        if (string.IsNullOrWhiteSpace(options.Provider))
            failures.Add("Bot:Provider is required.");
        if (string.IsNullOrWhiteSpace(options.MasterUserId))
            failures.Add("Bot:MasterUserId is required.");
        if (options.AllowedGroupIds.Any(string.IsNullOrWhiteSpace))
            failures.Add("Bot:AllowedGroupIds may not contain empty group identifiers.");
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            failures.Add("Bot:TimeZoneId is not available on this host.");
        }
        catch (InvalidTimeZoneException)
        {
            failures.Add("Bot:TimeZoneId is invalid.");
        }
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
