using HSMS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Persistence.Data;

public sealed class HsmsDbContext(DbContextOptions<HsmsDbContext> options) : DbContext(options)
{
    public DbSet<AccountLogin> Accounts => Set<AccountLogin>();
    public DbSet<SterilizerUnit> SterilizerUnits => Set<SterilizerUnit>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DepartmentItem> DepartmentItems => Set<DepartmentItem>();
    public DbSet<DoctorRoom> DoctorRooms => Set<DoctorRoom>();
    public DbSet<CycleProgram> CyclePrograms => Set<CycleProgram>();
    public DbSet<Sterilization> Sterilizations => Set<Sterilization>();
    public DbSet<SterilizationItem> SterilizationItems => Set<SterilizationItem>();
    public DbSet<CycleReceipt> CycleReceipts => Set<CycleReceipt>();
    public DbSet<CycleReceiptAsset> CycleReceiptAssets => Set<CycleReceiptAsset>();
    public DbSet<CycleReceiptDerivationState> CycleReceiptDerivationStates => Set<CycleReceiptDerivationState>();
    public DbSet<QaTest> QaTests => Set<QaTest>();
    public DbSet<SterilizationQaRecord> SterilizationQaRecords => Set<SterilizationQaRecord>();
    public DbSet<SterilizationQaStatusEvent> SterilizationQaStatusEvents => Set<SterilizationQaStatusEvent>();
    public DbSet<SterilizationQaAttachment> SterilizationQaAttachments => Set<SterilizationQaAttachment>();
    public DbSet<SterilizationQaPreset> SterilizationQaPresets => Set<SterilizationQaPreset>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PrintLog> PrintLogs => Set<PrintLog>();
    public DbSet<InstrumentCheck> InstrumentChecks => Set<InstrumentCheck>();
    public DbSet<InstrumentCheckAttachment> InstrumentCheckAttachments => Set<InstrumentCheckAttachment>();
    public DbSet<AnalyticsPreset> AnalyticsPresets => Set<AnalyticsPreset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SterilizerUnit>(e =>
        {
            e.ToTable("tbl_sterilizer_no");
            e.HasKey(x => x.SterilizerId);
            e.Property(x => x.SterilizerId).HasColumnName("sterilizer_id");
            e.Property(x => x.SterilizerNumber).HasColumnName("sterilizer_no");
            e.Property(x => x.Model).HasColumnName("model");
            e.Property(x => x.Manufacturer).HasColumnName("manufacturer");
            e.Property(x => x.SerialNumber).HasColumnName("serial_number").HasMaxLength(64);
            e.Property(x => x.MaintenanceSchedule).HasColumnName("maintenance_schedule").HasMaxLength(128);
            e.Property(x => x.PurchaseDate).HasColumnName("purchase_date");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.DisabledAt).HasColumnName("disabled_at");
            e.Property(x => x.DisabledBy).HasColumnName("disabled_by");
        });

        modelBuilder.Entity<Department>(e =>
        {
            e.ToTable("tbl_departments");
            e.HasKey(x => x.DepartmentId);
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.DepartmentCode).HasColumnName("department_code");
            e.Property(x => x.DepartmentName).HasColumnName("department_name");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.DisabledAt).HasColumnName("disabled_at");
            e.Property(x => x.DisabledBy).HasColumnName("disabled_by");
        });

        modelBuilder.Entity<DoctorRoom>(e =>
        {
            e.ToTable("tbl_doctors_rooms");
            e.HasKey(x => x.DoctorRoomId);
            e.Property(x => x.DoctorRoomId).HasColumnName("doctor_room_id");
            e.Property(x => x.DoctorName).HasColumnName("doctor_name");
            e.Property(x => x.Room).HasColumnName("room");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.DisabledAt).HasColumnName("disabled_at");
            e.Property(x => x.DisabledBy).HasColumnName("disabled_by");
        });

        modelBuilder.Entity<DepartmentItem>(e =>
        {
            e.ToTable("tbl_dept_items");
            e.HasKey(x => x.DeptItemId);
            e.Property(x => x.DeptItemId).HasColumnName("dept_item_id");
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.ItemCode).HasColumnName("item_code");
            e.Property(x => x.ItemName).HasColumnName("item_name");
            e.Property(x => x.DefaultPcs).HasColumnName("default_pcs");
            e.Property(x => x.DefaultQty).HasColumnName("default_qty");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.DisabledAt).HasColumnName("disabled_at");
            e.Property(x => x.DisabledBy).HasColumnName("disabled_by");
        });

        modelBuilder.Entity<CycleProgram>(e =>
        {
            e.ToTable("tbl_cycle_programs");
            e.HasKey(x => x.CycleProgramId);
            e.Property(x => x.CycleProgramId).HasColumnName("cycle_program_id");
            e.Property(x => x.ProgramCode).HasColumnName("program_code").HasMaxLength(32);
            e.Property(x => x.ProgramName).HasColumnName("program_name").HasMaxLength(64);
            e.Property(x => x.SterilizationType).HasColumnName("sterilization_type").HasMaxLength(32);
            e.Property(x => x.DefaultTemperatureC).HasColumnName("default_temperature_c").HasPrecision(6, 2);
            e.Property(x => x.DefaultPressure).HasColumnName("default_pressure").HasPrecision(8, 3);
            e.Property(x => x.DefaultExposureMinutes).HasColumnName("default_exposure_minutes");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.DisabledAt).HasColumnName("disabled_at");
            e.Property(x => x.DisabledBy).HasColumnName("disabled_by");
        });

        modelBuilder.Entity<AccountLogin>(e =>
        {
            e.ToTable("tbl_account_login");
            e.HasKey(x => x.AccountId);
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.Username).HasColumnName("username");
            e.Property(x => x.PasswordHash).HasColumnName("password_hash");
            e.Property(x => x.Role).HasColumnName("role");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(80);
            e.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(80);
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(128);
            e.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(40);
            e.Property(x => x.Department).HasColumnName("department").HasMaxLength(128);
            e.Property(x => x.JobTitle).HasColumnName("job_title").HasMaxLength(128);
            e.Property(x => x.EmployeeId).HasColumnName("employee_id").HasMaxLength(32);
        });

        modelBuilder.Entity<Sterilization>(e =>
        {
            e.ToTable("tbl_sterilization");
            e.HasKey(x => x.SterilizationId);
            e.HasIndex(x => x.CycleNo).IsUnique();
            e.Property(x => x.SterilizationId).HasColumnName("sterilization_id");
            e.Property(x => x.CycleNo).HasColumnName("cycle_no");
            e.Property(x => x.SterilizerId).HasColumnName("sterilizer_id");
            e.Property(x => x.SterilizationType).HasColumnName("sterilization_type");
            e.Property(x => x.CycleProgram).HasColumnName("cycle_program").HasMaxLength(40);
            e.Property(x => x.CycleDateTime).HasColumnName("cycle_datetime");
            e.Property(x => x.CycleTimeIn).HasColumnName("cycle_time_in");
            e.Property(x => x.CycleTimeOut).HasColumnName("cycle_time_out");
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .ValueGeneratedOnAdd();
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
            e.Property(x => x.OperatorName).HasColumnName("operator_name");
            e.Property(x => x.TemperatureC).HasColumnName("temperature_c").HasPrecision(6, 2);
            e.Property(x => x.TemperatureInC).HasColumnName("temperature_in_c").HasPrecision(6, 2);
            e.Property(x => x.TemperatureOutC).HasColumnName("temperature_out_c").HasPrecision(6, 2);
            e.Property(x => x.Pressure).HasColumnName("pressure").HasPrecision(8, 3);
            e.Property(x => x.ExposureTimeMinutes).HasColumnName("exposure_time_minutes");
            e.Property(x => x.BiResult).HasColumnName("bi_result");
            e.Property(x => x.BiResultUpdatedAt).HasColumnName("bi_result_updated_at");
            e.Property(x => x.BiLotNo).HasColumnName("bi_lot_no");
            e.Property(x => x.BiStripNo).HasColumnName("bi_strip_no");
            e.Property(x => x.BiTimeIn).HasColumnName("bi_time_in");
            e.Property(x => x.BiTimeOut).HasColumnName("bi_time_out");
            e.Property(x => x.BiTimeCut).HasColumnName("bi_time_cut");
            e.Property(x => x.BiDaily).HasColumnName("bi_daily");
            e.Property(x => x.BiIncubatorTemp).HasColumnName("bi_incubator_temp").HasMaxLength(48);
            e.Property(x => x.BiIncubatorChecked).HasColumnName("bi_incubator_checked");
            e.Property(x => x.BiTimeInInitials).HasColumnName("bi_time_in_initials").HasMaxLength(32);
            e.Property(x => x.BiTimeOutInitials).HasColumnName("bi_time_out_initials").HasMaxLength(32);
            e.Property(x => x.BiProcessedResult24m).HasColumnName("bi_processed_result_24m").HasMaxLength(1);
            e.Property(x => x.BiProcessedValue24m).HasColumnName("bi_processed_value_24m");
            e.Property(x => x.BiProcessedResult24h).HasColumnName("bi_processed_result_24h").HasMaxLength(1);
            e.Property(x => x.BiProcessedValue24h).HasColumnName("bi_processed_value_24h");
            e.Property(x => x.BiControlResult24m).HasColumnName("bi_control_result_24m").HasMaxLength(1);
            e.Property(x => x.BiControlValue24m).HasColumnName("bi_control_value_24m");
            e.Property(x => x.BiControlResult24h).HasColumnName("bi_control_result_24h").HasMaxLength(1);
            e.Property(x => x.BiControlValue24h).HasColumnName("bi_control_value_24h");
            e.Property(x => x.LoadQty).HasColumnName("load_qty");
            e.Property(x => x.CycleStatus).HasColumnName("cycle_status");
            e.Property(x => x.DoctorRoomId).HasColumnName("doctor_room_id");
            e.Property(x => x.Implants).HasColumnName("implants");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();

            e.HasMany(s => s.Items)
                .WithOne()
                .HasForeignKey(i => i.SterilizationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(s => s.Receipts)
                .WithOne()
                .HasForeignKey(r => r.SterilizationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(s => s.QaTests)
                .WithOne()
                .HasForeignKey(q => q.SterilizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SterilizationItem>(e =>
        {
            e.ToTable("tbl_str_items");
            e.HasKey(x => x.SterilizationItemId);
            e.Property(x => x.SterilizationItemId).HasColumnName("sterilization_item_id");
            e.Property(x => x.SterilizationId).HasColumnName("sterilization_id");
            e.Property(x => x.DeptItemId).HasColumnName("dept_item_id");
            e.Property(x => x.DepartmentName).HasColumnName("department_name").HasMaxLength(256);
            e.Property(x => x.DoctorOrRoom).HasColumnName("doctor_or_room").HasMaxLength(256);
            e.Property(x => x.ItemName).HasColumnName("item_name");
            e.Property(x => x.Pcs).HasColumnName("pcs");
            e.Property(x => x.Qty).HasColumnName("qty");
            e.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();
        });

        modelBuilder.Entity<InstrumentCheck>(e =>
        {
            e.ToTable("tbl_instrument_checks");
            e.HasKey(x => x.InstrumentCheckId);
            e.Property(x => x.InstrumentCheckId).HasColumnName("instrument_check_id");
            e.Property(x => x.CheckedAtUtc).HasColumnName("checked_at_utc")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.ItemName).HasColumnName("item_name").HasMaxLength(256);
            e.Property(x => x.SerialReference).HasColumnName("serial_reference").HasMaxLength(128);
            e.Property(x => x.CheckedByName).HasColumnName("checked_by_name").HasMaxLength(128);
            e.Property(x => x.WitnessByName).HasColumnName("witness_by_name").HasMaxLength(128);
            e.Property(x => x.Remarks).HasColumnName("remarks").HasMaxLength(2000);
            e.Property(x => x.WitnessApprovedAt).HasColumnName("witness_approved_at");
            e.Property(x => x.WitnessApprovedBy).HasColumnName("witness_approved_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
        });

        modelBuilder.Entity<InstrumentCheckAttachment>(e =>
        {
            e.ToTable("tbl_instrument_check_attachments");
            e.HasKey(x => x.AttachmentId);
            e.Property(x => x.AttachmentId).HasColumnName("attachment_id");
            e.Property(x => x.InstrumentCheckId).HasColumnName("instrument_check_id");
            e.Property(x => x.FilePath).HasColumnName("file_path").HasMaxLength(400);
            e.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(260);
            e.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(64);
            e.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes");
            e.Property(x => x.Sha256).HasColumnName("sha256");
            e.Property(x => x.CapturedAt).HasColumnName("captured_at").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.CapturedBy).HasColumnName("captured_by");
            e.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();
        });

        modelBuilder.Entity<CycleReceipt>(e =>
        {
            e.ToTable("cycle_receipts");
            e.HasKey(x => x.ReceiptId);
            e.Property(x => x.ReceiptId).HasColumnName("receipt_id");
            e.Property(x => x.SterilizationId).HasColumnName("sterilization_id");
            e.Property(x => x.FilePath).HasColumnName("file_path");
            e.Property(x => x.FileName).HasColumnName("file_name");
            e.Property(x => x.ContentType).HasColumnName("content_type");
            e.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes");
            e.Property(x => x.Sha256).HasColumnName("sha256");
            e.Property(x => x.CapturedAt).HasColumnName("captured_at");
        });

        modelBuilder.Entity<CycleReceiptAsset>(e =>
        {
            e.ToTable("cycle_receipt_assets");
            e.HasKey(x => x.AssetId);
            e.Property(x => x.AssetId).HasColumnName("asset_id");
            e.Property(x => x.ReceiptId).HasColumnName("receipt_id");
            e.Property(x => x.AssetKind).HasColumnName("asset_kind").HasMaxLength(16);
            e.Property(x => x.FilePath).HasColumnName("file_path").HasMaxLength(400);
            e.Property(x => x.WidthPx).HasColumnName("width_px");
            e.Property(x => x.HeightPx).HasColumnName("height_px");
            e.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes");
            e.Property(x => x.GeneratedAtUtc).HasColumnName("generated_at_utc");
            e.HasIndex(x => new { x.ReceiptId, x.AssetKind }).IsUnique().HasDatabaseName("UQ_cycle_receipt_assets_receipt_kind");
        });

        modelBuilder.Entity<CycleReceiptDerivationState>(e =>
        {
            e.ToTable("cycle_receipt_derivation_state");
            e.HasKey(x => x.ReceiptId);
            e.Property(x => x.ReceiptId).HasColumnName("receipt_id");
            e.Property(x => x.State).HasColumnName("state").HasMaxLength(16);
            e.Property(x => x.Attempts).HasColumnName("attempts");
            e.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(2000);
            e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<QaTest>(e =>
        {
            e.ToTable("qa_tests");
            e.HasKey(x => x.QaTestId);
            e.Property(x => x.QaTestId).HasColumnName("qa_test_id");
            e.Property(x => x.SterilizationId).HasColumnName("sterilization_id");
            e.Property(x => x.TestType).HasColumnName("test_type");
            e.Property(x => x.TestDateTime).HasColumnName("test_datetime");
            e.Property(x => x.Result).HasColumnName("result");
            e.Property(x => x.MeasuredValue).HasColumnName("measured_value").HasPrecision(10, 3);
            e.Property(x => x.Unit).HasColumnName("unit");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.PerformedBy).HasColumnName("performed_by");
            e.Property(x => x.ApprovedBy).HasColumnName("approved_by");
            e.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            e.Property(x => x.ApprovedRemarks).HasColumnName("approved_remarks").HasMaxLength(500);
            e.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();
        });

        modelBuilder.Entity<SterilizationQaRecord>(e =>
        {
            e.ToTable("qa_test_records");
            e.HasKey(x => x.RecordId);
            e.Property(x => x.RecordId).HasColumnName("record_id");
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(48);
            e.Property(x => x.SterilizationId).HasColumnName("sterilization_id");
            e.Property(x => x.SterilizerId).HasColumnName("sterilizer_id");
            e.Property(x => x.TestDateTimeUtc).HasColumnName("test_datetime_utc");
            e.Property(x => x.Department).HasColumnName("department").HasMaxLength(128);
            e.Property(x => x.Technician).HasColumnName("technician").HasMaxLength(128);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
            e.Property(x => x.ResultLabel).HasColumnName("result_label").HasMaxLength(32);
            e.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(240);
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
            e.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
            e.Property(x => x.ReviewedAtUtc).HasColumnName("reviewed_at_utc");
            e.Property(x => x.ApprovedBy).HasColumnName("approved_by");
            e.Property(x => x.ApprovedAtUtc).HasColumnName("approved_at_utc");
            e.Property(x => x.ArchivedAtUtc).HasColumnName("archived_at_utc");
            e.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();

            e.HasMany(x => x.StatusEvents)
                .WithOne()
                .HasForeignKey(x => x.RecordId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Attachments)
                .WithOne()
                .HasForeignKey(x => x.RecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SterilizationQaStatusEvent>(e =>
        {
            e.ToTable("qa_test_status_events");
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventId).HasColumnName("event_id");
            e.Property(x => x.RecordId).HasColumnName("record_id");
            e.Property(x => x.EventAtUtc).HasColumnName("event_at_utc").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.ActorAccountId).HasColumnName("actor_account_id");
            e.Property(x => x.FromStatus).HasColumnName("from_status").HasMaxLength(32);
            e.Property(x => x.ToStatus).HasColumnName("to_status").HasMaxLength(32);
            e.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(500);
        });

        modelBuilder.Entity<SterilizationQaAttachment>(e =>
        {
            e.ToTable("qa_test_attachments");
            e.HasKey(x => x.AttachmentId);
            e.Property(x => x.AttachmentId).HasColumnName("attachment_id");
            e.Property(x => x.RecordId).HasColumnName("record_id");
            e.Property(x => x.FilePath).HasColumnName("file_path").HasMaxLength(400);
            e.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(260);
            e.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(64);
            e.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes");
            e.Property(x => x.Sha256).HasColumnName("sha256");
            e.Property(x => x.CapturedAtUtc).HasColumnName("captured_at_utc").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.CapturedBy).HasColumnName("captured_by");
            e.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();
            e.HasIndex(x => new { x.RecordId, x.FileName }).HasDatabaseName("IX_qa_test_attachments_record_file");
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(x => x.AuditId);
            e.Property(x => x.AuditId).HasColumnName("audit_id");
            e.Property(x => x.EventAt).HasColumnName("event_at");
            e.Property(x => x.ActorAccountId).HasColumnName("actor_account_id");
            e.Property(x => x.Module).HasColumnName("module");
            e.Property(x => x.EntityName).HasColumnName("entity_name");
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.Action).HasColumnName("action").HasMaxLength(96);
            e.Property(x => x.OldValuesJson).HasColumnName("old_values_json");
            e.Property(x => x.NewValuesJson).HasColumnName("new_values_json");
            e.Property(x => x.ClientMachine).HasColumnName("client_machine");
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        });

        modelBuilder.Entity<PrintLog>(e =>
        {
            e.ToTable("print_logs");
            e.HasKey(x => x.PrintLogId);
            e.Property(x => x.PrintLogId).HasColumnName("print_log_id");
            e.Property(x => x.PrintedAt).HasColumnName("printed_at");
            e.Property(x => x.PrintedBy).HasColumnName("printed_by");
            e.Property(x => x.ReportType).HasColumnName("report_type");
            e.Property(x => x.SterilizationId).HasColumnName("sterilization_id");
            e.Property(x => x.QaTestId).HasColumnName("qa_test_id");
            e.Property(x => x.PrinterName).HasColumnName("printer_name");
            e.Property(x => x.Copies).HasColumnName("copies");
            e.Property(x => x.ParametersJson).HasColumnName("parameters_json");
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id");
            e.Property(x => x.ReportVersion).HasColumnName("report_version").HasMaxLength(32);
            e.Property(x => x.StationId).HasColumnName("station_id").HasMaxLength(64);
            // Enforced by migration 017_hsms_print_log_idempotency.sql; reflected here so EF model-checking is consistent.
            e.HasIndex(x => x.CorrelationId).IsUnique().HasDatabaseName("UQ_print_logs_correlation_id");
        });

        modelBuilder.Entity<AnalyticsPreset>(e =>
        {
            e.ToTable("analytics_presets");
            e.HasKey(x => x.PresetId);
            e.Property(x => x.PresetId).HasColumnName("preset_id");
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128);
            e.Property(x => x.IsDefault).HasColumnName("is_default");
            e.Property(x => x.PresetJson).HasColumnName("preset_json");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => new { x.AccountId, x.Name }).IsUnique().HasDatabaseName("UQ_analytics_presets_account_name");
            e.HasIndex(x => new { x.AccountId, x.IsDefault }).HasDatabaseName("IX_analytics_presets_default");
        });

        modelBuilder.Entity<SterilizationQaPreset>(e =>
        {
            e.ToTable("qa_test_presets");
            e.HasKey(x => x.PresetId);
            e.Property(x => x.PresetId).HasColumnName("preset_id");
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128);
            e.Property(x => x.IsDefault).HasColumnName("is_default");
            e.Property(x => x.PresetJson).HasColumnName("preset_json");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => new { x.AccountId, x.Name }).IsUnique().HasDatabaseName("UQ_qa_test_presets_account_name");
            e.HasIndex(x => new { x.AccountId, x.IsDefault }).HasDatabaseName("IX_qa_test_presets_default");
        });
    }
}
