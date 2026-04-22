using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Controllers;
using AlphaSurveilance.Data;
using AlphaSurveilance.Models;
using AlphaSurveilance.Services.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace violation_management_api.Tests
{
    public class EmailTemplatesTests
    {
        private readonly Mock<ICurrentTenantService> _currentTenantServiceMock;
        private readonly AppViolationDbContext _dbContext;
        private readonly EmailTemplatesController _controller;

        public EmailTemplatesTests()
        {
            _currentTenantServiceMock = new Mock<ICurrentTenantService>();

            var options = new DbContextOptionsBuilder<AppViolationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _dbContext = new AppViolationDbContext(options);

            _controller = new EmailTemplatesController(
                _dbContext,
                _currentTenantServiceMock.Object
            );
        }

        [Fact]
        public async Task EdgeCase1_GetTemplates_WhenTenantIdIsMissingInContext_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns((Guid?)null);

            // Act
            Func<Task> act = async () => await _controller.GetTemplates();

            // Assert
            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("User is not associated with a tenant.");
        }

        [Fact]
        public async Task EdgeCase2_GetTemplates_WhenZeroTemplatesExistForTenant_ReturnsEmptyListSuccessfully()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(tenantId);
            
            // Seed a template for a DIFFERENT tenant
            _dbContext.EmailTemplates.Add(new EmailTemplate { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Name = "Other Template" });
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _controller.GetTemplates();

            // Assert
            var actionResult = result.Value;
            actionResult.Should().NotBeNull();
            actionResult.Should().BeEmpty("Because this tenant has no templates created yet");
        }

        [Fact]
        public async Task EdgeCase3_GetTemplate_WhenIdDoesNotExist_ReturnsNotFound()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(tenantId);

            // Act
            var result = await _controller.GetTemplate(Guid.NewGuid());

            // Assert
            result.Result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task EdgeCase4_GetTemplate_CrossTenantAccess_ReturnsForbidResult()
        {
            // Arrange
            var attackerTenantId = Guid.NewGuid();
            var victimTenantId = Guid.NewGuid();
            var targetTemplateId = Guid.NewGuid();

            _dbContext.EmailTemplates.Add(new EmailTemplate { Id = targetTemplateId, TenantId = victimTenantId, Name = "Victim Template" });
            await _dbContext.SaveChangesAsync();

            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(attackerTenantId);

            // Act
            var result = await _controller.GetTemplate(targetTemplateId);

            // Assert
            result.Result.Should().BeOfType<ForbidResult>("Because Cross-Tenant data access on specific resources should be strictly blocked");
        }

        [Fact]
        public async Task EdgeCase5_CreateTemplate_WithMaliciousGuidAndTenantInjection_OverwritesBothSecurely()
        {
            // Arrange
            var activeTenantId = Guid.NewGuid();
            var maliciousTenantId = Guid.NewGuid();
            var maliciousTemplateId = Guid.NewGuid(); 
            
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(activeTenantId);

            var requestPayload = new EmailTemplate
            {
                Id = maliciousTemplateId, 
                TenantId = maliciousTenantId, // Try to assign template to another tenant
                Name = "Injected Template"
            };

            // Act
            var result = await _controller.CreateTemplate(requestPayload);
            var createdResult = result.Result as CreatedAtActionResult;
            var createdTemplate = createdResult!.Value as EmailTemplate;

            // Assert
            createdResult.Should().NotBeNull();
            createdTemplate.Should().NotBeNull();
            createdTemplate!.Id.Should().NotBe(maliciousTemplateId, "Because the system must forcefully overwrite client-provided Guids on create");
            createdTemplate.TenantId.Should().Be(activeTenantId, "Because the template must belong to the active tenant execution context");
        }

        [Fact]
        public async Task EdgeCase6_UpdateTemplate_WhenRouteIdMismatchesBodyId_ReturnsBadRequest()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(tenantId);

            var routeId = Guid.NewGuid();
            var bodyId = Guid.NewGuid();

            var updatePayload = new EmailTemplate { Id = bodyId, Name = "Updated Name" };

            // Act
            var result = await _controller.UpdateTemplate(routeId, updatePayload);

            // Assert
            result.Should().BeOfType<BadRequestResult>("Because route ID must match body ID securely");
        }

        [Fact]
        public async Task EdgeCase7_UpdateTemplate_WhenTargetRecordNotFound_ReturnsNotFound()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(tenantId);

            var missingTemplateId = Guid.NewGuid();
            var updatePayload = new EmailTemplate { Id = missingTemplateId, Name = "Ghost Template", Subject = "Hollow" };

            // Act
            var result = await _controller.UpdateTemplate(missingTemplateId, updatePayload);

            // Assert
            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task EdgeCase8_UpdateTemplate_CrossTenantModificationAttack_ReturnsForbid()
        {
            // Arrange
            var hackerTenantId = Guid.NewGuid();
            var adminTenantId = Guid.NewGuid();
            var sharedTemplateId = Guid.NewGuid();

            _dbContext.EmailTemplates.Add(new EmailTemplate 
            { 
                Id = sharedTemplateId, 
                TenantId = adminTenantId, 
                Name = "Core Template",
                Subject = "Original Subject"
            });
            await _dbContext.SaveChangesAsync();

            // Hacker logs in
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(hackerTenantId);

            var maliciousPayload = new EmailTemplate 
            { 
                Id = sharedTemplateId, 
                Name = "Hijacked Template", 
                Subject = "Hacked Subject" 
            };

            // Act
            var result = await _controller.UpdateTemplate(sharedTemplateId, maliciousPayload);

            // Assert
            result.Should().BeOfType<ForbidResult>("Because cross-tenant writes must be strictly forbidden");
            
            // Verify DB wasn't touched
            var dbRecord = await _dbContext.EmailTemplates.FindAsync(sharedTemplateId);
            dbRecord!.Subject.Should().Be("Original Subject");
        }

        [Fact]
        public async Task EdgeCase9_DeleteTemplate_WhenTargetRecordMissing_ReturnsNotFound()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(tenantId);

            var randomTemplateId = Guid.NewGuid();

            // Act
            var result = await _controller.DeleteTemplate(randomTemplateId);

            // Assert
            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task EdgeCase10_DeleteTemplate_CrossTenantDestructionAttack_ReturnsForbid()
        {
            // Arrange
            var attackerTenantId = Guid.NewGuid();
            var targetTenantId = Guid.NewGuid();
            var crucialTemplateId = Guid.NewGuid();

            _dbContext.EmailTemplates.Add(new EmailTemplate 
            { 
                Id = crucialTemplateId, 
                TenantId = targetTenantId, 
                Name = "Crucial Startup Template" 
            });
            await _dbContext.SaveChangesAsync();

            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(attackerTenantId);

            // Act
            var result = await _controller.DeleteTemplate(crucialTemplateId);

            // Assert
            result.Should().BeOfType<ForbidResult>("Because deleting cross-tenant templates is a severe violation");

            // Verify the template definitively survived the attack
            var survivors = await _dbContext.EmailTemplates.ToListAsync();
            survivors.Should().ContainSingle(t => t.Id == crucialTemplateId);
        }
    }
}
