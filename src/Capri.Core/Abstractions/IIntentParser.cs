using Capri.Core.Models;

namespace Capri.Core.Abstractions;

/// <summary>
/// Converts raw user input into a structured intent.
/// </summary>
public interface IIntentParser
{
    /// <summary>
    /// Parses user input into an intent.
    /// </summary>
    Intent Parse(string input);
}
