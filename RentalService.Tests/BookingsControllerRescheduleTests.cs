using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalService;
using RentalService.Controllers;
using RentalService.Models;
using RentalService.Services;
using Xunit;

namespace RentalService.Tests;

public class BookingsControllerRescheduleTests
{
    private static (AppDbContext Context, BookingsController Controller, Guid RenterId, Guid OwnerId, Guid PropertyId) CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);

        var ownerId = Guid.NewGuid();
        var renterId = Guid.NewGuid();

        var owner = new User { Id = ownerId, Email = "owner@test.com", PasswordHash = "x", FullName = "Owner", IsVerified = true };
        var renter = new User { Id = renterId, Email = "renter@test.com", PasswordHash = "x", FullName = "Renter", IsVerified = true };

        var property = new Property
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Title = "Test Property",
            City = "Moscow",
            Address = "Test 1",
            BasePrice = 1000m
        };

        context.Users.AddRange(owner, renter);
        context.Properties.Add(property);
        context.SaveChanges();

        var controller = new BookingsController(context, new MockEmailService(), new MockPaymentService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, renterId.ToString())
                }, "TestAuth"))
            }
        };

        return (context, controller, renterId, ownerId, property.Id);
    }

    private static Booking SeedBooking(AppDbContext context, Guid propertyId, Guid renterId, DateOnly checkIn, DateOnly checkOut, BookingStatus status = BookingStatus.pending)
    {
        var booking = new Booking
        {
            PropertyId = propertyId,
            RenterId = renterId,
            CheckIn = checkIn,
            CheckOut = checkOut,
            TotalPrice = 3330m,
            ServiceFee = 330m,
            Status = status
        };

        context.Bookings.Add(booking);
        context.SaveChanges();
        return booking;
    }

    [Fact]
    public async Task RescheduleBooking_HappyPath_UpdatesDatesAndRecalculatesPrice()
    {
        var (context, controller, renterId, _, propertyId) = CreateContext();
        var booking = SeedBooking(context, propertyId, renterId,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 4));

        var newCheckIn = new DateOnly(2026, 2, 1);
        var newCheckOut = new DateOnly(2026, 2, 6);

        var result = await controller.RescheduleBooking(booking.Id, new BookingsController.RescheduleBookingRequest
        {
            CheckIn = newCheckIn,
            CheckOut = newCheckOut
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<BookingsController.BookingResponse>(ok.Value);

        Assert.Equal(newCheckIn, response.CheckIn);
        Assert.Equal(newCheckOut, response.CheckOut);
        // 5 nights * 1000 base price = 5000 subtotal + 11% service fee = 5550 total
        Assert.Equal(550m, response.ServiceFee);
        Assert.Equal(5550m, response.TotalPrice);
    }

    [Fact]
    public async Task RescheduleBooking_OverlapsWithAnotherBooking_ReturnsConflict()
    {
        var (context, controller, renterId, _, propertyId) = CreateContext();
        var booking = SeedBooking(context, propertyId, renterId,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 4));
        SeedBooking(context, propertyId, Guid.NewGuid(),
            new DateOnly(2026, 2, 3), new DateOnly(2026, 2, 8));

        var result = await controller.RescheduleBooking(booking.Id, new BookingsController.RescheduleBookingRequest
        {
            CheckIn = new DateOnly(2026, 2, 1),
            CheckOut = new DateOnly(2026, 2, 6)
        });

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task RescheduleBooking_InvalidDateRange_ReturnsBadRequest()
    {
        var (context, controller, renterId, _, propertyId) = CreateContext();
        var booking = SeedBooking(context, propertyId, renterId,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 4));

        var result = await controller.RescheduleBooking(booking.Id, new BookingsController.RescheduleBookingRequest
        {
            CheckIn = new DateOnly(2026, 2, 6),
            CheckOut = new DateOnly(2026, 2, 1)
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RescheduleBooking_NotOwnedByCurrentUser_ReturnsForbid()
    {
        var (context, controller, _, _, propertyId) = CreateContext();
        var booking = SeedBooking(context, propertyId, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 4));

        var result = await controller.RescheduleBooking(booking.Id, new BookingsController.RescheduleBookingRequest
        {
            CheckIn = new DateOnly(2026, 2, 1),
            CheckOut = new DateOnly(2026, 2, 6)
        });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task RescheduleBooking_BookingNotFound_ReturnsNotFound()
    {
        var (_, controller, _, _, _) = CreateContext();

        var result = await controller.RescheduleBooking(Guid.NewGuid(), new BookingsController.RescheduleBookingRequest
        {
            CheckIn = new DateOnly(2026, 2, 1),
            CheckOut = new DateOnly(2026, 2, 6)
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData(BookingStatus.cancelled)]
    [InlineData(BookingStatus.completed)]
    public async Task RescheduleBooking_NonReschedulableStatus_ReturnsBadRequest(BookingStatus status)
    {
        var (context, controller, renterId, _, propertyId) = CreateContext();
        var booking = SeedBooking(context, propertyId, renterId,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 4), status);

        var result = await controller.RescheduleBooking(booking.Id, new BookingsController.RescheduleBookingRequest
        {
            CheckIn = new DateOnly(2026, 2, 1),
            CheckOut = new DateOnly(2026, 2, 6)
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
