using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.BookingEngine;
using StayFlow.BuildingBlocks.Domain.Parties;
using StayFlow.BuildingBlocks.Domain.Rates;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.Email;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record PublicRoomTypeDto(
    Guid RoomTypeId,
    string Name,
    string? Description,
    int MaxOccupancy,
    decimal RatePerNight,
    decimal TotalRate,
    int AvailableCount,
    string? AmenitiesJson);

public record PublicAvailabilityResponse(
    Guid PropertyId,
    string PropertyName,
    string CheckInDate,
    string CheckOutDate,
    int Nights,
    string Currency,
    string BookingPolicyText,
    IReadOnlyList<PublicRoomTypeDto> RoomTypes);

public record PublicValidateCouponRequest(
    string CouponCode,
    decimal BaseAmount,
    string CheckInDate,
    string CheckOutDate,
    Guid? RoomTypeId);

public record PublicValidateCouponResponse(
    bool IsValid,
    string? DiscountType,
    decimal? Value,
    decimal? DiscountAmount,
    string? Message);

public record CreateHoldRequest(
    Guid RoomTypeId,
    string CheckInDate,
    string CheckOutDate,
    int Adults,
    int Children,
    string? GuestEmail);

public record CreateHoldResponse(
    Guid HoldToken,
    DateTimeOffset ExpiresAt,
    string RoomTypeName,
    string CheckInDate,
    string CheckOutDate,
    int Nights,
    decimal TotalAmount,
    string Currency);

public record CreatePublicReservationRequest(
    Guid HoldToken,
    string GuestName,
    string GuestEmail,
    string? GuestPhone,
    string? GuestNationality,
    string? GuestNationalId,
    string? GuestPassportNumber,
    string? CouponCode,
    string? SpecialRequests,
    bool PolicyAcknowledged);

public record PublicReservationDto(
    Guid ReservationId,
    string ReservationRef,
    string GuestName,
    string PropertyName,
    string CheckInDate,
    string CheckOutDate,
    int Nights,
    decimal TotalAmount,
    string Currency,
    string Status);

// ── Endpoint Registration ────────────────────────────────────────────────────

public static class BookingEnginePublicEndpoints
{
    public static void MapBookingEnginePublicEndpoints(this IEndpointRouteBuilder app)
    {
        // Public booking group — no auth required; rate-limited via "booking" policy
        var group = app.MapGroup("/api/v1/book/{propId:guid}")
            .WithTags("Public Booking Engine")
            .RequireRateLimiting("booking");

        // GET /api/v1/book/{propId}/availability
        group.MapGet("/availability", async (
            Guid propId,
            string checkIn,
            string checkOut,
            int adults,
            int children,
            StayFlowDbContext db) =>
        {
            if (!DateOnly.TryParse(checkIn, CultureInfo.InvariantCulture, out var checkInDate) ||
                !DateOnly.TryParse(checkOut, CultureInfo.InvariantCulture, out var checkOutDate) ||
                checkOutDate <= checkInDate)
                return Results.BadRequest("Invalid checkIn / checkOut dates.");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (checkInDate < today)
                return Results.BadRequest("Check-in date cannot be in the past.");

            if (adults < 1) return Results.BadRequest("At least one adult is required.");

            // Load property (bypass tenant filter — this is a public endpoint)
            var property = await db.Properties
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == propId && p.IsActive);
            if (property == null) return Results.NotFound("Property not found.");

            // Load booking engine settings
            var settings = await db.BookingEngineSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.PropertyId == propId);

            if (settings is not { IsEnabled: true })
                return Results.Problem("Online booking is not available for this property.",
                    statusCode: 400, title: "Booking Engine Disabled");

            // Advance booking window validation
            var daysUntilCheckIn = checkInDate.DayNumber - today.DayNumber;
            if (settings.MinAdvanceBookingDays > 0 && daysUntilCheckIn < settings.MinAdvanceBookingDays)
                return Results.Problem(
                    $"Bookings must be made at least {settings.MinAdvanceBookingDays} day(s) in advance.",
                    statusCode: 400, title: "Too Soon");
            if (settings.MaxAdvanceBookingDays > 0 && daysUntilCheckIn > settings.MaxAdvanceBookingDays)
                return Results.Problem(
                    $"Bookings cannot be made more than {settings.MaxAdvanceBookingDays} day(s) in advance.",
                    statusCode: 400, title: "Too Far Ahead");

            var nights = checkOutDate.DayNumber - checkInDate.DayNumber;

            // Get all active room types for the property
            var roomTypes = await db.RoomTypes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(rt => rt.PropertyId == propId && rt.IsActive)
                .ToListAsync();

            // For each room type, find the cheapest active public rate plan
            var ratePlans = await db.RatePlans
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.PropertyId == propId && r.IsActive && r.IsPublic)
                .ToListAsync();

            // Count active rooms per room type
            var totalRoomCounts = await db.Rooms
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.PropertyId == propId && r.IsActive)
                .GroupBy(r => r.RoomTypeId)
                .Select(g => new { RoomTypeId = g.Key, Count = g.Count() })
                .ToListAsync();

            // Count active reservations per room type for the date range
            var activeSatuses = new[]
            {
                (int)ReservationStatus.Pending,
                (int)ReservationStatus.Confirmed,
                (int)ReservationStatus.CheckedIn
            };
            var reservedCounts = await db.Reservations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.PropertyId == propId &&
                            activeSatuses.Contains((int)r.Status) &&
                            r.CheckInDate < checkOutDate && r.CheckOutDate > checkInDate)
                .GroupBy(r => r.RoomTypeId)
                .Select(g => new { RoomTypeId = g.Key, Count = g.Count() })
                .ToListAsync();

            // Count active holds per room type for the date range
            var now = DateTimeOffset.UtcNow;
            var heldCounts = await db.BookingHolds
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(h => h.PropertyId == propId &&
                            h.Status == HoldStatus.Active &&
                            h.ExpiresAtUtc > now &&
                            h.CheckInDate < checkOutDate && h.CheckOutDate > checkInDate)
                .GroupBy(h => h.RoomTypeId)
                .Select(g => new { RoomTypeId = g.Key, Count = g.Count() })
                .ToListAsync();

            var result = roomTypes.Select(rt =>
            {
                var publicRate = ratePlans
                    .Where(rp => rp.IsPublic && rp.IsActive &&
                                 (rp.RoomTypeId == null || rp.RoomTypeId == rt.Id))
                    .OrderBy(rp => rp.BaseRate)
                    .FirstOrDefault();

                var ratePerNight = publicRate?.BaseRate ?? rt.BaseRate;
                var total = ratePerNight * nights;

                var totalRooms = totalRoomCounts.FirstOrDefault(x => x.RoomTypeId == rt.Id)?.Count ?? 0;
                var reserved = reservedCounts.FirstOrDefault(x => x.RoomTypeId == rt.Id)?.Count ?? 0;
                var held = heldCounts.FirstOrDefault(x => x.RoomTypeId == rt.Id)?.Count ?? 0;
                var available = Math.Max(0, totalRooms - reserved - held);

                return new PublicRoomTypeDto(
                    rt.Id, rt.Name, rt.Description, rt.MaxOccupancy,
                    ratePerNight, total, available, rt.AmenitiesJson);
            }).ToList();

            return Results.Ok(new PublicAvailabilityResponse(
                propId, property.Name,
                checkInDate.ToString("yyyy-MM-dd"), checkOutDate.ToString("yyyy-MM-dd"),
                nights, property.DefaultCurrency,
                settings.BookingPolicyText, result));
        }).WithName("GetPublicAvailability");

        // POST /api/v1/book/{propId}/validate-coupon
        group.MapPost("/validate-coupon", async (
            Guid propId,
            PublicValidateCouponRequest req,
            StayFlowDbContext db) =>
        {
            var property = await db.Properties
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == propId && p.IsActive);
            if (property == null) return Results.NotFound("Property not found.");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var coupon = await db.Coupons
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.PropertyId == propId &&
                    c.Code == req.CouponCode.Trim().ToUpperInvariant());

            if (coupon == null)
                return Results.Ok(new PublicValidateCouponResponse(false, null, null, null, "Coupon code not found."));

            var error = coupon.InvalidReason(today);
            if (error != null)
                return Results.Ok(new PublicValidateCouponResponse(false, null, null, null, error));

            var discount = coupon.CalculateDiscount(req.BaseAmount);
            return Results.Ok(new PublicValidateCouponResponse(
                true,
                coupon.DiscountType.ToString(),
                coupon.Value,
                discount,
                null));
        }).WithName("ValidatePublicCoupon");

        // POST /api/v1/book/{propId}/hold
        group.MapPost("/hold", async (
            Guid propId,
            CreateHoldRequest req,
            StayFlowDbContext db) =>
        {
            if (!DateOnly.TryParse(req.CheckInDate, CultureInfo.InvariantCulture, out var checkInDate) ||
                !DateOnly.TryParse(req.CheckOutDate, CultureInfo.InvariantCulture, out var checkOutDate) ||
                checkOutDate <= checkInDate)
                return Results.BadRequest("Invalid check-in / check-out dates.");

            if (req.Adults < 1) return Results.BadRequest("At least one adult is required.");

            var property = await db.Properties
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == propId && p.IsActive);
            if (property == null) return Results.NotFound("Property not found.");

            var settings = await db.BookingEngineSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.PropertyId == propId);
            if (settings is not { IsEnabled: true })
                return Results.Problem("Online booking is not available for this property.",
                    statusCode: 400, title: "Booking Engine Disabled");

            // Validate room type belongs to property
            var roomType = await db.RoomTypes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(rt => rt.Id == req.RoomTypeId && rt.PropertyId == propId && rt.IsActive);
            if (roomType == null) return Results.NotFound("Room type not found.");

            // Check availability
            var activeSatuses = new[]
            {
                (int)ReservationStatus.Pending,
                (int)ReservationStatus.Confirmed,
                (int)ReservationStatus.CheckedIn
            };
            var totalRooms = await db.Rooms
                .IgnoreQueryFilters()
                .CountAsync(r => r.PropertyId == propId && r.RoomTypeId == req.RoomTypeId && r.IsActive);
            var reserved = await db.Reservations
                .IgnoreQueryFilters()
                .CountAsync(r =>
                    r.PropertyId == propId && r.RoomTypeId == req.RoomTypeId &&
                    activeSatuses.Contains((int)r.Status) &&
                    r.CheckInDate < checkOutDate && r.CheckOutDate > checkInDate);
            var now = DateTimeOffset.UtcNow;
            var held = await db.BookingHolds
                .IgnoreQueryFilters()
                .CountAsync(h =>
                    h.PropertyId == propId && h.RoomTypeId == req.RoomTypeId &&
                    h.Status == HoldStatus.Active && h.ExpiresAtUtc > now &&
                    h.CheckInDate < checkOutDate && h.CheckOutDate > checkInDate);

            if (totalRooms - reserved - held <= 0)
                return Results.Conflict("No rooms available for the selected dates.");

            var hold = BookingHold.Create(
                property.OrganizationId, propId,
                req.RoomTypeId, checkInDate, checkOutDate,
                req.Adults, req.Children,
                settings.HoldDurationMinutes, req.GuestEmail);

            db.BookingHolds.Add(hold);
            await db.SaveChangesAsync();

            var nights = checkOutDate.DayNumber - checkInDate.DayNumber;
            var ratePlans = await db.RatePlans
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(rp => rp.PropertyId == propId && rp.IsPublic && rp.IsActive &&
                             (rp.RoomTypeId == null || rp.RoomTypeId == req.RoomTypeId))
                .ToListAsync();
            var ratePerNight = ratePlans.OrderBy(r => r.BaseRate).FirstOrDefault()?.BaseRate ?? roomType.BaseRate;
            var totalAmount = ratePerNight * nights;

            return Results.Ok(new CreateHoldResponse(
                hold.SessionToken,
                hold.ExpiresAtUtc,
                roomType.Name,
                checkInDate.ToString("yyyy-MM-dd"),
                checkOutDate.ToString("yyyy-MM-dd"),
                nights,
                totalAmount,
                property.DefaultCurrency));
        }).WithName("CreateBookingHold");

        // DELETE /api/v1/book/{propId}/hold/{token}
        group.MapDelete("/hold/{token:guid}", async (
            Guid propId,
            Guid token,
            StayFlowDbContext db) =>
        {
            var hold = await db.BookingHolds
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(h => h.SessionToken == token && h.PropertyId == propId);

            if (hold == null) return Results.NotFound();
            if (!hold.IsActive) return Results.Conflict("Hold is no longer active.");

            hold.Release();
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).WithName("ReleaseBookingHold");

        // POST /api/v1/book/{propId}/reservations
        group.MapPost("/reservations", async (
            Guid propId,
            CreatePublicReservationRequest req,
            StayFlowDbContext db,
            IEmailService emailService) =>
        {
            if (!req.PolicyAcknowledged)
                return Results.BadRequest("You must acknowledge the booking policy.");

            if (string.IsNullOrWhiteSpace(req.GuestName))
                return Results.BadRequest("Guest name is required.");

            var guestEmail = req.GuestEmail?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(guestEmail))
                return Results.BadRequest("Guest email is required for public bookings.");

            // Load hold (bypass tenant filter)
            var hold = await db.BookingHolds
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(h => h.SessionToken == req.HoldToken && h.PropertyId == propId);

            if (hold == null) return Results.NotFound("Hold not found.");
            if (!hold.IsActive)
                return Results.Problem(
                    "Hold has expired or is no longer active. Please search for availability again.",
                    statusCode: 409, title: "Hold Expired");

            // Load property
            var property = await db.Properties
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == propId && p.IsActive);
            if (property == null) return Results.NotFound("Property not found.");

            // Load room type
            var roomType = await db.RoomTypes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(rt => rt.Id == hold.RoomTypeId && rt.PropertyId == propId);
            if (roomType == null) return Results.NotFound("Room type not found.");

            // Look up or create guest within the property's org
            var guest = await db.Guests
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(g =>
                    g.OrganizationId == property.OrganizationId &&
                    g.Email == guestEmail);

            if (guest == null)
            {
                guest = Guest.Create(
                    property.OrganizationId,
                    req.GuestName.Trim(),
                    email: guestEmail,
                    phone: req.GuestPhone?.Trim(),
                    nationalId: req.GuestNationalId?.Trim(),
                    passportNumber: req.GuestPassportNumber?.Trim(),
                    nationality: req.GuestNationality?.Trim(),
                    notes: null);
                db.Guests.Add(guest);
            }

            // Coupon validation (server-side only)
            var couponCode = req.CouponCode?.Trim().ToUpperInvariant();
            decimal discountAmount = 0m;
            Coupon? coupon = null;

            if (!string.IsNullOrEmpty(couponCode))
            {
                coupon = await db.Coupons
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c =>
                        c.PropertyId == propId &&
                        c.Code == couponCode);

                if (coupon == null)
                    return Results.BadRequest("Coupon code not found.");

                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var couponError = coupon.InvalidReason(today);
                if (couponError != null)
                    return Results.BadRequest(couponError);
            }

            // Calculate rate
            var nights = hold.CheckOutDate.DayNumber - hold.CheckInDate.DayNumber;
            var ratePlans = await db.RatePlans
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(rp => rp.PropertyId == propId && rp.IsPublic && rp.IsActive &&
                             (rp.RoomTypeId == null || rp.RoomTypeId == hold.RoomTypeId))
                .ToListAsync();

            var ratePerNight = ratePlans.OrderBy(r => r.BaseRate).FirstOrDefault()?.BaseRate ?? roomType.BaseRate;
            var subtotal = ratePerNight * nights;

            if (coupon != null)
                discountAmount = coupon.CalculateDiscount(subtotal);

            var totalAmount = subtotal - discountAmount;

            // Create reservation — same lifecycle as direct bookings
            var reservation = Reservation.Create(
                organizationId: property.OrganizationId,
                propertyId: propId,
                guestId: guest.Id,
                checkInDate: hold.CheckInDate,
                checkOutDate: hold.CheckOutDate,
                adults: hold.Adults,
                children: hold.Children,
                roomTypeId: hold.RoomTypeId,
                roomId: null,
                quotedRate: ratePerNight,
                totalAmount: totalAmount,
                currency: property.DefaultCurrency,
                source: ReservationSource.BookingEngine,
                specialRequests: req.SpecialRequests?.Trim(),
                notes: null,
                couponCode: couponCode,
                discountAmount: discountAmount);

            db.Reservations.Add(reservation);

            // Convert the hold
            hold.Convert(reservation.Id);

            // Increment coupon usage
            coupon?.IncrementUsage();

            await db.SaveChangesAsync();

            // Send booking confirmation email (non-fatal if it fails)
            try
            {
                var msg = EmailTemplates.BookingConfirmation(
                    toAddress: guestEmail,
                    guestName: guest.FullName,
                    propertyName: property.Name,
                    reservationRef: reservation.Id.ToString("N")[..8].ToUpperInvariant(),
                    roomType: roomType.Name,
                    checkInDate: hold.CheckInDate.ToString("yyyy-MM-dd"),
                    checkOutDate: hold.CheckOutDate.ToString("yyyy-MM-dd"),
                    nights: nights,
                    adults: hold.Adults,
                    totalAmount: totalAmount,
                    currency: property.DefaultCurrency,
                    supportEmail: property.ContactEmail ?? "");
                await emailService.SendAsync(msg);
            }
            catch { /* non-fatal — booking already committed */ }

            return Results.Ok(new PublicReservationDto(
                reservation.Id,
                reservation.Id.ToString("N")[..8].ToUpperInvariant(),
                guest.FullName,
                property.Name,
                hold.CheckInDate.ToString("yyyy-MM-dd"),
                hold.CheckOutDate.ToString("yyyy-MM-dd"),
                nights,
                totalAmount,
                property.DefaultCurrency,
                reservation.Status.ToString()));
        }).WithName("CreatePublicReservation");
    }
}
