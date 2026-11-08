using Capri.Core.Models;

namespace Capri.Core.Abstractions.Automation;

/// <summary>
/// Maps spoken application aliases to concrete application targets.
/// </summary>
public interface IAppAliasResolver
{
    /// <summary>
    /// Returns the target for <paramref name="applicationAlias"/>, or null when the alias is unknown.
    /// </summary>
    ValueTask<AppTarget?> ResolveAsync(string applicationAlias, CancellationToken cancellationToken = default);
}
