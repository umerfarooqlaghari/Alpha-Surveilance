using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.DTOs;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.DTOs.Responses;
using AlphaSurveilance.Extensions;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text.Json;
using AlphaSurveilance.Services.Interfaces;

namespace AlphaSurveilance.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EmployeesController(
        AppViolationDbContext context, 
        ILogger<EmployeesController> logger,
        ICurrentTenantService currentTenantService) : ControllerBase
    {
        private Guid GetTenantId()
        {
            var tenantId = currentTenantService.TenantId;
            if (!tenantId.HasValue)
            {
                throw new UnauthorizedAccessException("User is not associated with a tenant.");
            }
            return tenantId.Value;
        }

        [HttpPost("bulk-import")]
        public async Task<ActionResult<BulkImportResponse>> BulkImport([FromForm] IFormFile file)
        {
            var tenantId = GetTenantId();
            
            if (file == null || file.Length == 0) 
            {
                logger.LogWarning("BulkImport: No file uploaded.");
                return BadRequest("No file uploaded.");
            }

            logger.LogInformation("Processing Bulk Import for Tenant {TenantId}. File: {FileName}, Size: {Size}", tenantId, file.FileName, file.Length);

            if (!file.FileName.EndsWith(".csv")) return BadRequest("Only CSV files are allowed.");

            var response = new BulkImportResponse();
            var validEmployees = new List<Employee>();
            var rowIndex = 0;

            try 
            {
                using (var reader = new StreamReader(file.OpenReadStream()))
                using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HeaderValidated = null,
                    MissingFieldFound = null,
                    PrepareHeaderForMatch = args => args.Header.ToLower(), // Optional help
                }))
                {
                    // dynamic reading to handle extra columns
                    var records = csv.GetRecords<dynamic>();

                    foreach (var record in records)
                    {
                        rowIndex++;
                        var recordDict = (IDictionary<string, object>)record;
                        
                        // Basic Validation with Case-Insensitivity
                        var email = GetValue(recordDict, "email");
                        if (string.IsNullOrWhiteSpace(email))
                        {
                            response.Failures.Add(new BulkImportFailure { RowIndex = rowIndex, Reason = "Missing Email" });
                            response.FailureCount++;
                            continue;
                        }

                        var employeeId = GetValue(recordDict, "employeeId");
                        if (string.IsNullOrWhiteSpace(employeeId))
                        {
                            employeeId = Guid.NewGuid().ToString().Substring(0, 8);
                        }

                        // Duplicate Check (Memory check for batch, DB check later)
                        if (validEmployees.Any(e => e.Email == email || e.EmployeeId == employeeId))
                        {
                             response.Failures.Add(new BulkImportFailure { RowIndex = rowIndex, Email = email, Reason = "Duplicate in file" });
                             response.FailureCount++;
                             continue;
                        }

                        // Map Standard Fields
                        var employee = new Employee
                        {
                            TenantId = tenantId.ToString(), // Storing as string in DB for now based on current schema
                            Email = email,
                            EmployeeId = employeeId,
                            FirstName = GetValue(recordDict, "firstName"),
                            LastName = GetValue(recordDict, "lastName"),
                            Number = GetValue(recordDict, "number"),
                            CompanyName = GetValue(recordDict, "companyName"),
                            Designation = GetValue(recordDict, "designation"),
                            Department = GetValue(recordDict, "department"),
                            Tenure = GetValue(recordDict, "tenure"),
                            Grade = GetValue(recordDict, "grade"),
                            Gender = GetValue(recordDict, "gender"),
                            ManagerId = GetValue(recordDict, "managerId"),
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        // Map Metadata (Everything else)
                        var metadata = new Dictionary<string, object>();
                        var standardKeys = new[] { "firstName", "lastName", "email", "employeeId", "number", "companyName", "designation", "department", "tenure", "grade", "gender", "managerId" };
                        
                        foreach (var key in recordDict.Keys)
                        {
                            if (!standardKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                            {
                                metadata[key] = recordDict[key];
                            }
                        }
                        employee.MetadataJson = JsonSerializer.Serialize(metadata);

                        validEmployees.Add(employee);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error parsing CSV file");
                return BadRequest($"Failed to parse CSV: {ex.Message}");
            }

            logger.LogInformation("Parsed {Count} valid employees from CSV.", validEmployees.Count);

            // DB Duplicate Check
            var emails = validEmployees.Select(e => e.Email).ToList();
            var existingEmails = await context.Employees
                .Where(e => e.TenantId == tenantId.ToString() && emails.Contains(e.Email))
                .Select(e => e.Email)
                .ToListAsync();

            var finalEmployees = validEmployees.Where(e => !existingEmails.Contains(e.Email)).ToList();
            var duplicateCount = validEmployees.Count - finalEmployees.Count;
            
            if (duplicateCount > 0)
            {
                response.FailureCount += duplicateCount;
                // Ideally add specific failures for duplicates
            }

            if (finalEmployees.Any())
            {
                await context.Employees.AddRangeAsync(finalEmployees);
                await context.SaveChangesAsync();
            }

            response.TotalProcessed = rowIndex;
            response.SuccessCount = finalEmployees.Count;

            return Ok(response);
        }

        [HttpPost]
        public async Task<ActionResult<EmployeeResponse>> Create([FromBody] EmployeeRequest request)
        {
            var tenantId = GetTenantId().ToString();

            // Check for existing
            var existing = await context.Employees.AnyAsync(e => e.TenantId == tenantId && (e.Email == request.Email || e.EmployeeId == request.EmployeeId));
            if (existing) return Conflict("Employee with this Email or ID already exists.");

            var employee = new Employee
            {
                TenantId = tenantId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            employee.UpdateFromRequest(request);

            await context.Employees.AddAsync(employee);
            await context.SaveChangesAsync();

            return CreatedAtAction(nameof(Get), new { id = employee.Id }, employee.ToResponse());
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<EmployeeResponse>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null,
            [FromQuery] string? department = null,
            [FromQuery] string? designation = null)
        {
            var tenantId = GetTenantId().ToString();

            var query = context.Employees.Where(e => e.TenantId == tenantId).AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();
                query = query.Where(e => 
                    e.FirstName.ToLower().Contains(search) || 
                    e.LastName.ToLower().Contains(search) || 
                    e.Email.ToLower().Contains(search) ||
                    e.EmployeeId.ToLower().Contains(search));
            }

            if (!string.IsNullOrEmpty(department))
                query = query.Where(e => e.Department == department);

            if (!string.IsNullOrEmpty(designation))
                query = query.Where(e => e.Designation == designation);

            var total = await query.CountAsync();
            var employees = await query
                .OrderByDescending(e => e.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            Response.Headers.Append("X-Total-Count", total.ToString());
            
            return Ok(employees.Select(e => e.ToResponse()));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<EmployeeResponse>> Get(Guid id)
        {
            var tenantId = GetTenantId().ToString();
            var employee = await context.Employees.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
            if (employee == null) return NotFound();
            return Ok(employee.ToResponse());
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] EmployeeRequest request)
        {
            var tenantId = GetTenantId().ToString();
            var employee = await context.Employees.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
            if (employee == null) return NotFound();

            employee.UpdateFromRequest(request);
            await context.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var tenantId = GetTenantId().ToString();
            var employee = await context.Employees.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
            if (employee == null) return NotFound();

            context.Employees.Remove(employee);
            await context.SaveChangesAsync();
            return NoContent();
        }

        private string GetValue(IDictionary<string, object> dict, string key)
        {
            // Case-insensitive lookup
            var match = dict.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            return match != null && dict[match] != null ? dict[match]?.ToString() ?? string.Empty : string.Empty;
        }
    }
}
