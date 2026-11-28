using System.ComponentModel.DataAnnotations;

namespace RentalService.Models;

public class Review
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PropertyId { get; set; }

    [Required]
    public Guid AuthorId { get; set; }

    public int Rating { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public Property? Property { get; set; }
    public User? Author { get; set; }
}
