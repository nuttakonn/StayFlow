using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.ThaiCompliance;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record CreateComplianceRecordRequest(
    Guid ReservationId,
    Guid GuestId,
    string GuestFullName,
    string? NationalId,
    string? PassportNumber,
    string? Nationality,
    string? DateOfBirth,          // yyyy-MM-dd
    string? VisaType,
    string? CountryOfOrigin,
    string? RoomNumber);

public record AmendComplianceRecordRequest(
    string Reason,
    string? NewNationalId,
    string? NewPassportNumber,
    string? NewNationality,
    string? NewDateOfBirth,
    string? NewVisaType,
    string? NewCountryOfOrigin);

public record ComplianceRecordDto(
    Guid Id,
    Guid PropertyId,
    Guid ReservationId,
    Guid GuestId,
    string GuestFullName,
    string? NationalId,
    string? PassportNumber,
    string? Nationality,
    string? DateOfBirth,
    string? VisaType,
    string? CountryOfOrigin,
    bool IsForeigner,
    string? RoomNumber,
    string CheckInDate,
    string CheckOutDate,
    int Adults,
    bool IsAmended,
    int AmendmentCount,
    DateTimeOffset RecordedAtUtc);

public record EnableThaiComplianceRequest(bool Enable);

// ── Endpoints ─────────────────────────────────────────────────────────────────

public static class ThaiComplianceEndpoints
{
    public static void MapThaiComplianceEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/properties/{propertyId:guid}/thai-compliance")
            .WithTags("Thai Compliance")
            .RequireAuthorization();

        // Configuration
        auth.MapPost("/enable", EnableThaiComplianceAsync);

        // Record management
        auth.MapGet("/records", ListRecordsAsync);
        auth.MapPost("/records", CreateRecordAsync);
        auth.MapGet("/records/{id:guid}", GetRecordAsync);
        auth.MapPost("/records/{id:guid}/amend", AmendRecordAsync);

        // Government report exports (always Thai language layout)
        auth.MapGet("/rs3/export", ExportRs3Async);    // R.S.3 — registration card per guest
        auth.MapGet("/rs4/export", ExportRs4Async);    // R.S.4 — registration summary
        auth.MapGet("/tm30/export", ExportTm30Async);  // TM.30 — foreign guest report
    }

    private static async Task<IResult> EnableThaiComplianceAsync(
        Guid propertyId,
        [FromBody] EnableThaiComplianceRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var property = await db.Properties.FindAsync([propertyId]);
        if (property == null) return Results.NotFound();
        property.SetThaiComplianceMode(req.Enable);
        await db.SaveChangesAsync();
        return Results.Ok(new { propertyId, enableThaiCompliance = req.Enable });
    }

    private static async Task<IResult> ListRecordsAsync(
        Guid propertyId,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        [FromQuery] bool? foreignersOnly,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var property = await db.Properties.FindAsync([propertyId]);
        if (property == null) return Results.NotFound();
        if (!property.EnableThaiCompliance)
            return Results.Problem("Thai compliance mode is not enabled for this property.", statusCode: 403);

        var query = db.ThaiComplianceRecords.Include(r => r.Amendments)
            .Where(r => r.PropertyId == propertyId);

        if (DateOnly.TryParse(startDate, out var start))
            query = query.Where(r => r.CheckInDate >= start);
        if (DateOnly.TryParse(endDate, out var end))
            query = query.Where(r => r.CheckInDate <= end);
        if (foreignersOnly == true)
            query = query.Where(r => r.IsForeigner);

        var records = await query.OrderBy(r => r.CheckInDate).ThenBy(r => r.GuestFullName).ToListAsync();
        return Results.Ok(new { data = records.Select(ToDto), total = records.Count });
    }

    private static async Task<IResult> CreateRecordAsync(
        Guid propertyId,
        [FromBody] CreateComplianceRecordRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();

        var property = await db.Properties.FindAsync([propertyId]);
        if (property == null) return Results.NotFound();
        if (!property.EnableThaiCompliance)
            return Results.Problem("Thai compliance mode is not enabled for this property.", statusCode: 403);

        // One record per reservation
        var exists = await db.ThaiComplianceRecords.AnyAsync(r =>
            r.PropertyId == propertyId && r.ReservationId == req.ReservationId);
        if (exists)
            return Results.Problem("A compliance record already exists for this reservation.", statusCode: 409);

        // Validate: Thai guests need NationalId, foreigners need PassportNumber
        var hasThai = !string.IsNullOrWhiteSpace(req.NationalId);
        var hasForeign = !string.IsNullOrWhiteSpace(req.PassportNumber);
        if (!hasThai && !hasForeign)
            return Results.Problem(
                "Either NationalId (Thai citizen) or PassportNumber (foreign guest) is required.",
                statusCode: 400);

        DateOnly? dob = string.IsNullOrWhiteSpace(req.DateOfBirth) ? null
            : DateOnly.TryParse(req.DateOfBirth, out var parsedDob) ? parsedDob : null;

        // Fetch reservation for stay dates
        var reservation = await db.Reservations.FindAsync([req.ReservationId]);
        if (reservation == null || reservation.PropertyId != propertyId)
            return Results.Problem("Reservation not found in this property.", statusCode: 404);

        var record = ThaiComplianceRecord.Create(
            tenant.OrganizationId.Value, propertyId,
            req.ReservationId, req.GuestId,
            req.GuestFullName, req.NationalId, req.PassportNumber,
            req.Nationality, dob, req.VisaType, req.CountryOfOrigin,
            req.RoomNumber ?? reservation.RoomId.ToString(),
            reservation.CheckInDate, reservation.CheckOutDate, reservation.Adults);

        db.ThaiComplianceRecords.Add(record);
        await db.SaveChangesAsync();
        return Results.Created($"/api/v1/properties/{propertyId}/thai-compliance/records/{record.Id}", ToDto(record));
    }

    private static async Task<IResult> GetRecordAsync(
        Guid propertyId, Guid id, StayFlowDbContext db, ITenantContext tenant)
    {
        var record = await db.ThaiComplianceRecords.Include(r => r.Amendments)
            .FirstOrDefaultAsync(r => r.Id == id && r.PropertyId == propertyId);
        return record == null ? Results.NotFound() : Results.Ok(ToDto(record));
    }

    private static async Task<IResult> AmendRecordAsync(
        Guid propertyId, Guid id,
        [FromBody] AmendComplianceRecordRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();

        var record = await db.ThaiComplianceRecords.Include(r => r.Amendments)
            .FirstOrDefaultAsync(r => r.Id == id && r.PropertyId == propertyId);
        if (record == null) return Results.NotFound();

        if (string.IsNullOrWhiteSpace(req.Reason))
            return Results.Problem("Amendment reason is required.", statusCode: 400);

        // Resolve the amending user's email from the token (use a placeholder if not available)
        var userEmail = tenant.UserEmail ?? "system@stayflow";

        DateOnly? newDob = string.IsNullOrWhiteSpace(req.NewDateOfBirth) ? null
            : DateOnly.TryParse(req.NewDateOfBirth, out var parsedDob) ? parsedDob : null;

        var amendment = record.Amend(req.Reason, userEmail,
            req.NewNationalId, req.NewPassportNumber,
            req.NewNationality, newDob,
            req.NewVisaType, req.NewCountryOfOrigin);

        db.ThaiComplianceAmendments.Add(amendment);
        await db.SaveChangesAsync();
        return Results.Ok(ToDto(record));
    }

    // ── R.S.3 — Guest registration card (one sheet per guest per stay) ────────

    private static async Task<IResult> ExportRs3Async(
        Guid propertyId,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var (records, property) = await LoadRecordsForExport(propertyId, startDate, endDate, db);
        if (property == null) return Results.NotFound();

        using var wb = new XLWorkbook();

        foreach (var r in records)
        {
            var sheetName = $"{r.RoomNumber ?? "?"}_{r.CheckInDate:yyyyMMdd}"[..Math.Min(31,
                ($"{r.RoomNumber ?? "?"}_${r.CheckInDate:yyyyMMdd}").Length)];
            // Ensure unique sheet name
            var safeName = $"{r.GuestFullName[..Math.Min(15, r.GuestFullName.Length)]}_{r.CheckInDate:MMdd}";
            var ws = wb.Worksheets.Add(safeName[..Math.Min(31, safeName.Length)]);

            WriteRs3Sheet(ws, r, property.Name);
        }

        if (!wb.Worksheets.Any()) wb.Worksheets.Add("ไม่มีข้อมูล");

        return ExcelResult(wb, $"RS3_{propertyId}_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    private static void WriteRs3Sheet(IXLWorksheet ws, ThaiComplianceRecord r, string propertyName)
    {
        // R.S.3 layout (Thai government hotel guest registration card)
        ws.Cell("A1").Value = "แบบ ร.ส.3";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Cell("A2").Value = "ทะเบียนรับรองการเดินทางเข้าพักในโรงแรม";
        ws.Cell("A3").Value = $"โรงแรม: {propertyName}";

        ws.Cell("A5").Value = "ชื่อ-นามสกุล";
        ws.Cell("B5").Value = r.GuestFullName;
        ws.Cell("A6").Value = "สัญชาติ";
        ws.Cell("B6").Value = r.Nationality ?? "-";
        ws.Cell("A7").Value = "เลขบัตรประชาชน";
        ws.Cell("B7").Value = r.NationalId ?? "-";
        ws.Cell("A8").Value = "เลขหนังสือเดินทาง";
        ws.Cell("B8").Value = r.PassportNumber ?? "-";
        ws.Cell("A9").Value = "วันเกิด";
        ws.Cell("B9").Value = r.DateOfBirth.HasValue ? r.DateOfBirth.Value.ToString("dd/MM/yyyy") : "-";
        ws.Cell("A10").Value = "ประเภทวีซ่า";
        ws.Cell("B10").Value = r.VisaType ?? "-";
        ws.Cell("A11").Value = "ประเทศที่มา";
        ws.Cell("B11").Value = r.CountryOfOrigin ?? "-";
        ws.Cell("A13").Value = "เลขที่ห้องพัก";
        ws.Cell("B13").Value = r.RoomNumber ?? "-";
        ws.Cell("A14").Value = "วันเข้าพัก";
        ws.Cell("B14").Value = r.CheckInDate.ToString("dd/MM/yyyy");
        ws.Cell("A15").Value = "วันออก";
        ws.Cell("B15").Value = r.CheckOutDate.ToString("dd/MM/yyyy");
        ws.Cell("A16").Value = "จำนวนผู้เข้าพัก";
        ws.Cell("B16").Value = r.Adults;

        if (r.IsAmended)
        {
            ws.Cell("A18").Value = "* บันทึกนี้มีการแก้ไข — ดูรายละเอียดการแก้ไขในระบบ";
            ws.Cell("A18").Style.Font.FontColor = XLColor.Red;
        }

        ws.Columns().AdjustToContents();
    }

    // ── R.S.4 — Registration summary (tabular, one row per stay) ─────────────

    private static async Task<IResult> ExportRs4Async(
        Guid propertyId,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var (records, property) = await LoadRecordsForExport(propertyId, startDate, endDate, db);
        if (property == null) return Results.NotFound();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ร.ส.4");

        // Header
        ws.Cell("A1").Value = "แบบ ร.ส.4 — ทะเบียนรับรองการเดินทางเข้าพักในโรงแรม (สรุป)";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A2").Value = $"โรงแรม: {property.Name}";
        if (DateOnly.TryParse(startDate, out var s) && DateOnly.TryParse(endDate, out var e))
            ws.Cell("A3").Value = $"ช่วงวันที่: {s:dd/MM/yyyy} ถึง {e:dd/MM/yyyy}";

        int col = 1;
        string[] headers = [
            "ลำดับ", "ชื่อ-นามสกุล", "สัญชาติ", "เลขบัตรประชาชน", "เลขหนังสือเดินทาง",
            "วันเกิด", "ประเภทวีซ่า", "ประเทศที่มา", "เลขห้อง", "วันเข้าพัก", "วันออก", "จำนวนคน"
        ];
        int headerRow = 5;
        foreach (var h in headers)
        {
            ws.Cell(headerRow, col).Value = h;
            ws.Cell(headerRow, col).Style.Font.Bold = true;
            ws.Cell(headerRow, col).Style.Fill.BackgroundColor = XLColor.LightBlue;
            col++;
        }

        int row = headerRow + 1;
        int seq = 1;
        foreach (var r in records)
        {
            ws.Cell(row, 1).Value = seq++;
            ws.Cell(row, 2).Value = r.GuestFullName;
            ws.Cell(row, 3).Value = r.Nationality ?? "-";
            ws.Cell(row, 4).Value = r.NationalId ?? "-";
            ws.Cell(row, 5).Value = r.PassportNumber ?? "-";
            ws.Cell(row, 6).Value = r.DateOfBirth.HasValue ? r.DateOfBirth.Value.ToString("dd/MM/yyyy") : "-";
            ws.Cell(row, 7).Value = r.VisaType ?? "-";
            ws.Cell(row, 8).Value = r.CountryOfOrigin ?? "-";
            ws.Cell(row, 9).Value = r.RoomNumber ?? "-";
            ws.Cell(row, 10).Value = r.CheckInDate.ToString("dd/MM/yyyy");
            ws.Cell(row, 11).Value = r.CheckOutDate.ToString("dd/MM/yyyy");
            ws.Cell(row, 12).Value = r.Adults;
            row++;
        }

        ws.Cell(row, 1).Value = $"รวมทั้งหมด: {records.Count} คน";
        ws.Row(row).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        return ExcelResult(wb, $"RS4_{propertyId}_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    // ── TM.30 — Foreign guest immigration report ──────────────────────────────

    private static async Task<IResult> ExportTm30Async(
        Guid propertyId,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var (allRecords, property) = await LoadRecordsForExport(propertyId, startDate, endDate, db);
        if (property == null) return Results.NotFound();

        // TM.30 is foreign guests only
        var records = allRecords.Where(r => r.IsForeigner).ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("TM.30");

        ws.Cell("A1").Value = "แบบ ตม.30 — แจ้งที่พักคนต่างด้าว";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A2").Value = $"โรงแรม/สถานที่พัก: {property.Name}";
        if (DateOnly.TryParse(startDate, out var s) && DateOnly.TryParse(endDate, out var e))
            ws.Cell("A3").Value = $"ช่วงวันที่: {s:dd/MM/yyyy} ถึง {e:dd/MM/yyyy}";
        ws.Cell("A4").Value = $"วันที่จัดทำรายงาน: {DateTime.Now:dd/MM/yyyy}";

        string[] headers = [
            "ลำดับ", "ชื่อ-นามสกุล", "สัญชาติ", "เลขหนังสือเดินทาง",
            "วันเกิด", "ประเภทวีซ่า", "ประเทศที่มา",
            "เลขห้อง", "วันเข้าพัก", "วันออก"
        ];
        int headerRow = 6;
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cell(headerRow, c + 1).Value = headers[c];
            ws.Cell(headerRow, c + 1).Style.Font.Bold = true;
            ws.Cell(headerRow, c + 1).Style.Fill.BackgroundColor = XLColor.LightYellow;
        }

        int row = headerRow + 1;
        int seq = 1;
        foreach (var r in records)
        {
            ws.Cell(row, 1).Value = seq++;
            ws.Cell(row, 2).Value = r.GuestFullName;
            ws.Cell(row, 3).Value = r.Nationality ?? "-";
            ws.Cell(row, 4).Value = r.PassportNumber ?? "-";
            ws.Cell(row, 5).Value = r.DateOfBirth.HasValue ? r.DateOfBirth.Value.ToString("dd/MM/yyyy") : "-";
            ws.Cell(row, 6).Value = r.VisaType ?? "-";
            ws.Cell(row, 7).Value = r.CountryOfOrigin ?? "-";
            ws.Cell(row, 8).Value = r.RoomNumber ?? "-";
            ws.Cell(row, 9).Value = r.CheckInDate.ToString("dd/MM/yyyy");
            ws.Cell(row, 10).Value = r.CheckOutDate.ToString("dd/MM/yyyy");
            row++;
        }

        ws.Cell(row, 1).Value = $"รวมนักท่องเที่ยวต่างชาติ: {records.Count} คน";
        ws.Row(row).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        return ExcelResult(wb, $"TM30_{propertyId}_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<(List<ThaiComplianceRecord> records, StayFlow.BuildingBlocks.Domain.Properties.Property? property)>
        LoadRecordsForExport(Guid propertyId, string? startDate, string? endDate, StayFlowDbContext db)
    {
        var property = await db.Properties.FindAsync([propertyId]);
        if (property == null) return ([], null);

        var query = db.ThaiComplianceRecords.Include(r => r.Amendments)
            .Where(r => r.PropertyId == propertyId);

        if (DateOnly.TryParse(startDate, out var s))
            query = query.Where(r => r.CheckInDate >= s);
        if (DateOnly.TryParse(endDate, out var e))
            query = query.Where(r => r.CheckInDate <= e);

        var records = await query.OrderBy(r => r.CheckInDate).ThenBy(r => r.GuestFullName).ToListAsync();
        return (records, property);
    }

    private static IResult ExcelResult(XLWorkbook wb, string filename)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Results.File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            filename);
    }

    private static ComplianceRecordDto ToDto(ThaiComplianceRecord r) =>
        new(r.Id, r.PropertyId, r.ReservationId, r.GuestId,
            r.GuestFullName, r.NationalId, r.PassportNumber,
            r.Nationality, r.DateOfBirth?.ToString("yyyy-MM-dd"),
            r.VisaType, r.CountryOfOrigin, r.IsForeigner,
            r.RoomNumber,
            r.CheckInDate.ToString("yyyy-MM-dd"),
            r.CheckOutDate.ToString("yyyy-MM-dd"),
            r.Adults, r.IsAmended, r.Amendments.Count, r.RecordedAtUtc);
}
