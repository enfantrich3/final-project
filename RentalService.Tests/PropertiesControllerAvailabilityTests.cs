using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalService;
using RentalService.Controllers;
using RentalService.Dto;
using RentalService.Models;
using Xunit;

namespace RentalService.Tests;

public class PropertiesControllerAvailabilityTests
{
    private static (AppDbContext Context, PropertiesController Controller, Guid PropertyId) CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);

        var ownerId = Guid.NewGuid();
        var owner = new User { Id = ownerId, Email = "owner@test.com", PasswordHash = "x", FullName = "Owner", IsVerified = true };

        var property = new Property
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Title = "Test Property",
            City = "Moscow",
            Address = "Test 1",
            BasePrice = 1000m
        };

        context.Users.Add(owner);
        context.Properties.Add(property);
        context.SaveChanges();

        var controller = new PropertiesController(context);

        return (context, controller, property.Id);
    }

    private static void SeedBooking(AppDbContext context, Guid propertyId, DateOnly checkIn, DateOnly checkOut, BookingStatus status = BookingStatus.pending)
    {
        context.Bookings.Add(new Booking
        {
            PropertyId = propertyId,
            RenterId = Guid.NewGuid(),
            CheckIn = checkIn,
            CheckOut = checkOut,
            TotalPrice = 3330m,
            ServiceFee = 330m,
            Status = status
        });
        context.SaveChanges();
    }

    [Fact]
    public async Task GetAvailability_HappyPath_ReturnsOccupiedRanges()
    {
        var (context, controller, propertyId) = CreateContext();
        SeedBooking(context, propertyId, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 15));
        SeedBooking(context, propertyId, new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 25), BookingStatus.confirmed);
        SeedBooking(context, propertyId, new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 8), BookingStatus.cancelled);

        var result = await controller.GetAvailability(propertyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var ok = Assert.IsType<OkObjectResult>(result);
        var ranges = Assert.IsAssignableFrom<List<OccupiedRangeDto>>(ok.Value);

        Assert.Equal(2, ranges.Count);
        Assert.Equal(new DateOnly(2026, 3, 10), ranges[0].CheckIn);
        Assert.Equal(new DateOnly(2026, 3, 15), ranges[0].CheckOut);
        Assert.Equal(new DateOnly(2026, 3, 20), ranges[1].CheckIn);
        Assert.Equal(new DateOnly(2026, 3, 25), ranges[1].CheckOut);
    }

    [Fact]
    public async Task GetAvailability_NoBookings_ReturnsEmptyList()
    {
        var (_, controller, propertyId) = CreateContext();

        var result = await controller.GetAvailability(propertyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var ok = Assert.IsType<OkObjectResult>(result);
        var ranges = Assert.IsAssignableFrom<List<OccupiedRangeDto>>(ok.Value);

        Assert.Empty(ranges);
    }

    [Fact]
    public async Task GetAvailability_RangeDoesNotOverlapAnyBooking_ReturnsEmptyList()
    {
        var (context, controller, propertyId) = CreateContext();
        SeedBooking(context, propertyId, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 15));

        var result = await controller.GetAvailability(propertyId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        var ok = Assert.IsType<OkObjectResult>(result);
        var ranges = Assert.IsAssignableFrom<List<OccupiedRangeDto>>(ok.Value);

        Assert.Empty(ranges);
    }

    [Fact]
    public async Task GetAvailability_PropertyNotFound_ReturnsNotFound()
    {
        var (_, controller, _) = CreateContext();

        var result = await controller.GetAvailability(Guid.NewGuid(), new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetAvailability_FromAfterTo_ReturnsBadRequest()
    {
        var (_, controller, propertyId) = CreateContext();

        var result = await controller.GetAvailability(propertyId, new DateOnly(2026, 3, 31), new DateOnly(2026, 3, 1));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetAvailability_MissingParams_ReturnsBadRequest()
    {
        var (_, controller, propertyId) = CreateContext();

        var result = await controller.GetAvailability(propertyId, null, null);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
