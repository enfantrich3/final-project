using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalService.Dto;
using RentalService.Models;

namespace RentalService.Controllers;

[ApiController]
[Route("api/v1/properties")]
public class PropertiesController : ControllerBase
{
    private readonly AppDbContext _db;

    public PropertiesController(AppDbContext db)
    {
        _db = db;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? city,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] int? rooms,
        [FromQuery] DateOnly? checkIn,
        [FromQuery] DateOnly? checkOut)
    {
        var query = _db.Properties
            .Include(p => p.Photos)
            .Include(p => p.PriceRates)
            .Include(p => p.Bookings)
            .Where(p => p.Status == PropertyStatus.active)
            .AsQueryable();

        if (!string.IsNullOrEmpty(city))
            query = query.Where(p => p.City.ToLower().Contains(city.ToLower()));

        if (minPrice.HasValue)
            query = query.Where(p => p.BasePrice >= minPrice);

        if (maxPrice.HasValue)
            query = query.Where(p => p.BasePrice <= maxPrice);

        if (rooms.HasValue)
            query = query.Where(p => p.Rooms == rooms);

        if (checkIn.HasValue && checkOut.HasValue)
        {
            query = query.Where(p => !p.Bookings.Any(b =>
                (b.Status == BookingStatus.pending || b.Status == BookingStatus.confirmed) &&
                b.CheckIn < checkOut && b.CheckOut > checkIn));
        }

        return Ok(await query.ToListAsync());
    }

    [HttpGet("my")]
    [Authorize]
    public async Task<IActionResult> GetMy()
    {
        var userId = GetUserId();
        var properties = await _db.Properties
            .Include(p => p.Photos)
            .Include(p => p.PriceRates)
            .Where(p => p.OwnerId == userId)
            .ToListAsync();

        return Ok(properties);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var property = await _db.Properties
            .Include(p => p.Photos)
            .Include(p => p.PriceRates)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (property == null)
            return NotFound();

        return Ok(property);
    }

    [HttpGet("{id:guid}/calendar")]
    [Authorize]
    public async Task<IActionResult> GetPropertyCalendar(Guid id)
    {
        var property = await _db.Properties
            .Include(p => p.Bookings)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (property == null)
            return NotFound("Property not found.");

        var dates = new List<string>();

        foreach (var booking in property.Bookings)
        {
            if (booking.Status == BookingStatus.cancelled)
                continue;

            var date = booking.CheckIn;
            while (date < booking.CheckOut)
            {
                dates.Add(date.ToString("yyyy-MM-dd"));
                date = date.AddDays(1);
            }
        }

        dates.Sort();
        var uniqueDates = new List<string>();
        foreach (var date in dates)
        {
            if (!uniqueDates.Contains(date))
                uniqueDates.Add(date);
        }

        return Ok(new { OccupiedDates = uniqueDates });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreatePropertyDto dto)
    {
        var userId = GetUserId();
        var user = await _db.Users.FindAsync(userId);

        if (user == null || user.Role != UserRole.host || !user.IsVerified)
            return Forbid();

        var property = new Property
        {
            OwnerId = userId,
            Title = dto.Title,
            City = dto.City,
            Address = dto.Address,
            PropertyType = dto.PropertyType,
            Rooms = dto.Rooms,
            BasePrice = dto.BasePrice,
            Description = dto.Description,
            Status = PropertyStatus.draft
        };

        _db.Properties.Add(property);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = property.Id }, property);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePropertyDto dto)
    {
        var userId = GetUserId();
        var property = await _db.Properties
            .Include(p => p.Bookings)
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);

        if (property == null)
            return NotFound();

        var hasActiveBookings = property.Bookings
            .Any(b => b.Status == BookingStatus.pending || b.Status == BookingStatus.confirmed);

        if (dto.Title != null) property.Title = dto.Title;
        if (dto.Description != null) property.Description = dto.Description;
        if (dto.BasePrice.HasValue) property.BasePrice = dto.BasePrice.Value;

        
        if (!hasActiveBookings)
        {
            if (dto.City != null) property.City = dto.City;
            if (dto.Address != null) property.Address = dto.Address;
            if (dto.Rooms.HasValue) property.Rooms = dto.Rooms.Value;
            if (dto.PropertyType.HasValue) property.PropertyType = dto.PropertyType.Value;
        }

        await _db.SaveChangesAsync();
        return Ok(property);
    }

    [HttpPut("{id:guid}/status")]
    [Authorize]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusDto dto)
    {
        var userId = GetUserId();
        var property = await _db.Properties
            .Include(p => p.Photos)
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);

        if (property == null)
            return NotFound();

        
        if (dto.Status == PropertyStatus.active && !property.Photos.Any())
            return BadRequest("Нельзя опубликовать объект без фотографий.");

        property.Status = dto.Status;
        await _db.SaveChangesAsync();
        return Ok(property);
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = GetUserId();
        var property = await _db.Properties
            .Include(p => p.Bookings)
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);

        if (property == null)
            return NotFound();

        
        var hasActiveBookings = property.Bookings
            .Any(b => b.Status == BookingStatus.pending || b.Status == BookingStatus.confirmed);

        if (hasActiveBookings)
            return BadRequest("Нельзя удалить объект с активными бронированиями.");

        _db.Properties.Remove(property);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/photos")]
    [Authorize]
    public async Task<IActionResult> AddPhoto(Guid id, [FromBody] AddPhotoDto dto)
    {
        var userId = GetUserId();
        var property = await _db.Properties
            .Include(p => p.Photos)
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);

        if (property == null)
            return NotFound();

        if (dto.IsCover)
        {
            foreach (var photo in property.Photos)
                photo.IsCover = false;
        }

        var newPhoto = new PropertyPhoto
        {
            PropertyId = id,
            Url = dto.Url,
            IsCover = dto.IsCover
        };

        _db.PropertyPhotos.Add(newPhoto);
        await _db.SaveChangesAsync();
        return Ok(newPhoto);
    }

    [HttpDelete("{id:guid}/photos/{photoId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeletePhoto(Guid id, Guid photoId)
    {
        var userId = GetUserId();
        var property = await _db.Properties
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);

        if (property == null)
            return NotFound();

        var photo = await _db.PropertyPhotos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.PropertyId == id);

        if (photo == null)
            return NotFound();

        _db.PropertyPhotos.Remove(photo);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/rates")]
    [Authorize]
    public async Task<IActionResult> AddPriceRate(Guid id, [FromBody] AddPriceRateDto dto)
    {
        var userId = GetUserId();
        var property = await _db.Properties
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);

        if (property == null)
            return NotFound();

        var rate = new PriceRate
        {
            PropertyId = id,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            PricePerDay = dto.PricePerDay
        };

        _db.PriceRates.Add(rate);
        await _db.SaveChangesAsync();
        return Ok(rate);
    }

    [HttpDelete("{id:guid}/rates/{rateId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeletePriceRate(Guid id, Guid rateId)
    {
        var userId = GetUserId();
        var property = await _db.Properties
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);

        if (property == null)
            return NotFound();

        var rate = await _db.PriceRates
            .FirstOrDefaultAsync(r => r.Id == rateId && r.PropertyId == id);

        if (rate == null)
            return NotFound();

        _db.PriceRates.Remove(rate);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
