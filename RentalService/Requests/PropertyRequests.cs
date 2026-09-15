using RentalService.Models;

namespace RentalService.Dto;

public class CreatePropertyDto
{
    public string Title { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public PropertyType PropertyType { get; set; }
    public int Rooms { get; set; }
    public decimal BasePrice { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class UpdatePropertyDto
{
    public string? Title { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public PropertyType? PropertyType { get; set; }
    public int? Rooms { get; set; }
    public decimal? BasePrice { get; set; }
    public string? Description { get; set; }
}

public class UpdateStatusDto
{
    public PropertyStatus Status { get; set; }
}

public class AddPhotoDto
{
    public string Url { get; set; } = string.Empty;
    public bool IsCover { get; set; }
}

public class AddPriceRateDto
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal PricePerDay { get; set; }
}

public class OccupiedRangeDto
{
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
}
