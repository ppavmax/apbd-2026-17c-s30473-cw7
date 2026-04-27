using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClinicAppointmentsApi.DTOs;
using System.Data;

namespace ClinicAppointmentsApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;

    public AppointmentsController(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string not found");
    }

    // GET /api/appointments
    [HttpGet]
    public async Task<IActionResult> GetAppointments(
        [FromQuery] string? status = null,
        [FromQuery] string? patientLastName = null)
    {
        var appointments = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
            """, connection);

        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = 
            (object?)status ?? DBNull.Value;
        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 80).Value = 
            (object?)patientLastName ?? DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return Ok(appointments);
    }

    // GET /api/appointments/{idAppointment}
    [HttpGet("{idAppointment}")]
    public async Task<IActionResult> GetAppointment(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail,
                p.PhoneNumber AS PatientPhoneNumber,
                d.FirstName + N' ' + d.LastName AS DoctorFullName,
                d.LicenseNumber AS DoctorLicenseNumber,
                s.Name AS DoctorSpecialization
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
            """, connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            var appointment = new AppointmentDetailsDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes")) 
                    ? string.Empty 
                    : reader.GetString(reader.GetOrdinal("InternalNotes")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
                PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber")),
                DoctorFullName = reader.GetString(reader.GetOrdinal("DoctorFullName")),
                DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber")),
                DoctorSpecialization = reader.GetString(reader.GetOrdinal("DoctorSpecialization"))
            };

            return Ok(appointment);
        }

        return NotFound(new ErrorResponseDto 
        { 
            Message = $"Appointment with ID {idAppointment} not found",
            StatusCode = 404
        });
    }

    // POST /api/appointments
    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new ErrorResponseDto 
            { 
                Message = "Reason cannot be empty",
                StatusCode = 400
            });
        }

        if (request.Reason.Length > 250)
        {
            return BadRequest(new ErrorResponseDto 
            { 
                Message = "Reason cannot exceed 250 characters",
                StatusCode = 400
            });
        }
        
        if (request.AppointmentDate < DateTime.UtcNow)
        {
            return BadRequest(new ErrorResponseDto 
            { 
                Message = "Appointment date cannot be in the past",
                StatusCode = 400
            });
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var checkPatientCommand = new SqlCommand("""
            SELECT IdPatient FROM dbo.Patients 
            WHERE IdPatient = @IdPatient AND IsActive = 1;
            """, connection);
        checkPatientCommand.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;

        var patientResult = await checkPatientCommand.ExecuteScalarAsync();
        if (patientResult == null)
        {
            return BadRequest(new ErrorResponseDto 
            { 
                Message = "Patient does not exist or is not active",
                StatusCode = 400
            });
        }
        
        await using var checkDoctorCommand = new SqlCommand("""
            SELECT IdDoctor FROM dbo.Doctors 
            WHERE IdDoctor = @IdDoctor AND IsActive = 1;
            """, connection);
        checkDoctorCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;

        var doctorResult = await checkDoctorCommand.ExecuteScalarAsync();
        if (doctorResult == null)
        {
            return BadRequest(new ErrorResponseDto 
            { 
                Message = "Doctor does not exist or is not active",
                StatusCode = 400
            });
        }

        await using var checkConflictCommand = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.Appointments 
            WHERE IdDoctor = @IdDoctor 
              AND AppointmentDate = @AppointmentDate
              AND Status = N'Scheduled';
            """, connection);
        checkConflictCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        checkConflictCommand.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;

        var conflictCount = (int)await checkConflictCommand.ExecuteScalarAsync();
        if (conflictCount > 0)
        {
            return Conflict(new ErrorResponseDto 
            { 
                Message = "Doctor already has an appointment at this time",
                StatusCode = 409
            });
        }


        await using var insertCommand = new SqlCommand("""
            INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
            OUTPUT INSERTED.IdAppointment
            VALUES (@IdPatient, @IdDoctor, @AppointmentDate, N'Scheduled', @Reason);
            """, connection);

        insertCommand.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;
        insertCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        insertCommand.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        insertCommand.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = request.Reason;

        var newId = (int)await insertCommand.ExecuteScalarAsync();

        return CreatedAtAction(nameof(GetAppointment), new { idAppointment = newId }, 
            new { idAppointment = newId, message = "Appointment created successfully" });
    }

    // PUT /api/appointments/{idAppointment}
    [HttpPut("{idAppointment}")]
    public async Task<IActionResult> UpdateAppointment(
        int idAppointment, 
        [FromBody] UpdateAppointmentRequestDto request)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();


        await using var checkAppointmentCommand = new SqlCommand("""
            SELECT Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;
            """, connection);
        checkAppointmentCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        var currentStatus = await checkAppointmentCommand.ExecuteScalarAsync();
        if (currentStatus == null)
        {
            return NotFound(new ErrorResponseDto 
            { 
                Message = $"Appointment with ID {idAppointment} not found",
                StatusCode = 404
            });
        }


        var validStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
        if (!validStatuses.Contains(request.Status, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new ErrorResponseDto 
            { 
                Message = "Status must be one of: Scheduled, Completed, Cancelled",
                StatusCode = 400
            });
        }


        if (currentStatus.ToString() == "Completed" && 
            await GetAppointmentDate(connection, idAppointment) != request.AppointmentDate)
        {
            return BadRequest(new ErrorResponseDto 
            { 
                Message = "Cannot change date of a completed appointment",
                StatusCode = 400
            });
        }

        await using var checkPatientCommand = new SqlCommand("""
            SELECT IdPatient FROM dbo.Patients 
            WHERE IdPatient = @IdPatient AND IsActive = 1;
            """, connection);
        checkPatientCommand.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;

        var patientResult = await checkPatientCommand.ExecuteScalarAsync();
        if (patientResult == null)
        {
            return BadRequest(new ErrorResponseDto 
            { 
                Message = "Patient does not exist or is not active",
                StatusCode = 400
            });
        }


        await using var checkDoctorCommand = new SqlCommand("""
            SELECT IdDoctor FROM dbo.Doctors 
            WHERE IdDoctor = @IdDoctor AND IsActive = 1;
            """, connection);
        checkDoctorCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;

        var doctorResult = await checkDoctorCommand.ExecuteScalarAsync();
        if (doctorResult == null)
        {
            return BadRequest(new ErrorResponseDto 
            { 
                Message = "Doctor does not exist or is not active",
                StatusCode = 400
            });
        }

 
        var oldDate = await GetAppointmentDate(connection, idAppointment);
        if (oldDate != request.AppointmentDate)
        {
            await using var checkConflictCommand = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.Appointments 
                WHERE IdDoctor = @IdDoctor 
                  AND AppointmentDate = @AppointmentDate
                  AND Status = N'Scheduled'
                  AND IdAppointment != @IdAppointment;
                """, connection);
            checkConflictCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
            checkConflictCommand.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
            checkConflictCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

            var conflictCount = (int)await checkConflictCommand.ExecuteScalarAsync();
            if (conflictCount > 0)
            {
                return Conflict(new ErrorResponseDto 
                { 
                    Message = "Doctor already has an appointment at this time",
                    StatusCode = 409
                });
            }
        }

        // Update the appointment
        await using var updateCommand = new SqlCommand("""
            UPDATE dbo.Appointments 
            SET IdPatient = @IdPatient,
                IdDoctor = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status = @Status,
                Reason = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;
            """, connection);

        updateCommand.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;
        updateCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        updateCommand.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        updateCommand.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = request.Status;
        updateCommand.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = request.Reason;
        updateCommand.Parameters.Add("@InternalNotes", SqlDbType.NVarChar, 500).Value = 
            (object?)request.InternalNotes ?? DBNull.Value;
        updateCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await updateCommand.ExecuteNonQueryAsync();

        return Ok(new { message = "Appointment updated successfully" });
    }

    // DELETE /api/appointments/{idAppointment}
    [HttpDelete("{idAppointment}")]
    public async Task<IActionResult> DeleteAppointment(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Check if appointment exists
        await using var checkAppointmentCommand = new SqlCommand("""
            SELECT Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;
            """, connection);
        checkAppointmentCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        var statusResult = await checkAppointmentCommand.ExecuteScalarAsync();
        if (statusResult == null)
        {
            return NotFound(new ErrorResponseDto 
            { 
                Message = $"Appointment with ID {idAppointment} not found",
                StatusCode = 404
            });
        }

        // Check if appointment is completed
        if (statusResult.ToString() == "Completed")
        {
            return Conflict(new ErrorResponseDto 
            { 
                Message = "Cannot delete a completed appointment",
                StatusCode = 409
            });
        }

        // Delete the appointment
        await using var deleteCommand = new SqlCommand("""
            DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;
            """, connection);
        deleteCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await deleteCommand.ExecuteNonQueryAsync();

        return NoContent();
    }

    private async Task<DateTime> GetAppointmentDate(SqlConnection connection, int idAppointment)
    {
        await using var command = new SqlCommand("""
            SELECT AppointmentDate FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;
            """, connection);
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        return (DateTime)await command.ExecuteScalarAsync();
    }
}