namespace Capri.Core.Models;

/// <summary>
/// Token accounting reported by a provider.
/// </summary>
/// <param name="PromptTokens">Tokens consumed by the prompt.</param>
/// <param name="CompletionTokens">Tokens produced in the completion.</param>
public sealed record TokenUsage(int PromptTokens, int CompletionTokens)
{
    /// <summary>
    /// Total number of tokens billed for the request.
    /// </summary>
    public int TotalTokens => PromptTokens + CompletionTokens;
}
