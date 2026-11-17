using System.Text.RegularExpressions;

namespace InFarm.Mcp.Gdal.Infrastructure;

internal static partial class Redactor
{
    public const string Mask = "***";

    // Returns true when a configuration key or connection parameter is likely to hold a credential.
    public static bool IsSecretKey(string key)
    {
        return SecretKeyPattern().IsMatch(key);
    }

    // Replaces credential values in GDAL messages, URLs and connection strings with a mask.
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = KeyValuePattern().Replace(text, match => $"{match.Groups[1].Value}{match.Groups[2].Value}{Mask}");
        return UrlUserInfoPattern().Replace(result, match => $"{match.Groups[1].Value}{Mask}@");
    }

    [GeneratedRegex("(SECRET|PASSWORD|PASSWD|PWD|TOKEN|CREDENTIAL|SIGNATURE|ACCESS_KEY|ACCOUNT_KEY|API_?KEY|SAS|USERPWD|AUTH|COOKIE|HEADER|PRIVATE_KEY|CLIENT_ID|SESSION)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretKeyPattern();

    [GeneratedRegex(@"((?:password|passwd|pwd|secret|token|sig|signature|X-Amz-Signature|X-Amz-Credential|X-Amz-Security-Token|access_key|accesskey|api_?key)\b)(\s*[=:]\s*)([^\s&;,'""]+)", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValuePattern();

    [GeneratedRegex(@"([A-Za-z][A-Za-z0-9+.\-]*://)[^/\s:@]+:[^/\s@]+@")]
    private static partial Regex UrlUserInfoPattern();
}
