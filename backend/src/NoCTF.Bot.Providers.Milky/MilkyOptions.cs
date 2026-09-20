using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace NoCTF.Bot.Providers.Milky;

public sealed class MilkyOptions
{
    public const string SectionName = "Milky";

    [Required]
    public Uri BaseUrl { get; init; } = null!;

    [Required]
    public string AccessToken { get; init; } = string.Empty;

    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; init; } = 15;
}

internal sealed class MilkyOptionsValidator : IValidateOptions<MilkyOptions>
{
    public ValidateOptionsResult Validate(string? name, MilkyOptions options)
    {
        var failures = new List<string>();
        if (options.BaseUrl is null
            || !options.BaseUrl.IsAbsoluteUri
            || options.BaseUrl.Scheme != Uri.UriSchemeHttp
            && options.BaseUrl.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(options.BaseUrl.UserInfo)
            || options.BaseUrl.AbsolutePath != "/"
            || !string.IsNullOrEmpty(options.BaseUrl.Query)
            || !string.IsNullOrEmpty(options.BaseUrl.Fragment))
        {
            failures.Add("Milky:BaseUrl must be an HTTP(S) origin without credentials, path, query, or fragment.");
        }
        if (string.IsNullOrWhiteSpace(options.AccessToken))
            failures.Add("Milky:AccessToken is required.");
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
