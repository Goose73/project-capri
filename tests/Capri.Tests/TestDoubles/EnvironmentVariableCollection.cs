namespace Capri.Tests.TestDoubles;

/// <summary>
/// Collection for tests that mutate process-wide environment variables. These
/// tests must not run concurrently with other test collections.
/// </summary>
[CollectionDefinition(EnvironmentVariableTestGroup.Name, DisableParallelization = true)]
public sealed class EnvironmentVariableTestGroup
{
    public const string Name = "EnvironmentVariables";
}
