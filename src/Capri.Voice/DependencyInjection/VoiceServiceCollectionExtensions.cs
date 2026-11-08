using Capri.Core.Abstractions.Voice;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Capri.Voice.DependencyInjection;

/// <summary>
/// Registration helpers for voice services.
/// </summary>
public static class VoiceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Phase 1 no-op speech synthesizer and recognizer.
    /// </summary>
    public static IServiceCollection AddCapriVoice(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISpeechSynthesizer, NullSpeechSynthesizer>();
        services.TryAddSingleton<ISpeechRecognizer, NullSpeechRecognizer>();

        return services;
    }
}
