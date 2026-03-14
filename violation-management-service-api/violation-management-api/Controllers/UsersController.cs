using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using violation_management_api.DTOs.Requests;
using violation_management_api.Services.Interfaces;
using AlphaSurveilance.Services.Interfaces;

namespace violation_management_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController(
        IUserService userService, 
        ILogger<UsersController> logger,
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

        /// <summary>
        /// Create a new user (Tenant Admin)
        /// </summary>
        [HttpPost]
        [Authorize(Policy = "SuperAdmin")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            try
            {
                var user = await userService.CreateUserAsync(request);
                return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating user");
                return StatusCode(500, new { error = "An error occurred while creating the user" });
            }
        }

        /// <summary>
        /// Get all users filtered by tenant (optional)
        /// </summary>
        [HttpGet]
        [Authorize(Policy = "SuperOrTenantAdmin")]
        public async Task<IActionResult> GetUsers([FromQuery] Guid? tenantId)
        {
            try
            {
                Guid? targetTenantId = tenantId;

                if (!currentTenantService.IsSuperAdmin)
                {
                    targetTenantId = GetTenantId();
                }

                var users = await userService.GetUsersByTenantAsync(targetTenantId);
                return Ok(users);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching users");
                return StatusCode(500, new { error = "An error occurred while fetching users" });
            }
        }

        /// <summary>
        /// Get user by ID
        /// </summary>
        [HttpGet("{id}")]
        [Authorize(Policy = "SuperOrTenantAdmin")]
        public async Task<IActionResult> GetUser(Guid id)
        {
            try
            {
                var user = await userService.GetUserByIdAsync(id);
                if (user == null)
                    return NotFound(new { error = "User not found" });

                // Enforce tenant isolation
                if (!currentTenantService.IsSuperAdmin)
                {
                    var tenantId = GetTenantId();
                    if (user.TenantId != tenantId) return Forbid();
                }

                return Ok(user);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching user {UserId}", id);
                return StatusCode(500, new { error = "An error occurred while fetching the user" });
            }
        }

        /// <summary>
        /// Update user
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Policy = "SuperOrTenantAdmin")]
        public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request)
        {
            try
            {
                // Verify ownership/permission (Optional, could be in service)
                 if (!currentTenantService.IsSuperAdmin)
                {
                    var existing = await userService.GetUserByIdAsync(id);
                    if (existing == null) return NotFound();
                    if (existing.TenantId != GetTenantId()) return Forbid();
                }

                var user = await userService.UpdateUserAsync(id, request);
                if (user == null)
                    return NotFound(new { error = "User not found" });

                return Ok(user);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating user {UserId}", id);
                return StatusCode(500, new { error = "An error occurred while updating the user" });
            }
        }

        /// <summary>
        /// Reset user password
        /// </summary>
        [HttpPost("{id}/reset-password")]
        [Authorize(Policy = "SuperAdmin")]
        public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request)
        {
            try
            {
                var result = await userService.ResetPasswordAsync(id, request.NewPassword);
                if (!result)
                    return NotFound(new { error = "User not found" });

                return Ok(new { message = "Password reset successfully" });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error resetting password for user {UserId}", id);
                return StatusCode(500, new { error = "An error occurred while resetting the password" });
            }
        }

        /// <summary>
        /// Toggle user active status
        /// </summary>
        [HttpPatch("{id}/toggle-status")]
        [Authorize(Policy = "SuperAdmin")]
        public async Task<IActionResult> ToggleStatus(Guid id)
        {
            try
            {
                var result = await userService.ToggleUserStatusAsync(id);
                if (!result)
                    return NotFound(new { error = "User not found" });

                return Ok(new { message = "User status toggled successfully" });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error toggling status for user {UserId}", id);
                return StatusCode(500, new { error = "An error occurred while toggling user status" });
            }
        }

        /// <summary>
        /// Soft delete user
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Policy = "SuperAdmin")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            try
            {
                var result = await userService.DeleteUserAsync(id);
                if (!result)
                    return NotFound(new { error = "User not found" });

                return NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting user {UserId}", id);
                return StatusCode(500, new { error = "An error occurred while deleting the user" });
            }
        }
    }
}
