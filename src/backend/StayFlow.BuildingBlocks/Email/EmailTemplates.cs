namespace StayFlow.BuildingBlocks.Email;

/// <summary>
/// Builds transactional email bodies. All text is English; Thai compliance
/// documents (R.S.3, R.S.4, TM.30) are separate and always rendered in Thai.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage BookingConfirmation(
        string toAddress,
        string guestName,
        string propertyName,
        string reservationRef,
        string roomType,
        string checkInDate,
        string checkOutDate,
        int nights,
        int adults,
        decimal totalAmount,
        string currency,
        string supportEmail)
    {
        var subject = $"Booking Confirmed — {reservationRef} · {propertyName}";
        var html = $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"/></head>
            <body style="font-family:sans-serif;color:#222;max-width:600px;margin:0 auto;padding:24px">
              <h2 style="color:#1a56a0">{propertyName}</h2>
              <h3>Your booking is confirmed</h3>
              <p>Dear {Encode(guestName)},</p>
              <p>Thank you for your reservation. Here are your booking details:</p>
              <table style="border-collapse:collapse;width:100%;margin:16px 0">
                <tr style="background:#f0f4fa">
                  <td style="padding:8px 12px;font-weight:bold">Booking Reference</td>
                  <td style="padding:8px 12px">{Encode(reservationRef)}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px;font-weight:bold">Property</td>
                  <td style="padding:8px 12px">{Encode(propertyName)}</td>
                </tr>
                <tr style="background:#f0f4fa">
                  <td style="padding:8px 12px;font-weight:bold">Room Type</td>
                  <td style="padding:8px 12px">{Encode(roomType)}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px;font-weight:bold">Check-in</td>
                  <td style="padding:8px 12px">{Encode(checkInDate)}</td>
                </tr>
                <tr style="background:#f0f4fa">
                  <td style="padding:8px 12px;font-weight:bold">Check-out</td>
                  <td style="padding:8px 12px">{Encode(checkOutDate)}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px;font-weight:bold">Nights</td>
                  <td style="padding:8px 12px">{nights}</td>
                </tr>
                <tr style="background:#f0f4fa">
                  <td style="padding:8px 12px;font-weight:bold">Guests</td>
                  <td style="padding:8px 12px">{adults} adult{(adults != 1 ? "s" : "")}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px;font-weight:bold">Total</td>
                  <td style="padding:8px 12px;font-weight:bold">{totalAmount:N2} {Encode(currency)}</td>
                </tr>
              </table>
              <p>If you have questions, please contact us at <a href="mailto:{Encode(supportEmail)}">{Encode(supportEmail)}</a>.</p>
              <hr style="border:none;border-top:1px solid #ddd;margin:24px 0"/>
              <p style="font-size:12px;color:#888">This is an automated message from {Encode(propertyName)} powered by StayFlow PMS.</p>
            </body>
            </html>
            """;

        var text = $"""
            Booking Confirmed — {reservationRef}

            Dear {guestName},

            Your booking at {propertyName} is confirmed.

            Booking Reference : {reservationRef}
            Property          : {propertyName}
            Room Type         : {roomType}
            Check-in          : {checkInDate}
            Check-out         : {checkOutDate}
            Nights            : {nights}
            Guests            : {adults} adult{(adults != 1 ? "s" : "")}
            Total             : {totalAmount:N2} {currency}

            Questions? Contact us at {supportEmail}
            """;

        return new EmailMessage(toAddress, subject, html, text);
    }

    public static EmailMessage DailyReport(
        string toAddress,
        string propertyName,
        DateOnly reportDate,
        int occupiedRooms,
        int totalRooms,
        decimal occupancyPct,
        decimal adr,
        decimal revPar,
        decimal revenueToday,
        string currency,
        int arrivalsToday,
        int departuresToday,
        int newReservations)
    {
        var dateStr = reportDate.ToString("dddd, d MMMM yyyy");
        var subject = $"Daily Report — {propertyName} — {reportDate:d MMM yyyy}";

        var html = $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"/></head>
            <body style="font-family:sans-serif;color:#222;max-width:600px;margin:0 auto;padding:24px">
              <h2 style="color:#1a56a0">{Encode(propertyName)}</h2>
              <h3>Daily Summary — {Encode(dateStr)}</h3>
              <table style="border-collapse:collapse;width:100%;margin:16px 0">
                <tr style="background:#1a56a0;color:#fff">
                  <th style="padding:10px 12px;text-align:left" colspan="2">Occupancy &amp; Revenue</th>
                </tr>
                <tr style="background:#f0f4fa">
                  <td style="padding:8px 12px">Occupied Rooms</td>
                  <td style="padding:8px 12px;font-weight:bold">{occupiedRooms} / {totalRooms} ({occupancyPct:F1}%)</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px">ADR (Average Daily Rate)</td>
                  <td style="padding:8px 12px;font-weight:bold">{adr:N2} {Encode(currency)}</td>
                </tr>
                <tr style="background:#f0f4fa">
                  <td style="padding:8px 12px">RevPAR</td>
                  <td style="padding:8px 12px;font-weight:bold">{revPar:N2} {Encode(currency)}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px">Revenue Today</td>
                  <td style="padding:8px 12px;font-weight:bold">{revenueToday:N2} {Encode(currency)}</td>
                </tr>
                <tr style="background:#1a56a0;color:#fff">
                  <th style="padding:10px 12px;text-align:left" colspan="2">Operations</th>
                </tr>
                <tr style="background:#f0f4fa">
                  <td style="padding:8px 12px">Arrivals</td>
                  <td style="padding:8px 12px;font-weight:bold">{arrivalsToday}</td>
                </tr>
                <tr>
                  <td style="padding:8px 12px">Departures</td>
                  <td style="padding:8px 12px;font-weight:bold">{departuresToday}</td>
                </tr>
                <tr style="background:#f0f4fa">
                  <td style="padding:8px 12px">New Reservations</td>
                  <td style="padding:8px 12px;font-weight:bold">{newReservations}</td>
                </tr>
              </table>
              <hr style="border:none;border-top:1px solid #ddd;margin:24px 0"/>
              <p style="font-size:12px;color:#888">Automated daily report from {Encode(propertyName)} · StayFlow PMS</p>
            </body>
            </html>
            """;

        var text = $"""
            Daily Report — {propertyName}
            {dateStr}

            OCCUPANCY & REVENUE
            Occupied Rooms  : {occupiedRooms} / {totalRooms} ({occupancyPct:F1}%)
            ADR             : {adr:N2} {currency}
            RevPAR          : {revPar:N2} {currency}
            Revenue Today   : {revenueToday:N2} {currency}

            OPERATIONS
            Arrivals        : {arrivalsToday}
            Departures      : {departuresToday}
            New Reservations: {newReservations}
            """;

        return new EmailMessage(toAddress, subject, html, text);
    }

    private static string Encode(string? s) =>
        System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
}
