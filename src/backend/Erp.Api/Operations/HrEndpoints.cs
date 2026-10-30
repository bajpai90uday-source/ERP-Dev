using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Erp.Domain.Commerce;
using Erp.Domain.Operations;
using Erp.Domain.Platform;
using Erp.Infrastructure.Persistence;
using Erp.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Operations;

internal static class HrEndpoints
{
    public static void MapHrEndpoints(this WebApplication app)
    {
        var hr = app.MapGroup("/api/v1/hr");
        hr.MapGet("/departments", async (HttpContext context, ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            var companyId = CompanyId(context);
            return Results.Ok(await db.HrDepartments.AsNoTracking()
                .Where(value => value.CompanyId == companyId)
                .OrderBy(value => value.Code)
                .Select(value => new { value.Id, value.Code, value.Name, value.IsActive })
                .ToArrayAsync(cancellationToken));
        }).RequireAuthorization(PermissionPolicies.For("hr.read"));
        hr.MapPost("/departments", async (HttpContext context, ErpDbContext db,
            HrCodeNameRequest request, CancellationToken cancellationToken) =>
        {
            if (!Valid(request, out var errors)) return Results.ValidationProblem(errors);
            var companyId = CompanyId(context);
            var code = request.Code.Trim().ToUpperInvariant();
            if (await db.HrDepartments.AnyAsync(value =>
                value.CompanyId == companyId && value.Code == code, cancellationToken))
                return Results.Conflict(new { error = "Department code already exists." });
            var department = new HrDepartment
            {
                Id = Guid.NewGuid(), CompanyId = companyId, Code = code, Name = request.Name.Trim(),
            };
            db.HrDepartments.Add(department);
            AddEvent(db, context, companyId, "hr-department", department.Id,
                "department_created", new { department.Code, department.Name });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/hr/departments/{department.Id}",
                new { department.Id, department.Code, department.Name, department.IsActive });
        }).RequireAuthorization(PermissionPolicies.For("hr.create"));

        hr.MapGet("/designations", async (HttpContext context, ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            var companyId = CompanyId(context);
            return Results.Ok(await db.HrDesignations.AsNoTracking()
                .Where(value => value.CompanyId == companyId)
                .OrderBy(value => value.Code)
                .Select(value => new { value.Id, value.Code, value.Name, value.IsActive })
                .ToArrayAsync(cancellationToken));
        }).RequireAuthorization(PermissionPolicies.For("hr.read"));
        hr.MapPost("/designations", async (HttpContext context, ErpDbContext db,
            HrCodeNameRequest request, CancellationToken cancellationToken) =>
        {
            if (!Valid(request, out var errors)) return Results.ValidationProblem(errors);
            var companyId = CompanyId(context);
            var code = request.Code.Trim().ToUpperInvariant();
            if (await db.HrDesignations.AnyAsync(value =>
                value.CompanyId == companyId && value.Code == code, cancellationToken))
                return Results.Conflict(new { error = "Designation code already exists." });
            var designation = new HrDesignation
            {
                Id = Guid.NewGuid(), CompanyId = companyId, Code = code, Name = request.Name.Trim(),
            };
            db.HrDesignations.Add(designation);
            AddEvent(db, context, companyId, "hr-designation", designation.Id,
                "designation_created", new { designation.Code, designation.Name });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/hr/designations/{designation.Id}",
                new { designation.Id, designation.Code, designation.Name, designation.IsActive });
        }).RequireAuthorization(PermissionPolicies.For("hr.create"));

        hr.MapGet("/employees", async (HttpContext context, ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            var companyId = CompanyId(context);
            return Results.Ok(await (
                from employee in db.Employees.AsNoTracking()
                join department in db.HrDepartments.AsNoTracking()
                    on new { employee.CompanyId, Id = employee.DepartmentId }
                    equals new { department.CompanyId, department.Id }
                join designation in db.HrDesignations.AsNoTracking()
                    on new { employee.CompanyId, Id = employee.DesignationId }
                    equals new { designation.CompanyId, designation.Id }
                where employee.CompanyId == companyId
                orderby employee.EmployeeNumber
                select new
                {
                    employee.Id, employee.EmployeeNumber, employee.FirstName, employee.LastName,
                    employee.DepartmentId, Department = department.Name,
                    employee.DesignationId, Designation = designation.Name,
                    employee.HireDate, employee.IsActive,
                }).ToArrayAsync(cancellationToken));
        }).RequireAuthorization(PermissionPolicies.For("hr.read"));
        hr.MapPost("/employees", async (HttpContext context, ErpDbContext db,
            EmployeeRequest request, CancellationToken cancellationToken) =>
        {
            if (!Valid(request, out var errors)) return Results.ValidationProblem(errors);
            var companyId = CompanyId(context);
            if (request.HireDate == default || request.HireDate > DateOnly.FromDateTime(DateTime.UtcNow)
                || request.BirthDate >= request.HireDate)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["hireDate"] = ["Hire date must be valid; birth date, when supplied, must precede it."],
                });
            if (!await db.HrDepartments.AnyAsync(value =>
                    value.CompanyId == companyId && value.Id == request.DepartmentId && value.IsActive,
                    cancellationToken)
                || !await db.HrDesignations.AnyAsync(value =>
                    value.CompanyId == companyId && value.Id == request.DesignationId && value.IsActive,
                    cancellationToken))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["organization"] = ["Choose an active department and designation in this company."],
                });
            var number = request.EmployeeNumber.Trim().ToUpperInvariant();
            if (await db.Employees.AnyAsync(value =>
                value.CompanyId == companyId && value.EmployeeNumber == number, cancellationToken))
                return Results.Conflict(new { error = "Employee number already exists." });
            var employee = new Employee
            {
                Id = Guid.NewGuid(), CompanyId = companyId, EmployeeNumber = number,
                FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(),
                DepartmentId = request.DepartmentId, DesignationId = request.DesignationId,
                HireDate = request.HireDate, PersonalEmail = EmptyToNull(request.PersonalEmail),
                Phone = EmptyToNull(request.Phone), BirthDate = request.BirthDate,
            };
            db.Employees.Add(employee);
            AddEvent(db, context, companyId, "employee", employee.Id,
                "employee_created", new { employee.EmployeeNumber, employee.DepartmentId, employee.DesignationId });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/hr/employees/{employee.Id}",
                new { employee.Id, employee.EmployeeNumber, employee.FirstName, employee.LastName });
        }).RequireAuthorization(PermissionPolicies.For("hr.create"));
        hr.MapGet("/employees/{id:guid}/confidential", async (Guid id, HttpContext context,
            ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var companyId = CompanyId(context);
            var employee = await db.Employees.AsNoTracking().Where(value =>
                value.CompanyId == companyId && value.Id == id)
                .Select(value => new { value.Id, value.PersonalEmail, value.Phone, value.BirthDate })
                .SingleOrDefaultAsync(cancellationToken);
            return employee is null ? Results.NotFound() : Results.Ok(employee);
        }).RequireAuthorization(PermissionPolicies.For("hr.confidential.read"));

        hr.MapGet("/shifts", async (HttpContext context, ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            var companyId = CompanyId(context);
            return Results.Ok(await db.HrShifts.AsNoTracking()
                .Where(value => value.CompanyId == companyId)
                .OrderBy(value => value.Code)
                .Select(value => new
                {
                    value.Id, value.Code, value.Name, value.StartsAt, value.EndsAt, value.IsActive,
                }).ToArrayAsync(cancellationToken));
        }).RequireAuthorization(PermissionPolicies.For("hr.read"));
        hr.MapPost("/shifts", async (HttpContext context, ErpDbContext db,
            ShiftRequest request, CancellationToken cancellationToken) =>
        {
            if (!Valid(request, out var errors)) return Results.ValidationProblem(errors);
            if (request.StartsAt == request.EndsAt)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["endsAt"] = ["A shift must have a non-zero duration."],
                });
            var companyId = CompanyId(context);
            var code = request.Code.Trim().ToUpperInvariant();
            if (await db.HrShifts.AnyAsync(value =>
                value.CompanyId == companyId && value.Code == code, cancellationToken))
                return Results.Conflict(new { error = "Shift code already exists." });
            var shift = new HrShift
            {
                Id = Guid.NewGuid(), CompanyId = companyId, Code = code,
                Name = request.Name.Trim(), StartsAt = request.StartsAt, EndsAt = request.EndsAt,
            };
            db.HrShifts.Add(shift);
            AddEvent(db, context, companyId, "hr-shift", shift.Id,
                "shift_created", new { shift.Code, shift.Name });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/hr/shifts/{shift.Id}", new { shift.Id });
        }).RequireAuthorization(PermissionPolicies.For("hr.create"));

        var attendance = app.MapGroup("/api/v1/attendance");
        attendance.MapGet("", async (HttpContext context, ErpDbContext db,
            DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        {
            var companyId = CompanyId(context);
            if (from > to) return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["dateRange"] = ["The start date must not follow the end date."] });
            var query = db.AttendanceRecords.AsNoTracking().Where(value => value.CompanyId == companyId);
            if (from.HasValue) query = query.Where(value => value.AttendanceDate >= from.Value);
            if (to.HasValue) query = query.Where(value => value.AttendanceDate <= to.Value);
            return Results.Ok(await (
                from record in query
                join employee in db.Employees.AsNoTracking()
                    on new { record.CompanyId, record.EmployeeId } equals new { employee.CompanyId, EmployeeId = employee.Id }
                join shift in db.HrShifts.AsNoTracking()
                    on new { record.CompanyId, record.ShiftId } equals new { shift.CompanyId, ShiftId = shift.Id }
                orderby record.AttendanceDate descending, employee.EmployeeNumber
                select new
                {
                    record.Id, record.EmployeeId, employee.EmployeeNumber,
                    EmployeeName = employee.FirstName + " " + employee.LastName,
                    record.ShiftId, Shift = shift.Name, record.AttendanceDate,
                    record.ClockIn, record.ClockOut, record.Status,
                }).Take(1000).ToArrayAsync(cancellationToken));
        }).RequireAuthorization(PermissionPolicies.For("attendance.read"));
        attendance.MapPost("", async (HttpContext context, ErpDbContext db,
            AttendanceRequest request, CancellationToken cancellationToken) =>
        {
            if (!Valid(request, out var errors)) return Results.ValidationProblem(errors);
            if (request.AttendanceDate == default || request.AttendanceDate > DateOnly.FromDateTime(DateTime.UtcNow)
                || request.ClockIn > request.ClockOut
                || request.Status is not ("present" or "absent" or "leave" or "holiday"))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["attendance"] = ["Provide a valid attendance date, time range, and status."],
                });
            var companyId = CompanyId(context);
            if (!await db.Employees.AnyAsync(value =>
                    value.CompanyId == companyId && value.Id == request.EmployeeId && value.IsActive,
                    cancellationToken)
                || !await db.HrShifts.AnyAsync(value =>
                    value.CompanyId == companyId && value.Id == request.ShiftId && value.IsActive,
                    cancellationToken))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["employeeId"] = ["Choose an active employee and shift in this company."],
                });
            if (await db.AttendanceRecords.AnyAsync(value =>
                value.CompanyId == companyId && value.EmployeeId == request.EmployeeId
                && value.AttendanceDate == request.AttendanceDate, cancellationToken))
                return Results.Conflict(new { error = "Attendance has already been recorded for this employee and date." });
            var record = new AttendanceRecord
            {
                Id = Guid.NewGuid(), CompanyId = companyId, EmployeeId = request.EmployeeId,
                ShiftId = request.ShiftId, AttendanceDate = request.AttendanceDate,
                ClockIn = request.ClockIn, ClockOut = request.ClockOut, Status = request.Status,
                RecordedBySubject = ActorSubject(context),
            };
            db.AttendanceRecords.Add(record);
            AddEvent(db, context, companyId, "attendance", record.Id,
                "attendance_recorded", new { record.EmployeeId, record.AttendanceDate, record.Status });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/attendance/{record.Id}", new { record.Id });
        }).RequireAuthorization(PermissionPolicies.For("attendance.create"));

        var leave = app.MapGroup("/api/v1/hr/leave");
        leave.MapGet("", async (HttpContext context, ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            var companyId = CompanyId(context);
            return Results.Ok(await db.LeaveRecords.AsNoTracking()
                .Where(value => value.CompanyId == companyId)
                .OrderByDescending(value => value.CreatedAt)
                .Take(500)
                .Select(value => new
                {
                    value.Id, value.EmployeeId, value.StartDate, value.EndDate,
                    value.LeaveType, value.Status, value.Reason, value.ReviewedBySubject,
                }).ToArrayAsync(cancellationToken));
        }).RequireAuthorization(PermissionPolicies.For("attendance.read"));
        leave.MapPost("", async (HttpContext context, ErpDbContext db,
            LeaveRequest request, CancellationToken cancellationToken) =>
        {
            if (!Valid(request, out var errors)) return Results.ValidationProblem(errors);
            if (request.StartDate == default || request.EndDate < request.StartDate
                || request.StartDate < DateOnly.FromDateTime(DateTime.UtcNow))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["dateRange"] = ["Leave dates must be valid, current or future, and ordered."],
                });
            var companyId = CompanyId(context);
            if (!await db.Employees.AnyAsync(value =>
                value.CompanyId == companyId && value.Id == request.EmployeeId && value.IsActive,
                cancellationToken))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["employeeId"] = ["Choose an active employee in this company."],
                });
            if (await db.LeaveRecords.AnyAsync(value =>
                value.CompanyId == companyId && value.EmployeeId == request.EmployeeId
                && value.Status != "rejected" && value.StartDate <= request.EndDate
                && value.EndDate >= request.StartDate, cancellationToken))
                return Results.Conflict(new { error = "The employee already has overlapping pending or approved leave." });
            var record = new LeaveRecord
            {
                Id = Guid.NewGuid(), CompanyId = companyId, EmployeeId = request.EmployeeId,
                StartDate = request.StartDate, EndDate = request.EndDate,
                LeaveType = request.LeaveType.Trim(), Reason = EmptyToNull(request.Reason),
                CreatedBySubject = ActorSubject(context),
            };
            db.LeaveRecords.Add(record);
            AddEvent(db, context, companyId, "leave-record", record.Id,
                "leave_requested", new { record.EmployeeId, record.StartDate, record.EndDate, record.LeaveType });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/hr/leave/{record.Id}", new { record.Id, record.Status });
        }).RequireAuthorization(PermissionPolicies.For("attendance.create"));
        leave.MapPost("/{id:guid}/review", async (Guid id, HttpContext context,
            ErpDbContext db, LeaveReviewRequest request, CancellationToken cancellationToken) =>
        {
            if (!Valid(request, out var errors)) return Results.ValidationProblem(errors);
            var companyId = CompanyId(context);
            var record = await db.LeaveRecords.SingleOrDefaultAsync(value =>
                value.CompanyId == companyId && value.Id == id, cancellationToken);
            if (record is null) return Results.NotFound();
            if (record.Status != "pending") return Results.Conflict(new { error = "Only pending leave can be reviewed." });
            if (!request.Approve && string.IsNullOrWhiteSpace(request.Reason))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["reason"] = ["A rejection reason is required."] });
            record.Status = request.Approve ? "approved" : "rejected";
            record.ReviewedBySubject = ActorSubject(context);
            AddEvent(db, context, companyId, "leave-record", record.Id,
                request.Approve ? "leave_approved" : "leave_rejected",
                new { record.EmployeeId, Reason = EmptyToNull(request.Reason) });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { record.Id, record.Status, record.ReviewedBySubject });
        }).RequireAuthorization(PermissionPolicies.For("attendance.approve"));

        hr.MapGet("/employees/{id:guid}/skills", async (Guid id, HttpContext context,
            ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var companyId = CompanyId(context);
            if (!await db.Employees.AnyAsync(value => value.CompanyId == companyId
                && value.Id == id, cancellationToken)) return Results.NotFound();
            return Results.Ok(await db.EmployeeSkills.AsNoTracking()
                .Where(value => value.CompanyId == companyId && value.EmployeeId == id)
                .OrderBy(value => value.Skill)
                .Select(value => new { value.Id, value.Skill, value.Proficiency, value.ValidUntil })
                .ToArrayAsync(cancellationToken));
        }).RequireAuthorization(PermissionPolicies.For("hr.read"));
        hr.MapPost("/employees/{id:guid}/skills", async (Guid id, HttpContext context,
            ErpDbContext db, EmployeeSkillRequest request, CancellationToken cancellationToken) =>
        {
            if (!Valid(request, out var errors)) return Results.ValidationProblem(errors);
            var companyId = CompanyId(context);
            if (!await db.Employees.AnyAsync(value => value.CompanyId == companyId
                && value.Id == id && value.IsActive, cancellationToken)) return Results.NotFound();
            var skillName = request.Skill.Trim();
            if (await db.EmployeeSkills.AnyAsync(value => value.CompanyId == companyId
                && value.EmployeeId == id && value.Skill == skillName, cancellationToken))
                return Results.Conflict(new { error = "This employee skill is already recorded." });
            var skill = new EmployeeSkill
            {
                Id = Guid.NewGuid(), CompanyId = companyId, EmployeeId = id,
                Skill = skillName, Proficiency = request.Proficiency.Trim().ToLowerInvariant(),
                ValidUntil = request.ValidUntil,
            };
            db.EmployeeSkills.Add(skill);
            AddEvent(db, context, companyId, "employee-skill", skill.Id,
                "employee_skill_added", new { skill.EmployeeId, skill.Skill });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/hr/employees/{id}/skills/{skill.Id}", new { skill.Id });
        }).RequireAuthorization(PermissionPolicies.For("hr.create"));

        hr.MapGet("/employees/{id:guid}/training", async (Guid id, HttpContext context,
            ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var companyId = CompanyId(context);
            if (!await db.Employees.AnyAsync(value => value.CompanyId == companyId
                && value.Id == id, cancellationToken)) return Results.NotFound();
            return Results.Ok(await db.TrainingRecords.AsNoTracking()
                .Where(value => value.CompanyId == companyId && value.EmployeeId == id)
                .OrderByDescending(value => value.CompletedOn)
                .Select(value => new
                {
                    value.Id, value.CourseName, value.CompletedOn,
                    value.ExpiresOn, value.CertificateReference,
                }).ToArrayAsync(cancellationToken));
        }).RequireAuthorization(PermissionPolicies.For("hr.read"));
        hr.MapPost("/employees/{id:guid}/training", async (Guid id, HttpContext context,
            ErpDbContext db, TrainingRequest request, CancellationToken cancellationToken) =>
        {
            if (!Valid(request, out var errors)) return Results.ValidationProblem(errors);
            if (request.CompletedOn == default || request.CompletedOn > DateOnly.FromDateTime(DateTime.UtcNow)
                || request.ExpiresOn < request.CompletedOn)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["completedOn"] = ["Training dates must be valid and expiration cannot precede completion."] });
            var companyId = CompanyId(context);
            if (!await db.Employees.AnyAsync(value => value.CompanyId == companyId
                && value.Id == id && value.IsActive, cancellationToken)) return Results.NotFound();
            var training = new TrainingRecord
            {
                Id = Guid.NewGuid(), CompanyId = companyId, EmployeeId = id,
                CourseName = request.CourseName.Trim(), CompletedOn = request.CompletedOn,
                ExpiresOn = request.ExpiresOn, CertificateReference = EmptyToNull(request.CertificateReference),
            };
            db.TrainingRecords.Add(training);
            AddEvent(db, context, companyId, "employee-training", training.Id,
                "training_recorded", new { training.EmployeeId, training.CourseName, training.CompletedOn });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/hr/employees/{id}/training/{training.Id}", new { training.Id });
        }).RequireAuthorization(PermissionPolicies.For("hr.create"));
    }

    private static bool Valid(object request, out Dictionary<string, string[]> errors)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        errors = results
            .SelectMany(value => value.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(name => (name, value.ErrorMessage ?? "The value is invalid.")))
            .GroupBy(value => value.name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(value => value.Item2).ToArray(),
                StringComparer.Ordinal);
        return errors.Count == 0;
    }

    private static void AddEvent(
        ErpDbContext db, HttpContext context, Guid companyId,
        string type, Guid id, string action, object details)
    {
        db.BusinessTransactionEvents.Add(new BusinessTransactionEvent
        {
            Id = Guid.NewGuid(), CompanyId = companyId, AggregateType = type,
            AggregateId = id, Action = action,
            ActorUserId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            ActorSubject = ActorSubject(context), CorrelationId = context.TraceIdentifier,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(details),
        });
    }

    private static Guid CompanyId(HttpContext context) =>
        Guid.Parse(context.User.FindFirstValue(SecurityClaims.CompanyId)!);

    private static string ActorSubject(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.Name) ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
