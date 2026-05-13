using HSMS.LegacyMigrator.Infrastructure;

namespace HSMS.LegacyMigrator.Steps;

internal interface IMigrationStep
{
    string Name { get; }
    Task<StepResult> RunAsync(MigrationContext context, MappingStore mappings, FindingStore findings, CancellationToken ct);
}

internal sealed record StepResult(int Read, int Inserted, int Skipped, int Errors)
{
    public static StepResult Empty => new(0, 0, 0, 0);
}
