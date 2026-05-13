using HSMS.Application.Reporting.Datasets;
using HSMS.Persistence.Data;
using HSMS.Shared.Contracts.Reporting;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Reporting.Builders;

/// <summary>
/// Loads a single sterilization cycle + items + sterilizer label + receipt assets into a LoadRecord dataset.
/// Receipt-image bytes are populated by the file storage layer (see <see cref="IReceiptImageProvider"/>).
/// </summary>
public sealed class LoadRecordDatasetBuilder(IReceiptImageProvider receiptImageProvider)
    : IReportDatasetBuilder<LoadRecordReportData>
{
    public string ReportType => Shared.Contracts.Reporting.ReportType.LoadRecord;

    public async Task<(LoadRecordReportData? data, ReportValidationResult validation)> BuildAsync(
        HsmsDbContext db,
        ReportRenderRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = new ReportValidationResult();
        if (request.SterilizationId is not int cycleId)
        {
            validation.AddError(ReportValidationCodes.CycleNotFound, "Sterilization cycle id is required.");
            return (null, validation);
        }

        var cycle = await db.Sterilizations
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Receipts)
            .SingleOrDefaultAsync(x => x.SterilizationId == cycleId, cancellationToken);

        if (cycle is null)
        {
            validation.AddError(ReportValidationCodes.CycleNotFound, $"Cycle #{cycleId} not found.");
            return (null, validation);
        }

        if (cycle.Items.Count == 0)
        {
            validation.AddWarning(ReportValidationCodes.CycleNoItems,
                "This cycle has no line items; the load record will print with an empty item table.");
        }

        if (!string.Equals(cycle.CycleStatus, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            validation.AddWarning(ReportValidationCodes.CycleNotCompleted,
                $"Cycle status is \"{cycle.CycleStatus}\". You can still print, but consider completing the cycle first.");
        }

        var sterilizer = await db.SterilizerUnits.AsNoTracking()
            .Where(x => x.SterilizerId == cycle.SterilizerId)
            .Select(x => new { x.SterilizerNumber, x.Model })
            .SingleOrDefaultAsync(cancellationToken);

        var doctorRoom = cycle.DoctorRoomId is int drId
            ? await db.DoctorRooms.AsNoTracking()
                .Where(x => x.DoctorRoomId == drId)
                .Select(x => new { x.DoctorName, x.Room })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var data = new LoadRecordReportData
        {
            Header = new LoadRecordHeader
            {
                CycleNo = cycle.CycleNo,
                SterilizerNo = sterilizer?.SterilizerNumber ?? cycle.SterilizerId.ToString(),
                SterilizerModel = sterilizer?.Model ?? string.Empty,
                SterilizationType = cycle.SterilizationType,
                CycleProgram = cycle.CycleProgram ?? string.Empty,
                CycleDateTimeUtc = cycle.CycleDateTime,
                CycleTimeInUtc = cycle.CycleTimeIn,
                CycleTimeOutUtc = cycle.CycleTimeOut,
                OperatorName = cycle.OperatorName,
                TemperatureC = cycle.TemperatureC,
                Pressure = cycle.Pressure,
                ExposureTimeMinutes = cycle.ExposureTimeMinutes,
                BiLotNo = cycle.BiLotNo,
                BiResult = cycle.BiResult,
                CycleStatus = cycle.CycleStatus,
                DoctorOrRoom = doctorRoom is null
                    ? null
                    : string.IsNullOrWhiteSpace(doctorRoom.Room) ? doctorRoom.DoctorName : $"{doctorRoom.DoctorName} / {doctorRoom.Room}",
                Implants = cycle.Implants,
                Notes = cycle.Notes,
                TotalPcs = cycle.Items.Sum(i => i.Pcs),
                TotalQty = cycle.Items.Sum(i => i.Qty)
            },
            Items = [.. cycle.Items
                .OrderBy(i => i.SterilizationItemId)
                .Select((i, idx) => new LoadRecordItemRow
                {
                    LineNo = idx + 1,
                    DepartmentName = i.DepartmentName ?? string.Empty,
                    DoctorOrRoom = i.DoctorOrRoom ?? string.Empty,
                    ItemName = i.ItemName,
                    Pcs = i.Pcs,
                    Qty = i.Qty
                })]
        };

        if (request.IncludeReceiptImages)
        {
            if (cycle.Receipts.Count == 0)
            {
                validation.AddWarning(ReportValidationCodes.MissingReceipt,
                    "No receipts attached. Page 2 will print a blank receipts panel.");
            }
            else
            {
                foreach (var receipt in cycle.Receipts.OrderBy(r => r.CapturedAt))
                {
                    var bytes = await receiptImageProvider.LoadReceiptImageAsync(receipt.ReceiptId, cancellationToken);
                    if (bytes is null || bytes.Length == 0)
                    {
                        validation.AddWarning(ReportValidationCodes.ReceiptUnsupportedFormat,
                            $"Receipt {receipt.FileName} could not be rendered as an image and will be skipped.");
                        continue;
                    }

                    data.ReceiptImages.Add(new LoadRecordReceiptImage
                    {
                        ReceiptId = receipt.ReceiptId,
                        FileName = receipt.FileName,
                        ContentType = receipt.ContentType,
                        CapturedAtUtc = receipt.CapturedAt,
                        ImageBytes = bytes
                    });
                }
            }
        }

        return (data, validation);
    }
}

/// <summary>
/// Loads receipt image bytes (original PNG/JPG, or generated PNG preview for PDFs).
/// Provided by the host (API has a real implementation reading from disk + derivation cache; desktop in-process uses the same).
/// </summary>
public interface IReceiptImageProvider
{
    Task<byte[]?> LoadReceiptImageAsync(int receiptId, CancellationToken cancellationToken);
}
