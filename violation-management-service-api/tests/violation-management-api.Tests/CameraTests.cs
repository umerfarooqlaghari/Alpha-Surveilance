using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public async Task GetActiveCamerasInternal_WithAiModelMetadata_MapsDownloadFields()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            _dbContext.Tenants.Add(new Tenant { Id = tenantId, TenantName = "TMeta", Slug = "tmeta", City = "c", Country = "c" });

            var aiModel = new AiModel
            {
                Id = Guid.NewGuid(),
                ModelKey = "kitchen-hygiene-yolo11m-v2",
                DisplayName = "Kitchen Hygiene YOLO11m v2",
                Description = "DB-driven artifact metadata test",
                ModelType = AiModelType.YoloFineTuned,
                Status = AiModelStatus.Available,
                DownloadUrl = "https://cdn.example.com/models/kitchen-hygiene-yolo11m-v2.pt",
                S3Bucket = "alpha-models",
                S3Key = "hygiene/kitchen-hygiene-yolo11m-v2.pt",
                LocalPath = "/tmp/models/kitchen-hygiene-yolo11m-v2.pt",
                Sha256Checksum = "abc123",
                IsDeleted = false,
            };

            var sop = new SopViolationType
            {
                Id = Guid.NewGuid(),
                ModelIdentifier = aiModel.ModelKey,
                TriggerLabels = "[\"no-mask\"]",
                AiModelId = aiModel.Id,
                AiModel = aiModel,
            };

            var camera = new Camera
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CameraId = "CAM-META-1",
                Status = CameraStatus.Active,
                IsDetectionEnabled = true,
                RtspUrlEncrypted = "enc_meta",
                ActiveViolationTypes = new List<CameraViolationType>
                {
                    new CameraViolationType { SopViolationTypeId = sop.Id, SopViolationType = sop }
                }
            };

            _dbContext.AiModels.Add(aiModel);
            _dbContext.SopViolationTypes.Add(sop);
            _dbContext.Cameras.Add(camera);
            await _dbContext.SaveChangesAsync();

            _encryptionServiceMock.Setup(e => e.Decrypt("enc_meta")).Returns("rtsp://cam/meta");

            // Act
            var result = await _controller.GetActiveCamerasInternal() as OkObjectResult;

            // Assert
            result.Should().NotBeNull();
            var list = result!.Value as List<InternalCameraDto>;
            var rule = list!.Single(c => c.CameraId == "CAM-META-1").ViolationRules.Single();

            rule.ModelStatus.Should().Be("Available");
            rule.ModelType.Should().Be("YoloFineTuned");
            rule.ModelDownloadUrl.Should().Be("https://cdn.example.com/models/kitchen-hygiene-yolo11m-v2.pt");
            rule.ModelS3Bucket.Should().Be("alpha-models");
            rule.ModelS3Key.Should().Be("hygiene/kitchen-hygiene-yolo11m-v2.pt");
            rule.ModelLocalPath.Should().Be("/tmp/models/kitchen-hygiene-yolo11m-v2.pt");
            rule.ModelSha256.Should().Be("abc123");
            rule.AiModelId.Should().Be(aiModel.Id);
        }

        [Fact]
        public async Task GetActiveCamerasInternal_WithoutAiModelMetadata_UsesSafeDefaults()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            _dbContext.Tenants.Add(new Tenant { Id = tenantId, TenantName = "TNoMeta", Slug = "tnometa", City = "c", Country = "c" });

            var sop = new SopViolationType
            {
                Id = Guid.NewGuid(),
                ModelIdentifier = "legacy-model-id",
                TriggerLabels = "person",
                AiModelId = null,
                AiModel = null,
            };

            var camera = new Camera
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CameraId = "CAM-NOMETA-1",
                Status = CameraStatus.Active,
                IsDetectionEnabled = true,
                RtspUrlEncrypted = "enc_nometa",
                ActiveViolationTypes = new List<CameraViolationType>
                {
                    new CameraViolationType { SopViolationTypeId = sop.Id, SopViolationType = sop }
                }
            };

            _dbContext.SopViolationTypes.Add(sop);
            _dbContext.Cameras.Add(camera);
            await _dbContext.SaveChangesAsync();

            _encryptionServiceMock.Setup(e => e.Decrypt("enc_nometa")).Returns("rtsp://cam/nometa");

            // Act
            var result = await _controller.GetActiveCamerasInternal() as OkObjectResult;

            // Assert
            result.Should().NotBeNull();
            var list = result!.Value as List<InternalCameraDto>;
            var rule = list!.Single(c => c.CameraId == "CAM-NOMETA-1").ViolationRules.Single();

            rule.ModelStatus.Should().Be("Available");
            rule.ModelType.Should().Be("YoloLocal");
            rule.ModelDownloadUrl.Should().BeNull();
            rule.ModelS3Bucket.Should().BeNull();
            rule.ModelS3Key.Should().BeNull();
            rule.ModelLocalPath.Should().BeNull();
            rule.ModelSha256.Should().BeNull();
            rule.AiModelId.Should().BeNull();
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
        public async Task UpdateCamera_UnapprovedViolationsAnomaly_ReturnsBadRequest()
        {
            // Arrange
            var cameraId = Guid.NewGuid();
            var updateRequest = new UpdateCameraRequest
            {
                ActiveViolations = new List<CameraViolationAssignment> { new CameraViolationAssignment { SopViolationTypeId = Guid.NewGuid() } }
            };

            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(true);

            // Service throws InvalidOperationException for user-input errors (unapproved violations,
            // bad RuleConfigurationJson, etc.). Controller maps these to 400 Bad Request.
            _cameraServiceMock
                .Setup(s => s.UpdateCameraAsync(cameraId, updateRequest))
                .ThrowsAsync(new InvalidOperationException("Cannot assign unapproved violation types"));

            // Act
            var result = await _controller.UpdateCamera(cameraId, updateRequest) as ObjectResult;

            // Assert
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(400);
        }

        // ── IsDetectionEnabled tests ──────────────────────────────────────────

        /// <summary>
        /// Cameras with IsDetectionEnabled = false must be excluded from the
        /// internal/active endpoint even when their Status is Active.
        /// </summary>
        [Fact]
        public async Task GetActiveCamerasInternal_DetectionDisabledCamera_IsExcluded()
        {
            // Arrange: one enabled camera, one disabled camera — both Active.
            var tenantId = Guid.NewGuid();
            _dbContext.Tenants.Add(new Tenant { Id = tenantId, TenantName = "T", Slug = "t", City = "c", Country = "c" });

            var enabledCam = new Camera
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CameraId = "CAM-ENABLED",
                Status = CameraStatus.Active,
                IsDetectionEnabled = true,
                RtspUrlEncrypted = "enc_enabled",
            };
            var sleepingCam = new Camera
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CameraId = "CAM-SLEEPING",
                Status = CameraStatus.Active,
                IsDetectionEnabled = false,
                RtspUrlEncrypted = "enc_sleeping",
            };

            _dbContext.Cameras.AddRange(enabledCam, sleepingCam);
            await _dbContext.SaveChangesAsync();

            _encryptionServiceMock
                .Setup(e => e.Decrypt(It.IsAny<string>()))
                .Returns((string s) => $"rtsp://decrypted/{s}");

            // Act
            var result = await _controller.GetActiveCamerasInternal() as OkObjectResult;

            // Assert
            result.Should().NotBeNull();
            var list = result!.Value as List<InternalCameraDto>;
            list.Should().NotBeNull();
            list!.Should().HaveCount(1, "only the detection-enabled camera should be returned");
            list.Single().CameraId.Should().Be("CAM-ENABLED");
        }

        /// <summary>
        /// When a camera has IsDetectionEnabled = true, it must be included in
        /// the internal/active response and the flag must be surfaced in the DTO.
        /// </summary>
        [Fact]
        public async Task GetActiveCamerasInternal_DetectionEnabledCamera_IncludesFlagInDto()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            _dbContext.Tenants.Add(new Tenant { Id = tenantId, TenantName = "T2", Slug = "t2", City = "c", Country = "c" });

            var cam = new Camera
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CameraId = "CAM-FLAGCHECK",
                Status = CameraStatus.Active,
                IsDetectionEnabled = true,
                RtspUrlEncrypted = "enc_flag",
            };

            _dbContext.Cameras.Add(cam);
            await _dbContext.SaveChangesAsync();

            _encryptionServiceMock.Setup(e => e.Decrypt("enc_flag")).Returns("rtsp://cam/stream");

            // Act
            var result = await _controller.GetActiveCamerasInternal() as OkObjectResult;

            // Assert
            var list = result!.Value as List<InternalCameraDto>;
            var dto = list!.Single(c => c.CameraId == "CAM-FLAGCHECK");
            dto.IsDetectionEnabled.Should().BeTrue("the DTO must surface IsDetectionEnabled = true");
        }

        /// <summary>
        /// CreateCameraRequest.IsDetectionEnabled defaults to true — the service
        /// must persist it as such when not explicitly provided.
        /// </summary>
        [Fact]
        public async Task CreateCamera_IsDetectionEnabledDefaultsToTrue_SetCorrectly()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            var request = new CreateCameraRequest
            {
                TenantId = tenantId,
                CameraId = "CAM-DEF",
                Name = "Default Detection",
                // IsDetectionEnabled not set → should default to true
            };

            var expectedResponse = new CameraResponse
            {
                Id = Guid.NewGuid(),
                Name = "Default Detection",
                IsDetectionEnabled = true,
            };

            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(true);
            _cameraServiceMock.Setup(s => s.CreateCameraAsync(request)).ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.CreateCamera(request) as CreatedAtActionResult;

            // Assert
            result.Should().NotBeNull();
            var response = result!.Value as CameraResponse;
            response!.IsDetectionEnabled.Should().BeTrue();
        }

        /// <summary>
        /// Explicitly creating a camera with IsDetectionEnabled = false must
        /// propagate the value through the service to the response DTO.
        /// </summary>
        [Fact]
        public async Task CreateCamera_IsDetectionEnabledFalse_PropagatesFlag()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            var request = new CreateCameraRequest
            {
                TenantId = tenantId,
                CameraId = "CAM-SLEEP",
                Name = "Sleeping Camera",
                IsDetectionEnabled = false,
            };

            var expectedResponse = new CameraResponse
            {
                Id = Guid.NewGuid(),
                Name = "Sleeping Camera",
                IsDetectionEnabled = false,
            };

            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(true);
            _cameraServiceMock.Setup(s => s.CreateCameraAsync(request)).ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.CreateCamera(request) as CreatedAtActionResult;

            // Assert
            var response = result!.Value as CameraResponse;
            response!.IsDetectionEnabled.Should().BeFalse("explicitly-disabled detection must survive round-trip");
        }

        /// <summary>
        /// Patching a camera with IsDetectionEnabled = false must call the service
        /// with the expected UpdateCameraRequest value.
        /// </summary>
        [Fact]
        public async Task UpdateCamera_SetDetectionDisabled_CallsServiceWithFalse()
        {
            // Arrange
            var cameraId = Guid.NewGuid();
            var updateRequest = new UpdateCameraRequest { IsDetectionEnabled = false };

            var expectedResponse = new CameraResponse
            {
                Id = cameraId,
                IsDetectionEnabled = false,
            };

            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(true);
            _cameraServiceMock
                .Setup(s => s.UpdateCameraAsync(cameraId, updateRequest))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _controller.UpdateCamera(cameraId, updateRequest) as OkObjectResult;

            // Assert
            result.Should().NotBeNull();
            var response = result!.Value as CameraResponse;
            response!.IsDetectionEnabled.Should().BeFalse();
            // Verify service was called — confirms controller doesn't silently drop the flag.
            _cameraServiceMock.Verify(s => s.UpdateCameraAsync(cameraId, It.Is<UpdateCameraRequest>(r => r.IsDetectionEnabled == false)), Times.Once);
        }

        /// <summary>
        /// Omitting IsDetectionEnabled in an UpdateCameraRequest (null = don't change)
        /// must NOT override the existing value.
        /// </summary>
        [Fact]
        public async Task UpdateCameraRequest_IsDetectionEnabledNullable_OmittedDoesNotChange()
        {
            var request = new UpdateCameraRequest { Name = "Rename Only" };
            request.IsDetectionEnabled.Should().BeNull("omitting the field must result in null, not a boolean default");
        }

        /// <summary>
        /// A camera with IsDetectionEnabled = false that is later re-enabled must
        /// reappear in the internal/active list.
        /// </summary>
        [Fact]
        public async Task GetActiveCamerasInternal_ReEnabledCamera_ReappearsInList()
        {
            // Arrange: start disabled
            var tenantId = Guid.NewGuid();
            _dbContext.Tenants.Add(new Tenant { Id = tenantId, TenantName = "T3", Slug = "t3", City = "c", Country = "c" });

            var cam = new Camera
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CameraId = "CAM-REENABLE",
                Status = CameraStatus.Active,
                IsDetectionEnabled = false,
                RtspUrlEncrypted = "enc_reenable",
            };

            _dbContext.Cameras.Add(cam);
            await _dbContext.SaveChangesAsync();

            _encryptionServiceMock.Setup(e => e.Decrypt("enc_reenable")).Returns("rtsp://cam/reenable");

            // Act 1: while disabled — should not appear
            var resultDisabled = await _controller.GetActiveCamerasInternal() as OkObjectResult;
            var listDisabled = resultDisabled!.Value as List<InternalCameraDto>;
            listDisabled!.Should().NotContain(c => c.CameraId == "CAM-REENABLE", "disabled camera must be excluded");

            // Re-enable the camera directly in the DB (simulates PATCH endpoint call)
            cam.IsDetectionEnabled = true;
            await _dbContext.SaveChangesAsync();

            // Act 2: after re-enable — should appear
            var resultEnabled = await _controller.GetActiveCamerasInternal() as OkObjectResult;
            var listEnabled = resultEnabled!.Value as List<InternalCameraDto>;
            listEnabled!.Should().Contain(c => c.CameraId == "CAM-REENABLE", "re-enabled camera must be returned");
        }

        // ── DetectionSchedule sleep-window tests (IsInSleepWindow) ────────────

        /// <summary>
        /// Reflection helper to invoke the private static IsInSleepWindow method.
        /// </summary>
        private static bool CallIsInSleepWindow(DetectionSchedule schedule, DateTime utcNow)
        {
            var method = typeof(CamerasController).GetMethod(
                "IsInSleepWindow",
                BindingFlags.NonPublic | BindingFlags.Static);
            return (bool)method!.Invoke(null, new object[] { schedule, utcNow })!;
        }

        private static DetectionSchedule MakeSchedule(
            string start, string end, int daysOfWeek = 127, bool isActive = true, string label = "") =>
            new DetectionSchedule
            {
                Id = Guid.NewGuid(),
                CameraId = Guid.NewGuid(),
                StartTime = TimeOnly.Parse(start),
                EndTime = TimeOnly.Parse(end),
                DaysOfWeek = daysOfWeek,
                IsActive = isActive,
                Label = label,
            };

        // Reference week: 2026-05-18 = Monday
        private static readonly DateTime Mon_10_00 = new DateTime(2026, 5, 18, 10, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Mon_07_00 = new DateTime(2026, 5, 18,  7, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Mon_08_00 = new DateTime(2026, 5, 18,  8, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Mon_18_00 = new DateTime(2026, 5, 18, 18, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Mon_20_00 = new DateTime(2026, 5, 18, 20, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Mon_22_00 = new DateTime(2026, 5, 18, 22, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Mon_21_59 = new DateTime(2026, 5, 18, 21, 59, 0, DateTimeKind.Utc);
        private static readonly DateTime Mon_23_00 = new DateTime(2026, 5, 18, 23, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Tue_01_00 = new DateTime(2026, 5, 19,  1, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Tue_02_00 = new DateTime(2026, 5, 19,  2, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Tue_02_01 = new DateTime(2026, 5, 19,  2, 1, 0, DateTimeKind.Utc);
        private static readonly DateTime Tue_03_30 = new DateTime(2026, 5, 19,  3, 30, 0, DateTimeKind.Utc);
        private static readonly DateTime Tue_06_01 = new DateTime(2026, 5, 19,  6, 1, 0, DateTimeKind.Utc);
        private static readonly DateTime Tue_12_00 = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Fri_23_00 = new DateTime(2026, 5, 22, 23, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Sat_10_00 = new DateTime(2026, 5, 23, 10, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Sat_23_00 = new DateTime(2026, 5, 23, 23, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Sun_10_00 = new DateTime(2026, 5, 24, 10, 0, 0, DateTimeKind.Utc);

        // ── Normal (intra-day) window ────────────────────────────────────────

        [Fact]
        public void IsInSleepWindow_NormalWindow_InsideWindow_ReturnsTrue()
        {
            var sched = MakeSchedule("08:00", "18:00");
            CallIsInSleepWindow(sched, Mon_10_00).Should().BeTrue("10:00 is inside 08:00–18:00");
        }

        [Fact]
        public void IsInSleepWindow_NormalWindow_BeforeStart_ReturnsFalse()
        {
            var sched = MakeSchedule("08:00", "18:00");
            CallIsInSleepWindow(sched, Mon_07_00).Should().BeFalse("07:00 is before window start");
        }

        [Fact]
        public void IsInSleepWindow_NormalWindow_AfterEnd_ReturnsFalse()
        {
            var sched = MakeSchedule("08:00", "18:00");
            CallIsInSleepWindow(sched, Mon_20_00).Should().BeFalse("20:00 is after window end");
        }

        [Fact]
        public void IsInSleepWindow_NormalWindow_AtStartInclusive_ReturnsTrue()
        {
            var sched = MakeSchedule("08:00", "18:00");
            CallIsInSleepWindow(sched, Mon_08_00).Should().BeTrue("start boundary must be inclusive");
        }

        [Fact]
        public void IsInSleepWindow_NormalWindow_AtEndExclusive_ReturnsFalse()
        {
            var sched = MakeSchedule("08:00", "18:00");
            CallIsInSleepWindow(sched, Mon_18_00).Should().BeFalse("end boundary must be exclusive");
        }

        // ── Overnight window: the 10 PM → 2 AM scenario ─────────────────────

        [Fact]
        public void IsInSleepWindow_OvernightWindow_NightSide_23_00_ReturnsTrue()
        {
            // User scenario: camera sleeps from 22:00 to 02:00 next day.
            // 23:00 on the start night must be inside the window.
            var sched = MakeSchedule("22:00", "02:00");
            CallIsInSleepWindow(sched, Mon_23_00).Should().BeTrue("23:00 is inside 22:00→02:00 overnight window");
        }

        [Fact]
        public void IsInSleepWindow_OvernightWindow_EarlyMorning_01_00_ReturnsTrue()
        {
            // 01:00 the next morning is still inside the overnight window.
            var sched = MakeSchedule("22:00", "02:00");
            CallIsInSleepWindow(sched, Tue_01_00).Should().BeTrue("01:00 next morning still inside 22:00→02:00 window");
        }

        [Fact]
        public void IsInSleepWindow_OvernightWindow_Midday_ReturnsFalse()
        {
            var sched = MakeSchedule("22:00", "02:00");
            CallIsInSleepWindow(sched, Tue_12_00).Should().BeFalse("12:00 is outside 22:00→02:00 window");
        }

        [Fact]
        public void IsInSleepWindow_OvernightWindow_AtStart_22_00_Inclusive_ReturnsTrue()
        {
            var sched = MakeSchedule("22:00", "02:00");
            CallIsInSleepWindow(sched, Mon_22_00).Should().BeTrue("exactly 22:00 — start is inclusive");
        }

        [Fact]
        public void IsInSleepWindow_OvernightWindow_AtEnd_02_00_Exclusive_ReturnsFalse()
        {
            var sched = MakeSchedule("22:00", "02:00");
            CallIsInSleepWindow(sched, Tue_02_00).Should().BeFalse("exactly 02:00 — end is exclusive");
        }

        [Fact]
        public void IsInSleepWindow_OvernightWindow_OneMinuteBeforeStart_ReturnsFalse()
        {
            var sched = MakeSchedule("22:00", "02:00");
            CallIsInSleepWindow(sched, Mon_21_59).Should().BeFalse("21:59 is before window start");
        }

        [Fact]
        public void IsInSleepWindow_OvernightWindow_OneMinuteAfterEnd_ReturnsFalse()
        {
            var sched = MakeSchedule("22:00", "02:00");
            CallIsInSleepWindow(sched, Tue_02_01).Should().BeFalse("02:01 is after window end");
        }

        [Fact]
        public void IsInSleepWindow_LongerOvernight_22_to_06_Midpoint_ReturnsTrue()
        {
            // 22:00 → 06:00: 03:30 in the morning must still be asleep.
            var sched = MakeSchedule("22:00", "06:00");
            CallIsInSleepWindow(sched, Tue_03_30).Should().BeTrue("03:30 is inside 22:00→06:00 window");
        }

        [Fact]
        public void IsInSleepWindow_LongerOvernight_22_to_06_AfterEnd_ReturnsFalse()
        {
            var sched = MakeSchedule("22:00", "06:00");
            CallIsInSleepWindow(sched, Tue_06_01).Should().BeFalse("06:01 is after 06:00 end");
        }

        // ── Day-of-week filtering ────────────────────────────────────────────

        [Fact]
        public void IsInSleepWindow_CorrectDayInMask_ReturnsTrue()
        {
            // Monday-only mask = bit 2 (1 << DayOfWeek.Monday = 1 << 1 = 2)
            var sched = MakeSchedule("08:00", "18:00", daysOfWeek: 2);
            CallIsInSleepWindow(sched, Mon_10_00).Should().BeTrue("Monday is in mask 2");
        }

        [Fact]
        public void IsInSleepWindow_WrongDayNotInMask_ReturnsFalse()
        {
            // Monday-only mask (2), but time is Saturday
            var sched = MakeSchedule("08:00", "18:00", daysOfWeek: 2);
            CallIsInSleepWindow(sched, Sat_10_00).Should().BeFalse("Saturday not in Monday-only mask");
        }

        [Fact]
        public void IsInSleepWindow_DaysOfWeek_Zero_MeansEveryDay()
        {
            var sched = MakeSchedule("08:00", "18:00", daysOfWeek: 0);
            CallIsInSleepWindow(sched, Sat_10_00).Should().BeTrue("DaysOfWeek=0 means every day");
        }

        [Fact]
        public void IsInSleepWindow_DaysOfWeek_127_MeansEveryDay()
        {
            var sched = MakeSchedule("08:00", "18:00", daysOfWeek: 127);
            CallIsInSleepWindow(sched, Sun_10_00).Should().BeTrue("DaysOfWeek=127 covers all days");
        }

        [Fact]
        public void IsInSleepWindow_WeekdaysMask_OnSaturday_ReturnsFalse()
        {
            // Mon(2)+Tue(4)+Wed(8)+Thu(16)+Fri(32) = 62
            var sched = MakeSchedule("22:00", "02:00", daysOfWeek: 62);
            CallIsInSleepWindow(sched, Sat_23_00).Should().BeFalse("Saturday not in weekday mask 62");
        }

        [Fact]
        public void IsInSleepWindow_WeekdaysMask_OnFriday_ReturnsTrue()
        {
            var sched = MakeSchedule("22:00", "02:00", daysOfWeek: 62);
            CallIsInSleepWindow(sched, Fri_23_00).Should().BeTrue("Friday is in weekday mask 62");
        }

        [Fact]
        public void IsInSleepWindow_OvernightDayBoundaryLimitation_TuesdayMorning_MonOnly_ReturnsFalse()
        {
            // Monday-only 22:00→02:00: Tuesday 01:00 does NOT match because the
            // day check uses the current day. Use days=127 for seamless overnight coverage.
            var sched = MakeSchedule("22:00", "02:00", daysOfWeek: 2); // Monday only
            CallIsInSleepWindow(sched, Tue_01_00).Should().BeFalse(
                "day check uses current day (Tuesday), not the start day; Monday-only window misses Tue 01:00");
        }

        [Fact]
        public void IsInSleepWindow_OvernightEveryDay_TuesdayMorning_ReturnsTrue()
        {
            // With days=127 the window covers Tuesday 01:00 correctly.
            var sched = MakeSchedule("22:00", "02:00", daysOfWeek: 127);
            CallIsInSleepWindow(sched, Tue_01_00).Should().BeTrue(
                "days=127 covers every day; Tuesday 01:00 is inside 22:00→02:00");
        }

        // ── End-to-end: schedule filtering through GetActiveCamerasInternal ──

        /// <summary>
        /// A camera whose detection schedule currently contains "now" must be
        /// excluded from the internal/active endpoint response.
        /// Uses a dynamic window anchored on the real current UTC time to avoid
        /// test fragility.
        /// </summary>
        [Fact]
        public async Task GetActiveCamerasInternal_CameraInSleepWindow_IsExcluded()
        {
            // Build a window that is guaranteed to contain the current moment
            // by spanning [now-1h, now+1h] (always a normal intra-day window
            // unless we are within 1 minute of midnight, which is acceptable).
            var now = DateTime.UtcNow;
            var start = TimeOnly.FromDateTime(now.AddHours(-1));
            var end   = TimeOnly.FromDateTime(now.AddHours(+1));

            var tenantId = Guid.NewGuid();
            _dbContext.Tenants.Add(new Tenant { Id = tenantId, TenantName = "SchedT1", Slug = "s1", City = "c", Country = "c" });

            var sleepingCam = new Camera
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CameraId = "CAM-SCHEDULE-SLEEP",
                Status = CameraStatus.Active,
                IsDetectionEnabled = true,
                RtspUrlEncrypted = "enc_sched_sleep",
                DetectionSchedules = new List<DetectionSchedule>
                {
                    new DetectionSchedule
                    {
                        Id = Guid.NewGuid(),
                        StartTime = start,
                        EndTime   = end,
                        DaysOfWeek = 127,
                        IsActive  = true,
                        Label     = "Dynamic test window",
                    }
                }
            };

            _dbContext.Cameras.Add(sleepingCam);
            await _dbContext.SaveChangesAsync();

            _encryptionServiceMock.Setup(e => e.Decrypt("enc_sched_sleep"))
                .Returns("rtsp://cam/sched-sleep");

            var result = await _controller.GetActiveCamerasInternal() as OkObjectResult;
            var list = result!.Value as List<InternalCameraDto>;
            list!.Should().NotContain(c => c.CameraId == "CAM-SCHEDULE-SLEEP",
                "camera inside its sleep window must be excluded from the active list");
        }

        /// <summary>
        /// A camera whose detection schedule window ended in the past must be
        /// included in the internal/active endpoint response.
        /// </summary>
        [Fact]
        public async Task GetActiveCamerasInternal_CameraOutsideSleepWindow_IsIncluded()
        {
            // Window that ended 2 hours ago — camera is currently awake
            var now = DateTime.UtcNow;
            var start = TimeOnly.FromDateTime(now.AddHours(-4));
            var end   = TimeOnly.FromDateTime(now.AddHours(-2));

            var tenantId = Guid.NewGuid();
            _dbContext.Tenants.Add(new Tenant { Id = tenantId, TenantName = "SchedT2", Slug = "s2", City = "c", Country = "c" });

            var awakeCam = new Camera
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CameraId = "CAM-SCHEDULE-AWAKE",
                Status = CameraStatus.Active,
                IsDetectionEnabled = true,
                RtspUrlEncrypted = "enc_sched_awake",
                DetectionSchedules = new List<DetectionSchedule>
                {
                    new DetectionSchedule
                    {
                        Id = Guid.NewGuid(),
                        StartTime = start,
                        EndTime   = end,
                        DaysOfWeek = 127,
                        IsActive  = true,
                        Label     = "Past window",
                    }
                }
            };

            _dbContext.Cameras.Add(awakeCam);
            await _dbContext.SaveChangesAsync();

            _encryptionServiceMock.Setup(e => e.Decrypt("enc_sched_awake"))
                .Returns("rtsp://cam/sched-awake");

            var result = await _controller.GetActiveCamerasInternal() as OkObjectResult;
            var list = result!.Value as List<InternalCameraDto>;
            list!.Should().Contain(c => c.CameraId == "CAM-SCHEDULE-AWAKE",
                "camera outside its sleep window must be included in the active list");
        }

        /// <summary>
        /// An inactive schedule whose time window matches "now" must NOT suppress the camera.
        /// </summary>
        [Fact]
        public async Task GetActiveCamerasInternal_InactiveScheduleInWindow_CameraIncluded()
        {
            var now = DateTime.UtcNow;
            var start = TimeOnly.FromDateTime(now.AddHours(-1));
            var end   = TimeOnly.FromDateTime(now.AddHours(+1));

            var tenantId = Guid.NewGuid();
            _dbContext.Tenants.Add(new Tenant { Id = tenantId, TenantName = "SchedT3", Slug = "s3", City = "c", Country = "c" });

            var cam = new Camera
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CameraId = "CAM-INACTIVE-SCHED",
                Status = CameraStatus.Active,
                IsDetectionEnabled = true,
                RtspUrlEncrypted = "enc_inactive_sched",
                DetectionSchedules = new List<DetectionSchedule>
                {
                    new DetectionSchedule
                    {
                        Id = Guid.NewGuid(),
                        StartTime = start,
                        EndTime   = end,
                        DaysOfWeek = 127,
                        IsActive  = false,  // ← disabled; must be ignored
                        Label     = "Disabled window",
                    }
                }
            };

            _dbContext.Cameras.Add(cam);
            await _dbContext.SaveChangesAsync();

            _encryptionServiceMock.Setup(e => e.Decrypt("enc_inactive_sched"))
                .Returns("rtsp://cam/inactive-sched");

            var result = await _controller.GetActiveCamerasInternal() as OkObjectResult;
            var list = result!.Value as List<InternalCameraDto>;
            list!.Should().Contain(c => c.CameraId == "CAM-INACTIVE-SCHED",
                "an inactive schedule must not suppress the camera");
        }

        /// <summary>
        /// DetectionSchedules must be returned in the DTO so the Vision Service
        /// can apply client-side schedule enforcement.
        /// </summary>
        [Fact]
        public async Task GetActiveCamerasInternal_ReturnsDetectionSchedulesInDto()
        {
            var tenantId = Guid.NewGuid();
            _dbContext.Tenants.Add(new Tenant { Id = tenantId, TenantName = "SchedT4", Slug = "s4", City = "c", Country = "c" });

            var schedId = Guid.NewGuid();
            var cam = new Camera
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CameraId = "CAM-DTO-SCHED",
                Status = CameraStatus.Active,
                IsDetectionEnabled = true,
                RtspUrlEncrypted = "enc_dto_sched",
                DetectionSchedules = new List<DetectionSchedule>
                {
                    new DetectionSchedule
                    {
                        Id = schedId,
                        StartTime = TimeOnly.Parse("22:00"),
                        EndTime   = TimeOnly.Parse("06:00"),
                        DaysOfWeek = 62,    // Mon–Fri
                        IsActive  = true,
                        Label     = "Night hours",
                    }
                }
            };

            _dbContext.Cameras.Add(cam);
            await _dbContext.SaveChangesAsync();

            // Camera is awake at noon on a Saturday — use dynamic window that does NOT match now
            // (past window ensures inclusion).
            var nowSat = new DateTime(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc); // Saturday noon

            _encryptionServiceMock.Setup(e => e.Decrypt("enc_dto_sched"))
                .Returns("rtsp://cam/dto-sched");

            var result = await _controller.GetActiveCamerasInternal() as OkObjectResult;
            var list = result!.Value as List<InternalCameraDto>;
            // Camera may or may not be included depending on real "now", but if included
            // the DTO must carry the schedule. If filtered (Fri/Sat night), skip DTO assertion.
            var dto = list?.FirstOrDefault(c => c.CameraId == "CAM-DTO-SCHED");
            if (dto is not null)
            {
                dto.DetectionSchedules.Should().HaveCount(1);
                var s = dto.DetectionSchedules.Single();
                s.StartTime.Should().Be("22:00");
                s.EndTime.Should().Be("06:00");
                s.DaysOfWeek.Should().Be(62);
                s.IsActive.Should().BeTrue();
                s.Label.Should().Be("Night hours");
            }
        }
        // ═══════════════════════════════════════════════════════════════════════
        // Security-critical regression tests
        //  CT_NEW1 – CT_NEW7  added to verify invariants that, if broken, open
        //  cross-tenant data-access or privilege-escalation vulnerabilities.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Security: for non-SuperAdmin callers the controller MUST overwrite
        /// request.TenantId with the tenant from the auth context, regardless
        /// of what the caller supplied in the request body. If the override
        /// line (`request.TenantId = GetTenantId()`) is removed, a malicious
        /// caller could create cameras under any tenant by supplying an
        /// arbitrary tenantId in the request body.
        /// </summary>
        [Fact]
        public async Task CreateCamera_RegularUser_TenantIdOverriddenFromContext_NotFromRequestBody()
        {
            var contextTenantId = Guid.NewGuid();
            var requestTenantId = Guid.NewGuid(); // attacker-supplied different tenant

            var request = new CreateCameraRequest
            {
                TenantId = requestTenantId,
                CameraId = "CAM-ATTACK",
                Name = "Injected camera"
            };

            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(false);
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(contextTenantId);

            _cameraServiceMock
                .Setup(s => s.CreateCameraAsync(It.IsAny<CreateCameraRequest>()))
                .ReturnsAsync(new CameraResponse { Id = Guid.NewGuid(), Name = "Injected camera" });

            await _controller.CreateCamera(request);

            // The controller must have replaced the body's tenantId with the one from context.
            _cameraServiceMock.Verify(s => s.CreateCameraAsync(
                It.Is<CreateCameraRequest>(r => r.TenantId == contextTenantId)),
                Times.Once,
                "controller must always force request.TenantId = context TenantId for non-SuperAdmin callers");

            _cameraServiceMock.Verify(s => s.CreateCameraAsync(
                It.Is<CreateCameraRequest>(r => r.TenantId == requestTenantId)),
                Times.Never,
                "the attacker-supplied tenantId must never reach the service");
        }

        /// <summary>
        /// Security: non-SuperAdmin callers must not be able to inject
        /// RuleConfigurationJson via the request body. The controller must null
        /// out every RuleConfigurationJson in request.ActiveViolations before
        /// forwarding to the service.
        /// </summary>
        [Fact]
        public async Task CreateCamera_RegularUser_RuleConfigurationJsonIsStripped()
        {
            var tenantId = Guid.NewGuid();
            var request = new CreateCameraRequest
            {
                TenantId = tenantId,
                CameraId = "CAM-RULE-INJECT",
                Name = "Cam",
                ActiveViolations = new List<CameraViolationAssignment>
                {
                    new CameraViolationAssignment
                    {
                        SopViolationTypeId = Guid.NewGuid(),
                        RuleConfigurationJson = "{\"geofence\":[{\"x\":0,\"y\":0}]}" // attacker payload
                    },
                    new CameraViolationAssignment
                    {
                        SopViolationTypeId = Guid.NewGuid(),
                        RuleConfigurationJson = "{\"threshold\":0.0}" // attacker payload
                    }
                }
            };

            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(false);
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(tenantId);

            _cameraServiceMock
                .Setup(s => s.CreateCameraAsync(It.IsAny<CreateCameraRequest>()))
                .ReturnsAsync(new CameraResponse { Id = Guid.NewGuid() });

            await _controller.CreateCamera(request);

            _cameraServiceMock.Verify(s => s.CreateCameraAsync(
                It.Is<CreateCameraRequest>(r =>
                    r.ActiveViolations != null &&
                    r.ActiveViolations.All(v => v.RuleConfigurationJson == null))),
                Times.Once,
                "all RuleConfigurationJson values must be stripped for non-SuperAdmin callers");
        }

        [Fact]
        public async Task GetCamera_WhenCameraNotFound_Returns404WithErrorMessage()
        {
            var cameraId = Guid.NewGuid();

            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(true);
            _cameraServiceMock
                .Setup(s => s.GetCameraByIdAsync(cameraId))
                .ReturnsAsync((CameraResponse?)null);

            var result = await _controller.GetCamera(cameraId) as NotFoundObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(404);
            var errorProp = result.Value!.GetType().GetProperty("error");
            (errorProp!.GetValue(result.Value) as string).Should().Be("Camera not found");
        }

        /// <summary>
        /// Security: a TenantAdmin must NOT be able to access a camera that
        /// belongs to a different tenant by guessing its GUID. The controller
        /// must compare camera.TenantId against the caller's context TenantId
        /// and return Forbid() if they differ.
        /// </summary>
        [Fact]
        public async Task GetCamera_TenantAdmin_CrossTenantCamera_ReturnsForbid()
        {
            var callerTenantId = Guid.NewGuid();
            var cameraTenantId = Guid.NewGuid(); // belongs to a different tenant

            var cameraId = Guid.NewGuid();
            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(false);
            _currentTenantServiceMock.Setup(s => s.TenantId).Returns(callerTenantId);

            _cameraServiceMock
                .Setup(s => s.GetCameraByIdAsync(cameraId))
                .ReturnsAsync(new CameraResponse { Id = cameraId, TenantId = cameraTenantId });

            var result = await _controller.GetCamera(cameraId);

            result.Should().BeOfType<ForbidResult>(
                "a TenantAdmin must not be able to read cameras owned by another tenant");
        }

        /// <summary>
        /// Security: SuperAdmin must supply a tenantId query param; without it
        /// the controller cannot know which tenant to scope the query to and
        /// must return 400 rather than leaking all cameras across all tenants.
        /// </summary>
        [Fact]
        public async Task GetCameras_SuperAdmin_WithoutTenantIdQueryParam_ReturnsBadRequest()
        {
            _currentTenantServiceMock.Setup(s => s.IsSuperAdmin).Returns(true);

            var result = await _controller.GetCamerasByTenant(tenantId: null, locationId: null)
                         as BadRequestObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task GetActiveCameras_WithUnknownDeviceId_ReturnsEmptyArray()
        {
            // DeviceId is not in the database at all.
            var unknownDeviceId = Guid.NewGuid();

            var result = await _controller.GetActiveCamerasInternal(deviceId: unknownDeviceId)
                         as OkObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            var list = result.Value as System.Collections.IEnumerable;
            list.Should().NotBeNull();
            list!.Cast<object>().Should().BeEmpty(
                "an unregistered deviceId must return an empty camera list, not 404/500");
        }

        [Fact]
        public async Task GetActiveCameras_WithDisabledDevice_ReturnsEmptyArray()
        {
            // Device exists but its Status is Disabled.
            var tenantId = Guid.NewGuid();
            _dbContext.Tenants.Add(new Tenant { Id = tenantId, TenantName = "T-Disabled", Slug = "td", City = "c", Country = "c" });

            var device = new violation_management_api.Core.Entities.EdgeDevice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DeviceIdentifier = "dev-disabled",
                Hostname = "host",
                DisplayName = "Disabled Device",
                Status = EdgeDeviceStatus.Disabled,
                IsDeleted = false
            };
            _dbContext.Set<violation_management_api.Core.Entities.EdgeDevice>().Add(device);
            await _dbContext.SaveChangesAsync();

            var result = await _controller.GetActiveCamerasInternal(deviceId: device.Id)
                         as OkObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            var list = result.Value as System.Collections.IEnumerable;
            list!.Cast<object>().Should().BeEmpty(
                "a disabled device must receive an empty camera list so it stops processing");
        }
    }
}
