using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Data;
using AlphaSurveilance.Services.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using violation_management_api.Controllers;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;
using Xunit;

namespace violation_management_api.Tests
{
    public class CameraTests
    {
        private readonly Mock<ICameraService> _cameraServiceMock;
        private readonly Mock<ILogger<CamerasController>> _loggerMock;
        private readonly Mock<ICurrentTenantService> _currentTenantServiceMock;
        private readonly Mock<IEncryptionService> _encryptionServiceMock;
        private readonly AppViolationDbContext _dbContext;
        private readonly CamerasController _controller;

        public CameraTests()
        {
            _cameraServiceMock = new Mock<ICameraService>();
            _loggerMock = new Mock<ILogger<CamerasController>>();
            _currentTenantServiceMock = new Mock<ICurrentTenantService>();
            _encryptionServiceMock = new Mock<IEncryptionService>();

            var options = new DbContextOptionsBuilder<AppViolationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            
            _dbContext = new AppViolationDbContext(options);

            _controller = new CamerasController(
                _cameraServiceMock.Object,
                _loggerMock.Object,
                _currentTenantServiceMock.Object,
                _dbContext,
                _encryptionServiceMock.Object
            );
        }

        [Fact]
        public async Task CreateCamera_ValidRequest_ReturnsCreatedCamera()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            var request = new CreateCameraRequest
            {
                TenantId = tenantId,
                CameraId = "CAM-1",
                Name = "Lobby",
                IsStreaming = true
            };

            var expectedResponse = new CameraResponse { Id = Guid.NewGuid(), Name = "Lobby", CameraId = "CAM-1" };

            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(false);
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(tenantId);

            _cameraServiceMock.Setup(s => s.CreateCameraAsync(request)).ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.CreateCamera(request) as CreatedAtActionResult;

            // Assert
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(201);
            result.Value.Should().BeEquivalentTo(expectedResponse);
        }

        [Fact]
        public async Task CreateCamera_DuplicateIdAnomaly_ReturnsBadRequest()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            var request = new CreateCameraRequest { TenantId = tenantId, CameraId = "CAM-DUP", Name = "Lobby Room 1" };

            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(false);
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(tenantId);

            // Simulated from CameraService.CreateCameraAsync
            _cameraServiceMock
                .Setup(s => s.CreateCameraAsync(request))
                .ThrowsAsync(new InvalidOperationException("Camera with ID 'CAM-DUP' already exists"));

            // Act
            var result = await _controller.CreateCamera(request) as BadRequestObjectResult;

            // Assert
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(400);
            
            // Extract the anonymous type value
            var errorProperty = result.Value.GetType().GetProperty("error");
            var errorValue = errorProperty.GetValue(result.Value, null) as string;
            errorValue.Should().Contain("already exists");
        }

        [Fact]
        public async Task CreateCamera_MissingSuperAdminTenant_ReturnsBadRequest()
        {
            // Arrange
            var request = new CreateCameraRequest { TenantId = Guid.Empty, CameraId = "CAM-3" };

            // Super Admin making the request without specifying target TenantId
            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(true);

            // Act
            var result = await _controller.CreateCamera(request) as BadRequestObjectResult;

            // Assert
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task GetActiveCamerasInternal_WithMultipleViolationsModelsAndLabels_ReturnsAggregatedData()
        {
            // Arrange (Seeding InMemory DB precisely to reflect DB relationships)
            var tenantId = Guid.NewGuid();
            var tenant = new violation_management_api.Core.Entities.Tenant { Id = tenantId, TenantName = "T", Slug = "t", City = "t", Country = "t" };
            _dbContext.Tenants.Add(tenant);
            
            // Seed 2 distinct SOP Violation Types to act as our distinct underlying AI Models
            var sop1 = new violation_management_api.Core.Entities.SopViolationType { Id = Guid.NewGuid(), ModelIdentifier = "human-detection", TriggerLabels = "person" };
            var sop2 = new violation_management_api.Core.Entities.SopViolationType { Id = Guid.NewGuid(), ModelIdentifier = "ppe-detection", TriggerLabels = "helmet,vest" };
            
            _dbContext.SopViolationTypes.AddRange(sop1, sop2);

            var camera = new Camera
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CameraId = "MULTI-VIOLATION-CAM-1",
                Status = CameraStatus.Active,
                RtspUrlEncrypted = "valid_encryption",
                // Associating multiple violations to this single camera
                ActiveViolationTypes = new List<CameraViolationType>
                {
                    new CameraViolationType { SopViolationTypeId = sop1.Id, TriggerLabels = "person" }, // Overriding labels optional
                    new CameraViolationType { SopViolationTypeId = sop2.Id, TriggerLabels = "helmet,vest,gloves" } // Demonstrating Camera-level Override
                }
            };
            
            _dbContext.Cameras.Add(camera);
            await _dbContext.SaveChangesAsync();

            _encryptionServiceMock.Setup(e => e.Decrypt(It.IsAny<string>())).Returns("rtsp://internal/valid");

            // Act
            var result = await _controller.GetActiveCamerasInternal() as OkObjectResult;

            // Assert
            result.Should().NotBeNull();
            var camerasList = result!.Value as List<InternalCameraDto>;
            camerasList.Should().NotBeNull();
            camerasList.Should().HaveCount(1);
            
            var internalCam = camerasList!.First();
            internalCam.CameraId.Should().Be("MULTI-VIOLATION-CAM-1");
            internalCam.ViolationRules.Should().HaveCount(2);

            // Assert Multi-Model Association Mapping
            var model1 = internalCam.ViolationRules.First(v => v.SopViolationTypeId == sop1.Id);
            model1.ModelIdentifier.Should().Be("human-detection");
            model1.TriggerLabels.Should().Be("person");

            // Assert custom Camera-level Override takes precedence natively
            var model2 = internalCam.ViolationRules.First(v => v.SopViolationTypeId == sop2.Id);
            model2.ModelIdentifier.Should().Be("ppe-detection");
            model2.TriggerLabels.Should().Be("helmet,vest,gloves");
        }

        [Fact]
        public async Task GetActiveCamerasInternal_CorruptCryptoAnomaly_ExcludesRtspOrReturnsEmptyString()
        {
            // Arrange
            var camera = new Camera
            {
                Id = Guid.NewGuid(),
                CameraId = "BAD-CRYPTO-CAM",
                Status = CameraStatus.Active,
                RtspUrlEncrypted = "gibberish_garbage_token"
            };
            
            _dbContext.Cameras.Add(camera);
            await _dbContext.SaveChangesAsync();

            // Setup encryption service to throw exception when faced with garbage data
            _encryptionServiceMock
                .Setup(e => e.Decrypt("gibberish_garbage_token"))
                .Throws(new System.Security.Cryptography.CryptographicException("Padding is invalid"));

            // Act
            var result = await _controller.GetActiveCamerasInternal() as OkObjectResult;

            // Assert
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);

            var camerasList = result.Value as List<InternalCameraDto>;
            // Because the RtspUrl returns as empty string on crash, the internal API filters it via `.Where(c => !string.IsNullOrEmpty(c.RtspUrl))`
            camerasList.Should().HaveCount(0, "A camera with bad RTSP data should be safely bypassed gracefully rather than crashing the loop.");
        }
        [Fact]
        public async Task UpdateCamera_ValidRequest_UpdatesAndReturnsCamera()
        {
            // Arrange
            var cameraId = Guid.NewGuid();
            var updateRequest = new UpdateCameraRequest
            {
                Name = "Updated Lobby",
                IsStreaming = false
            };

            var expectedResponse = new CameraResponse { Id = cameraId, Name = "Updated Lobby", IsStreaming = false };

            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(true); // Bypass ownership check for test ease
            
            _cameraServiceMock
                .Setup(s => s.UpdateCameraAsync(cameraId, updateRequest))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.UpdateCamera(cameraId, updateRequest) as OkObjectResult;

            // Assert
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            result.Value.Should().BeEquivalentTo(expectedResponse);
        }

        [Fact]
        public async Task UpdateCamera_UnapprovedViolationsAnomaly_ReturnsStatusCode500()
        {
            // Arrange
            var cameraId = Guid.NewGuid();
            var updateRequest = new UpdateCameraRequest
            {
                ActiveViolations = new List<CameraViolationAssignment> { new CameraViolationAssignment { SopViolationTypeId = Guid.NewGuid() } }
            };

            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(true);

            // Service throws exception when applying unapproved violation to an existing camera
            _cameraServiceMock
                .Setup(s => s.UpdateCameraAsync(cameraId, updateRequest))
                .ThrowsAsync(new InvalidOperationException("Cannot assign unapproved violation types"));

            // Act
            var result = await _controller.UpdateCamera(cameraId, updateRequest) as ObjectResult;

            // Assert
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(500); // Controller catches general Exceptions as 500 error in UpdateCamera
        }
    }
}
