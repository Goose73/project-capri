using Capri.Core.Models;

namespace Capri.Core.Abstractions;

/// <summary>
/// Selects the provider and model for a chat request.
/// </summary>
public interface IModelRouter
{
    /// <summary>
    /// Resolves the route for the supplied request characteristics.
    /// </summary>
    ValueTask<ModelRoute> ResolveAsync(ModelRouteRequest request, CancellationToken cancellationToken = default);
}
