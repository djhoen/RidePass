using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Instructor
{
    public class UpsertInstructorRequest
    {
        [Required, MaxLength(120)] public string Name { get; set; } = null!;
        [MaxLength(200)] public string? Email { get; set; }
        [MaxLength(40)] public string? Phone { get; set; }
        [MaxLength(2000)] public string? Bio { get; set; }
        [MaxLength(1000)] public string? ImageUrl { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 100;
        /// <summary>How many students this coach takes in one session. Caps a training group
        /// alongside the group's own inventory (docs/lessons.md).</summary>
        [Range(1, 100)] public int MaxStudentsPerSession { get; set; } = 8;
    }

    public class InstructorResponse
    {
        public Guid Id { get; set; }
        public int MaxStudentsPerSession { get; set; }
        public string Name { get; set; } = null!;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Bio { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }
}
