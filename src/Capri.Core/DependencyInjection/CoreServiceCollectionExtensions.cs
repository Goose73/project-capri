using Capri.Core.Abstractions;
using Capri.Core.Abstractions.Automation;
using Capri.Core.Autonomy;
using Capri.Core.Avatar;
using Capri.Core.Conversation;
using Capri.Core.Intents;
using Capri.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Capri.Core.DependencyInjection;

/// <summary>
/// Registration helpers for core orchestration services.
/// </summary>
/// <remarks>
/// <c>AddCapriCore</c> registers the avatar state machine, conversation store,
/// intent parser, and assistant orchestrator. It does not register the model
/// router or chat provider (Providers project), settings/credentials/permissions
/// (Security project), or automation/voice implementations (their projects).
/// </remarks>
public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers Phase 1 core services.
    /// </summary>
    public static IServiceCollection AddCapriCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAvatarStateMachine, AvatarStateMachine>();
        services.TryAddSingleton<IConversationStore, InMemoryConversationStore>();
        services.TryAddSingleton<IIntentParser, RuleBasedIntentParser>();
        services.TryAddSingleton<IAssistantOrchestrator>(serviceProvider =>
            new AssistantOrchestrator(
                serviceProvider.GetRequiredService<IIntentParser>(),
                serviceProvider.GetRequiredService<IAutomationService>(),
                serviceProvider.GetRequiredService<IModelRouter>(),
                serviceProvider.GetRequiredService<IChatProvider>(),
                serviceProvider.GetRequiredService<IConversationStore>(),
                serviceProvider.GetRequiredService<IAvatarStateMachine>(),
                serviceProvider.GetService<ILogger<AssistantOrchestrator>>(),
                serviceProvider.GetService<ITaskRunner>() ?? serviceProvider.GetService<TaskCoordinator>()));

        return services;
    }
}
