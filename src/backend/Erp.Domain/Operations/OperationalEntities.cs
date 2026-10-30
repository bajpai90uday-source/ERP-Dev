namespace Erp.Domain.Operations;

public static class DispatchStatuses
{
    public const string Draft = "draft";
    public const string Authorized = "authorized";
    public const string Dispatched = "dispatched";
}

public sealed class DispatchShipment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public Guid SalesOrderId { get; set; }
    public Guid SalesOrderRevisionId { get; set; }
    public string Status { get; set; } = DispatchStatuses.Draft;
    public string CreatedBySubject { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? AuthorizedBySubject { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public string? DispatchedBySubject { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
}

public sealed class DispatchShipmentLine
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DispatchShipmentId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public Guid ProductVariantId { get; set; }
    public Guid ItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class DispatchCarton
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DispatchShipmentId { get; set; }
    public string CartonNumber { get; set; } = string.Empty;
    public decimal Weight { get; set; }
}

public sealed class DispatchCartonLine
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DispatchCartonId { get; set; }
    public Guid DispatchShipmentLineId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class HrDepartment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class HrDesignation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class Employee
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public Guid DesignationId { get; set; }
    public DateOnly HireDate { get; set; }
    public string? PersonalEmail { get; set; }
    public string? Phone { get; set; }
    public DateOnly? BirthDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HrShift
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartsAt { get; set; }
    public TimeOnly EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AttendanceRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ShiftId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public DateTimeOffset? ClockIn { get; set; }
    public DateTimeOffset? ClockOut { get; set; }
    public string Status { get; set; } = "present";
    public string RecordedBySubject { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LeaveRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string LeaveType { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? Reason { get; set; }
    public string CreatedBySubject { get; set; } = string.Empty;
    public string? ReviewedBySubject { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class EmployeeSkill
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Skill { get; set; } = string.Empty;
    public string Proficiency { get; set; } = string.Empty;
    public DateOnly? ValidUntil { get; set; }
}

public sealed class TrainingRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public string CourseName { get; set; } = string.Empty;
    public DateOnly CompletedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? CertificateReference { get; set; }
}

public sealed class InAppNotification
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? RecipientUserId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}

public sealed class NotificationReadReceipt
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ReadAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NotificationDeliveryLog
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid NotificationId { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public int AttemptCount { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
