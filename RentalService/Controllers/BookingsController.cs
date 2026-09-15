using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using RentalService.Models;
using RentalService.Services;

namespace RentalService.Controllers;

[ApiController]
[Route("api/v1/bookings")]
public class BookingsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly MockEmailService _emailService;
    private readonly MockPaymentService _paymentService;

    public BookingsController(AppDbContext context, MockEmailService emailService, MockPaymentService paymentService)
    {
        _context = context;
        _emailService = emailService;
        _paymentService = paymentService;
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequest request)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
            return Unauthorized();

        var renter = await _context.Users.FindAsync(currentUserId.Value);
        if (renter == null || renter.IsBlocked)
            return Forbid();
        if (!renter.IsVerified)
            return StatusCode(403, "Бронирование доступно только верифицированным пользователям.");

        if (request.CheckIn >= request.CheckOut)
            return BadRequest("Дата заезда должна быть раньше даты выезда.");

        var property = await _context.Properties
            .Include(p => p.PriceRates)
            .FirstOrDefaultAsync(p => p.Id == request.PropertyId);

        if (property == null)
            return NotFound("Property not found.");

        if (property.OwnerId == currentUserId.Value)
            return BadRequest("Нельзя бронировать свой объект.");

        var existingBookings = await _context.Bookings
            .Where(b => b.PropertyId == request.PropertyId && b.Status != BookingStatus.cancelled)
            .ToListAsync();

        foreach (var existing in existingBookings)
        {
            if (DatesOverlap(existing.CheckIn, existing.CheckOut, request.CheckIn, request.CheckOut))
                return Conflict("Даты пересекаются с уже существующим бронированием.");
        }

        decimal subtotal = 0;
        var date = request.CheckIn;
        while (date < request.CheckOut)
        {
            var rate = property.PriceRates
                .FirstOrDefault(r => date >= r.StartDate && date <= r.EndDate);

            if (rate != null)
                subtotal += rate.PricePerDay;
            else
                subtotal += property.BasePrice;

            date = date.AddDays(1);
        }

        var serviceFee = Math.Round(subtotal * 0.11m, 2);
        var totalPrice = subtotal + serviceFee;

        var booking = new Booking
        {
            PropertyId = request.PropertyId,
            RenterId = currentUserId.Value,
            CheckIn = request.CheckIn,
            CheckOut = request.CheckOut,
            TotalPrice = totalPrice,
            ServiceFee = serviceFee,
            Comment = request.Comment ?? string.Empty,
            Status = BookingStatus.pending
        };

        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync();

        _emailService.SendBookingCreatedEmail(booking);

        return CreatedAtAction(nameof(GetBooking), new { id = booking.Id }, ToResponse(booking));
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<IActionResult> GetBooking(Guid id)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
            return Unauthorized();

        var booking = await _context.Bookings
            .Include(b => b.Property)
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking == null)
            return NotFound();

        if (booking.RenterId != currentUserId.Value && booking.Property?.OwnerId != currentUserId.Value)
            return Forbid();

        return Ok(ToResponse(booking));
    }

    [HttpGet("trips")]
    [Authorize]
    public async Task<IActionResult> GetTrips()
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
            return Unauthorized();

        var bookings = await _context.Bookings
            .Include(b => b.Payment)
            .Where(b => b.RenterId == currentUserId.Value)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        var result = new List<BookingResponse>();
        foreach (var booking in bookings)
            result.Add(ToResponse(booking));

        return Ok(result);
    }

    [HttpGet("orders")]
    [Authorize]
    public async Task<IActionResult> GetOrders()
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
            return Unauthorized();

        var bookings = await _context.Bookings
            .Include(b => b.Property)
            .Include(b => b.Payment)
            .Where(b => b.Property != null && b.Property.OwnerId == currentUserId.Value)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        var result = new List<BookingResponse>();
        foreach (var booking in bookings)
            result.Add(ToResponse(booking));

        return Ok(result);
    }

    [HttpPost("{id}/cancel")]
    [Authorize]
    public async Task<IActionResult> CancelBooking(Guid id)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
            return Unauthorized();

        var booking = await _context.Bookings
            .Include(b => b.Property)
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking == null)
            return NotFound();

        if (booking.RenterId != currentUserId.Value && booking.Property?.OwnerId != currentUserId.Value)
            return Forbid();

        if (booking.Status == BookingStatus.completed)
            return BadRequest("Завершенные бронирования не могут быть отменены");

        if (booking.Status == BookingStatus.cancelled)
            return BadRequest("Бронирование уже отменено");

        booking.Status = BookingStatus.cancelled;

        if (booking.Payment != null && booking.Payment.Status == PaymentStatus.succeeded)
        {
            _paymentService.RefundPayment(booking.Payment);
            booking.Payment.Status = PaymentStatus.refunded;
        }

        await _context.SaveChangesAsync();

        _emailService.SendBookingCancelledEmail(booking);

        return Ok(ToResponse(booking));
    }

    [HttpPatch("{id}/reschedule")]
    [Authorize]
    public async Task<IActionResult> RescheduleBooking(Guid id, [FromBody] RescheduleBookingRequest request)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
            return Unauthorized();

        var booking = await _context.Bookings
            .Include(b => b.Property)
                .ThenInclude(p => p!.PriceRates)
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (booking == null)
            return NotFound();

        if (booking.RenterId != currentUserId.Value)
            return Forbid();

        if (booking.Status != BookingStatus.pending && booking.Status != BookingStatus.confirmed)
            return BadRequest("Перенос дат доступен только для бронирований в статусе pending или confirmed.");

        if (request.CheckIn >= request.CheckOut)
            return BadRequest("Дата заезда должна быть раньше даты выезда.");

        if (booking.Property == null)
            return NotFound("Property not found.");

        var existingBookings = await _context.Bookings
            .Where(b => b.PropertyId == booking.PropertyId && b.Id != booking.Id && b.Status != BookingStatus.cancelled)
            .ToListAsync();

        foreach (var existing in existingBookings)
        {
            if (DatesOverlap(existing.CheckIn, existing.CheckOut, request.CheckIn, request.CheckOut))
                return Conflict("Даты пересекаются с уже существующим бронированием.");
        }

        decimal subtotal = 0;
        var date = request.CheckIn;
        while (date < request.CheckOut)
        {
            var rate = booking.Property.PriceRates
                .FirstOrDefault(r => date >= r.StartDate && date <= r.EndDate);

            subtotal += rate != null ? rate.PricePerDay : booking.Property.BasePrice;

            date = date.AddDays(1);
        }

        var serviceFee = Math.Round(subtotal * 0.11m, 2);
        var totalPrice = subtotal + serviceFee;

        booking.CheckIn = request.CheckIn;
        booking.CheckOut = request.CheckOut;
        booking.TotalPrice = totalPrice;
        booking.ServiceFee = serviceFee;

        await _context.SaveChangesAsync();

        _emailService.SendBookingRescheduledEmail(booking);

        return Ok(ToResponse(booking));
    }

    private static bool DatesOverlap(DateOnly startA, DateOnly endA, DateOnly startB, DateOnly endB)
    {
        if (startA < endB && startB < endA)
            return true;

        return false;
    }

    private Guid? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdClaim, out var id))
            return id;

        return null;
    }

    private static BookingResponse ToResponse(Booking booking)
    {
        var paymentResponse = booking.Payment == null ? null : new PaymentResponse
        {
            Id = booking.Payment.Id,
            BookingId = booking.Payment.BookingId,
            Amount = booking.Payment.Amount,
            Status = booking.Payment.Status,
            TransactionDate = booking.Payment.TransactionDate
        };

        return new BookingResponse
        {
            Id = booking.Id,
            PropertyId = booking.PropertyId,
            RenterId = booking.RenterId,
            CheckIn = booking.CheckIn,
            CheckOut = booking.CheckOut,
            TotalPrice = booking.TotalPrice,
            ServiceFee = booking.ServiceFee,
            Status = booking.Status,
            Comment = booking.Comment,
            CreatedAt = booking.CreatedAt,
            Payment = paymentResponse
        };
    }

    public class CreateBookingRequest
    {
        public Guid PropertyId { get; set; }
        public DateOnly CheckIn { get; set; }
        public DateOnly CheckOut { get; set; }
        public string? Comment { get; set; }
    }

    public class RescheduleBookingRequest
    {
        public DateOnly CheckIn { get; set; }
        public DateOnly CheckOut { get; set; }
    }

    public class BookingResponse
    {
        public Guid Id { get; set; }
        public Guid PropertyId { get; set; }
        public Guid RenterId { get; set; }
        public DateOnly CheckIn { get; set; }
        public DateOnly CheckOut { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal ServiceFee { get; set; }
        public BookingStatus Status { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public PaymentResponse? Payment { get; set; }
    }

    public class PaymentResponse
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public decimal Amount { get; set; }
        public PaymentStatus Status { get; set; }
        public DateTime TransactionDate { get; set; }
    }
}
