using System.Text.Json;
using HSMS.LegacyMigrator.Infrastructure;
using HSMS.LegacyMigrator.Logging;
using HSMS.LegacyMigrator.Steps;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace HSMS.LegacyMigrator;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddCommandLine(args)
            .Build();

        var legacy = config.GetConnectionString("Legacy")
            ?? throw new InvalidOperationException("ConnectionStrings:Legacy is not configured.");
        var hsms = config.GetConnectionString("Hsms")
            ?? throw new InvalidOperationException("ConnectionStrings:Hsms is not configured.");

        bool dryRun = bool.TryParse(config["Migration:DryRun"], out var dr) ? dr : true;
        if (HasFlag(args, "--apply")) dryRun = false;
        if (HasFlag(args, "--dry-run")) dryRun = true;

        int batchSize = int.TryParse(config["Migration:BatchSize"], out var bs) ? bs : 500;

        var baseDir = AppContext.BaseDirectory;
        var logsDir = Path.Combine(baseDir, "logs");
        var reportsDir = Path.Combine(baseDir, "reports");
        Directory.CreateDirectory(logsDir);
        Directory.CreateDirectory(reportsDir);

        if (HasFlag(args, "--rollback"))
        {
            return await RunRollbackAsync(args, legacy, hsms, logsDir, reportsDir, batchSize);
        }

        var context = new MigrationContext
        {
            LegacyConnectionString = legacy,
            HsmsConnectionString = hsms,
            DryRun = dryRun,
            BatchSize = batchSize,
            LogsDirectory = logsDir,
            ReportsDirectory = reportsDir,
        };

        MigrationLog.Initialize(logsDir, context.RunId);
        MigrationLog.Info($"Starting migration run {context.RunId} (DryRun={dryRun}).");

        await EnsureRunRowAsync(context);

        var mappings = new MappingStore(context);
        await mappings.LoadAsync();

        var findings = new FindingStore(context);

        var steps = new IMigrationStep[]
        {
            new SterilizersStep(),
            new DepartmentsStep(),
            new DoctorsRoomsStep(),
            new SterilizationsStep(),
            new QaTestsStep(),
        };

        var summary = new Dictionary<string, StepResult>();
        var status = "Completed";
        try
        {
            foreach (var step in steps)
            {
                MigrationLog.Info($"-- Step: {step.Name} --");
                var result = await step.RunAsync(context, mappings, findings, CancellationToken.None);
                summary[step.Name] = result;
                MigrationLog.Info($"{step.Name}: read={result.Read} inserted={result.Inserted} skipped={result.Skipped} errors={result.Errors}");
            }
        }
        catch (Exception ex)
        {
            MigrationLog.Error("Fatal: " + ex);
            status = "Failed";
        }
        finally
        {
            await findings.FlushAsync();
            await CompleteRunAsync(context, status, summary, findings);
            await WriteReportAsync(context, summary, findings);
            MigrationLog.Close(status);
        }

        return status == "Completed" ? 0 : 1;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static async Task<int> RunRollbackAsync(string[] args, string legacy, string hsms,
        string logsDir, string reportsDir, int batchSize)
    {
        var idArg = args.FirstOrDefault(a => a.StartsWith("--run=", StringComparison.OrdinalIgnoreCase));
        if (idArg is null || !Guid.TryParse(idArg["--run=".Length..], out var runId))
        {
            Console.Error.WriteLine("Rollback requires --run=<GUID>.");
            return 2;
        }
        var ctx = new MigrationContext
        {
            LegacyConnectionString = legacy,
            HsmsConnectionString = hsms,
            DryRun = false,
            BatchSize = batchSize,
            LogsDirectory = logsDir,
            ReportsDirectory = reportsDir,
        };
        MigrationLog.Initialize(logsDir, runId);
        MigrationLog.Info($"Rolling back run {runId}.");
        try
        {
            await Rollback.RunAsync(ctx, runId, CancellationToken.None);
            MigrationLog.Close("RolledBack");
            return 0;
        }
        catch (Exception ex)
        {
            MigrationLog.Error("Rollback failed: " + ex);
            MigrationLog.Close("Failed");
            return 1;
        }
    }

    private static async Task EnsureRunRowAsync(MigrationContext ctx)
    {
        if (ctx.DryRun) return;
        await using var conn = ctx.OpenHsms();
        const string sql = @"INSERT INTO dbo.migration_runs(run_id, dry_run, status)
                             VALUES(@runId, @dry, N'Running')";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@runId", ctx.RunId);
        cmd.Parameters.AddWithValue("@dry", ctx.DryRun);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CompleteRunAsync(MigrationContext ctx, string status, Dictionary<string, StepResult> summary, FindingStore findings)
    {
        if (ctx.DryRun) return;
        await using var conn = ctx.OpenHsms();
        const string sql = @"UPDATE dbo.migration_runs
                             SET status = @status,
                                 completed_at_utc = SYSUTCDATETIME(),
                                 summary_json = @json
                             WHERE run_id = @runId";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@runId", ctx.RunId);
        cmd.Parameters.AddWithValue("@status", status);
        var payload = new
        {
            steps = summary.Select(kv => new { entity = kv.Key, kv.Value.Read, kv.Value.Inserted, kv.Value.Skipped, kv.Value.Errors }).ToArray(),
            warnings = findings.CountBy(FindingSeverity.Warning),
            errors = findings.CountBy(FindingSeverity.Error),
        };
        cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(payload));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task WriteReportAsync(MigrationContext ctx, Dictionary<string, StepResult> summary, FindingStore findings)
    {
        var path = Path.Combine(ctx.ReportsDirectory, $"migration_{ctx.RunId:N}.md");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# HSMS Legacy Migration Report");
        sb.AppendLine();
        sb.AppendLine($"- Run ID: `{ctx.RunId}`");
        sb.AppendLine($"- DryRun: `{ctx.DryRun}`");
        sb.AppendLine($"- Started: {DateTime.UtcNow:O}");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Entity | Read | Inserted | Skipped | Errors |");
        sb.AppendLine("|--------|------|----------|---------|--------|");
        foreach (var (entity, r) in summary)
        {
            sb.AppendLine($"| {entity} | {r.Read} | {r.Inserted} | {r.Skipped} | {r.Errors} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Findings");
        sb.AppendLine();
        if (findings.All.Count == 0)
        {
            sb.AppendLine("_No findings._");
        }
        else
        {
            sb.AppendLine("| Severity | Entity | Legacy ID | Code | Message |");
            sb.AppendLine("|----------|--------|-----------|------|---------|");
            foreach (var f in findings.All)
            {
                sb.AppendLine($"| {f.Severity} | {f.Entity} | {f.LegacyId} | {f.Code} | {EscapePipes(f.Message)} |");
            }
        }
        await File.WriteAllTextAsync(path, sb.ToString());
        MigrationLog.Info($"Report written: {path}");
    }

    private static string EscapePipes(string s) => s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
