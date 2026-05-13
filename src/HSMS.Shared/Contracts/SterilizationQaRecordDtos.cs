namespace HSMS.Shared.Contracts;

public enum SterilizationQaCategory
{
    Dashboard = 0,
    BowieDick = 1,
    LeakTest = 2,
    WarmUpTest = 3,
    InstrumentTests = 4,
    BiologicalIndicator = 5,
    Ppm = 6,
    MaintenanceCalibration = 7,
    FailedIncident = 8,
    Archived = 9
}

public enum SterilizationQaWorkflowStatus
{
    Draft = 0,
    PendingReview = 1,
    Approved = 2,
    Failed = 3,
    RetestRequired = 4,
    Archived = 5
}

public sealed class SterilizationQaRecordListItemDto
{
    public long RecordId { get; set; }
    public SterilizationQaCategory Category { get; set; }
    public int? SterilizationId { get; set; }
    public string? CycleNo { get; set; }
    public int? SterilizerId { get; set; }
    public string? SterilizerNo { get; set; }
    public DateTime TestDateTimeUtc { get; set; }
    public string? Department { get; set; }
    public string? Technician { get; set; }
    public SterilizationQaWorkflowStatus Status { get; set; }
    public string? ResultLabel { get; set; } // e.g. Pass/Fail/+, -, OK/NG
    public bool HasAttachments { get; set; }
    public bool IsLegacyQaTest { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SterilizationQaRecordQueryDto
{
    public SterilizationQaCategory? Category { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int? SterilizerId { get; set; }
    public int? SterilizationId { get; set; }
    public string? Technician { get; set; }
    public string? Department { get; set; }
    public int? ReviewerAccountId { get; set; } // matches ReviewedBy or ApprovedBy (enterprise), ApprovedBy (legacy)
    public SterilizationQaWorkflowStatus? Status { get; set; }
    public bool FailedOnly { get; set; }
    public bool PendingOnly { get; set; }
    public bool ReviewQueue { get; set; } // Pending review only, oldest-first, optimized for supervisors
    public string? Search { get; set; } // cycle no, sterilizer no, free text
    public int Skip { get; set; }
    public int Take { get; set; } = 200;
}

public sealed class SterilizationQaRecordCreateDto
{
    public SterilizationQaCategory Category { get; set; }
    public int? SterilizationId { get; set; }
    public int? SterilizerId { get; set; }
    public DateTime TestDateTimeUtc { get; set; } = DateTime.UtcNow;
    public string? Department { get; set; }
    public string? Technician { get; set; }
    public string? ResultLabel { get; set; }
    public string? Summary { get; set; }
    public string? Notes { get; set; }
    public string? ClientMachine { get; set; }
}

public sealed class SterilizationQaRecordStatusPatchDto
{
    public string RowVersion { get; set; } = string.Empty;
    public SterilizationQaWorkflowStatus NewStatus { get; set; }
    public string? Comment { get; set; }
    public string? ClientMachine { get; set; }
}

public sealed class SterilizationQaTimelineEventDto
{
    public DateTime EventAtUtc { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public SterilizationQaWorkflowStatus? Status { get; set; }
    public string? ActorUsername { get; set; }
}

