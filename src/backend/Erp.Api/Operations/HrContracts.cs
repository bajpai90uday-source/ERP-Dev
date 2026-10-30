using System.ComponentModel.DataAnnotations;

namespace Erp.Api.Operations;

internal sealed record HrCodeNameRequest
{
    [Required, StringLength(40, MinimumLength = 1)]
    public required string Code { get; init; }
    [Required, StringLength(160, MinimumLength = 1)]
    public required string Name { get; init; }
}

internal sealed record EmployeeRequest
{
    [Required, StringLength(40, MinimumLength = 1)]
    public required string EmployeeNumber { get; init; }
    [Required, StringLength(100, MinimumLength = 1)]
    public required string FirstName { get; init; }
    [Required, StringLength(100, MinimumLength = 1)]
    public required string LastName { get; init; }
    [Required]
    public Guid DepartmentId { get; init; }
    [Required]
    public Guid DesignationId { get; init; }
    public DateOnly HireDate { get; init; }
    [EmailAddress, StringLength(254)]
    public string? PersonalEmail { get; init; }
    [StringLength(40)]
    public string? Phone { get; init; }
    public DateOnly? BirthDate { get; init; }
}

internal sealed record ShiftRequest
{
    [Required, StringLength(40, MinimumLength = 1)]
    public required string Code { get; init; }
    [Required, StringLength(160, MinimumLength = 1)]
    public required string Name { get; init; }
    public TimeOnly StartsAt { get; init; }
    public TimeOnly EndsAt { get; init; }
}

internal sealed record AttendanceRequest
{
    [Required]
    public Guid EmployeeId { get; init; }
    [Required]
    public Guid ShiftId { get; init; }
    public DateOnly AttendanceDate { get; init; }
    public DateTimeOffset? ClockIn { get; init; }
    public DateTimeOffset? ClockOut { get; init; }
    [Required, StringLength(20)]
    public required string Status { get; init; }
}

internal sealed record LeaveRequest
{
    [Required]
    public Guid EmployeeId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    [Required, StringLength(60, MinimumLength = 1)]
    public required string LeaveType { get; init; }
    [StringLength(500)]
    public string? Reason { get; init; }
}

internal sealed record LeaveReviewRequest
{
    [Required]
    public bool Approve { get; init; }
    [StringLength(500)]
    public string? Reason { get; init; }
}

internal sealed record EmployeeSkillRequest
{
    [Required, StringLength(120, MinimumLength = 1)]
    public required string Skill { get; init; }
    [Required, StringLength(24, MinimumLength = 1)]
    public required string Proficiency { get; init; }
    public DateOnly? ValidUntil { get; init; }
}

internal sealed record TrainingRequest
{
    [Required, StringLength(160, MinimumLength = 1)]
    public required string CourseName { get; init; }
    public DateOnly CompletedOn { get; init; }
    public DateOnly? ExpiresOn { get; init; }
    [StringLength(100)]
    public string? CertificateReference { get; init; }
}
