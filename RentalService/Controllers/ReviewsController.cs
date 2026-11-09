using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalService.Dto;
using RentalService.Models;

namespace RentalService.Controllers;

[ApiController]
[Route("api/v1/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReviewsController(AppDbContext db)
    {
        _db = db;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("property/{propertyId:guid}")]
    public async Task<IActionResult> GetForProperty(Guid propertyId)
    {
        var reviews = await _db.Reviews
            .Include(r => r.Author)
            .Where(r => r.PropertyId == propertyId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(reviews);
    }

    [HttpGet("property/{propertyId:guid}/rating")]
    public async Task<IActionResult> GetAverageRating(Guid propertyId)
    {
        var ratings = _db.Reviews
            .Where(r => r.PropertyId == propertyId)
            .Select(r => r.Rating)
            .ToListAsync()
            .Result;

        if (ratings.Count == 0)
            return Ok(new { Average = 0.0, Count = 0 });

        return Ok(new { Average = Math.Round(ratings.Average(), 1), Count = ratings.Count });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateReviewDto dto)
    {
        var userId = GetUserId();

        if (dto.Rating < 1 || dto.Rating > 5)
            return BadRequest("Оценка должна быть от 1 до 5.");

        var hasCompletedStay = await _db.Bookings.AnyAsync(b =>
            b.PropertyId == dto.PropertyId &&
            b.RenterId == userId &&
            b.Status == BookingStatus.completed);

        if (!hasCompletedStay)
            return StatusCode(403, "Отзыв можно оставить только после завершённой поездки.");

        var review = new Review
        {
            PropertyId = dto.PropertyId,
            AuthorId = userId,
            Rating = dto.Rating,
            Text = dto.Text,
            CreatedAt = DateTime.Now
        };

        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();

        Console.WriteLine("Review " + review.Id + " created by user " + userId);

        return CreatedAtAction(nameof(GetForProperty), new { propertyId = review.PropertyId }, review);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateReviewDto dto)
    {
        var userId = GetUserId();

        if (dto.Rating < 1 || dto.Rating > 5)
            return BadRequest("Оценка должна быть от 1 до 5.");

        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == id && r.AuthorId == userId);
        if (review == null)
            return NotFound();

        review.Rating = dto.Rating;
        review.Text = dto.Text;
        _db.SaveChangesAsync();

        return Ok(review);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var review = await _db.Reviews.FindAsync(id);
        if (review == null)
            return NotFound("Review " + id + " not found");

        _db.Reviews.Remove(review);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
