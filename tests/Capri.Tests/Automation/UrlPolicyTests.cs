using Capri.Automation.Web;

namespace Capri.Tests.Automation;

public sealed class UrlPolicyTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?q=1")]
    [InlineData("HTTPS://EXAMPLE.COM/A")]
    [InlineData("https://example.com:8443/a%20b")]
    [InlineData("https://sub.example.co.uk/path#fragment")]
    public void AbsoluteHttpAndHttpsAddressesAreAccepted(string candidate)
    {
        var result = UrlPolicy.Validate(candidate);

        Assert.True(result.IsValid, result.Reason);
        Assert.NotNull(result.Uri);
        Assert.Null(result.Reason);
        Assert.True(
            string.Equals(result.Uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || string.Equals(result.Uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("ms-settings:display")]
    [InlineData("ms-windows-store://home")]
    [InlineData("shell:startup")]
    [InlineData("search-ms:query=test")]
    [InlineData("https://user@example.com")]
    [InlineData("https://user:password@example.com/path")]
    [InlineData("https://@evil.com")]
    [InlineData(@"\\server\share")]
    [InlineData("//server/share")]
    [InlineData("example.com")]
    [InlineData("www.example.com")]
    [InlineData(@"https://example.com\@evil.com")]
    [InlineData("https://exa mple.com")]
    [InlineData("mailto:user@example.com")]
    public void UnsafeOrAmbiguousAddressesAreRejected(string? candidate)
    {
        var result = UrlPolicy.Validate(candidate);

        Assert.False(result.IsValid);
        Assert.Null(result.Uri);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData("file:///C:/Windows", "file")]
    [InlineData("javascript:alert(1)", "javascript")]
    [InlineData("ms-settings:display", "ms-settings")]
    [InlineData("shell:startup", "shell")]
    [InlineData("search-ms:query=test", "search-ms")]
    public void RejectionReasonNamesTheDangerousScheme(string candidate, string scheme)
    {
        var result = UrlPolicy.Validate(candidate);

        Assert.False(result.IsValid);
        Assert.Contains(scheme, result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidateExposesParsedUri()
    {
        var accepted = UrlPolicy.TryValidate(
            "https://example.com/a",
            out var uri,
            out var reason);

        Assert.True(accepted);
        Assert.NotNull(uri);
        Assert.Null(reason);
        Assert.Equal("example.com", uri.Host);
    }
}
