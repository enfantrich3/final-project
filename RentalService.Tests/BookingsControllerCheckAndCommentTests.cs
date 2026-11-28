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

public class BookingsControllerCheckAndCommentTests
{
    private static (AppDbContext Context, BookingsController Controller, Guid RenterId, Guid PropertyId) CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        var renterId = Guid.NewGuid();
        var property = new Property
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            Title = "Test Property",
            City = "Moscow",
            Address = "Test 1",
            BasePrice = 1000m
        };
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

        return (context, controller, renterId, property.Id);
    }

    private static Booking SeedBooking(AppDbContext context, Guid propertyId, Guid renterId, DateOnly checkIn, DateOnly checkOut)
    {
        var booking = new Booking
        {
            PropertyId = propertyId,
            RenterId = renterId,
            CheckIn = checkIn,
            CheckOut = checkOut,
            TotalPrice = 3330m,
            ServiceFee = 330m
        };
        context.Bookings.Add(booking);
        context.SaveChanges();
        return booking;
    }

    [Fact]
    public async Task CheckDates_FreeDates_ReturnsAvailable()
    {
        var (context, controller, renterId, propertyId) = CreateContext();
        SeedBooking(context, propertyId, renterId, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 5));

        var result = await controller.CheckDates(propertyId, new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 12));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("Available = True", ok.Value!.ToString());
    }

    [Fact]
    public async Task CheckDates_OverlappingDates_ReturnsBusy()
    {
        var (context, controller, renterId, propertyId) = CreateContext();
        SeedBooking(context, propertyId, renterId, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 5));

        var result = await controller.CheckDates(propertyId, new DateOnly(2026, 7, 3), new DateOnly(2026, 7, 8));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("Available = False", ok.Value!.ToString());
    }

    [Fact]
    public async Task UpdateComment_OwnBooking_UpdatesComment()
    {
        var (context, controller, renterId, propertyId) = CreateContext();
        var booking = SeedBooking(context, propertyId, renterId, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 5));

        var result = await controller.UpdateComment(booking.Id,
            new BookingsController.UpdateBookingCommentRequest { Comment = "Приедем поздно" });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Приедем поздно", context.Bookings.Single().Comment);
    }
}
