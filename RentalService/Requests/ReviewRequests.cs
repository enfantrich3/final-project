namespace RentalService.Dto;

public class CreateReviewDto
{
    public Guid PropertyId { get; set; }
    public int Rating { get; set; }
    public string Text { get; set; } = string.Empty;
}

public class UpdateReviewDto
{
    public int Rating { get; set; }
    public string Text { get; set; } = string.Empty;
}
