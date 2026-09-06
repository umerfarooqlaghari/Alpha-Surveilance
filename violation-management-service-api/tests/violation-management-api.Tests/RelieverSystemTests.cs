using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using AlphaSurveilance.Data;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;
using AlphaSurveilance.Services.Interfaces;
using violation_management_api.Services.Interfaces;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.Services;
using Xunit;

namespace violation_management_api.Tests
{
    public class RelieverSystemTests
    {
        private AppViolationDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppViolationDbContext>()
                .UseInMemoryDatabase(databaseName: $"RelieverTestDb_{Guid.NewGuid()}")
                .Options;
            return new AppViolationDbContext(options);
        }

        [Fact]
        public void RuleConfigurationValidator_Accepts_Valid_Reliever_Rule()
        {
            var json = """
            {
                "type": "reliever",
                "polygon": [[0.1, 0.1], [0.5, 0.1], [0.5, 0.5], [0.1, 0.5]],
                "coordinate_space": "normalized",
                "handover_threshold_s": 45,
                "max_relief_duration_s": 600,
                "required_primaries": 2
            }
            """;

            var normalized = RuleConfigurationValidator.ValidateAndNormalize(json);
            normalized.Should().NotBeNull();
            normalized.Should().Contain("reliever");
        }

        [Fact]
        public void RuleConfigurationValidator_Rejects_Invalid_Reliever_Negative_Threshold()
        {
            var json = """
            {
                "type": "reliever",
                "polygon": [[0.1, 0.1], [0.5, 0.1], [0.5, 0.5], [0.1, 0.5]],
                "handover_threshold_s": -10
            }
            """;

            Action act = () => RuleConfigurationValidator.ValidateAndNormalize(json);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*handover_threshold_s*");
        }

        [Fact]
        public async Task WorkstationService_Creates_And_Retrieves_Workstation_Successfully()
        {
            using var db = CreateInMemoryDbContext();
            var tenantId = Guid.NewGuid();
            var cameraId = Guid.NewGuid();

            db.Cameras.Add(new Camera
            {
                Id = cameraId,
                TenantId = tenantId,
                CameraId = "CAM-LINE-01",
                Name = "Line 1 Main Camera",
                Status = CameraStatus.Active
            });
            await db.SaveChangesAsync();

            var service = new WorkstationService(db, NullLogger<WorkstationService>.Instance);

            var createReq = new CreateWorkstationRequest
            {
                Name = "Assembly Workstation A",
                Code = "WS-ASM-A",
                CameraId = cameraId,
                PolygonJson = "[[0.2, 0.2], [0.8, 0.2], [0.8, 0.8], [0.2, 0.8]]",
                RequiredPrimaryWorkers = 2,
                MaxRelievers = 1,
                HandoverThresholdSeconds = 90,
                MaxReliefDurationSeconds = 1200
            };

            var created = await service.CreateWorkstationAsync(tenantId, createReq);
            created.Should().NotBeNull();
            created.Code.Should().Be("WS-ASM-A");
            created.RequiredPrimaryWorkers.Should().Be(2);

            var retrieved = await service.GetWorkstationByIdAsync(tenantId, created.Id);
            retrieved.Should().NotBeNull();
            retrieved!.Name.Should().Be("Assembly Workstation A");
            retrieved.HandoverThresholdSeconds.Should().Be(90);
        }

        [Fact]
        public async Task ReliefTrackingService_Processes_Full_Handover_Lifecycle()
        {
            using var db = CreateInMemoryDbContext();
            var tenantId = Guid.NewGuid();
            var cameraId = Guid.NewGuid();
            var workstationId = Guid.NewGuid();

            db.Cameras.Add(new Camera
            {
                Id = cameraId,
                TenantId = tenantId,
                CameraId = "CAM-PKG-01",
                Name = "Packaging Camera",
                Status = CameraStatus.Active
            });

            var primaryEmp = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId.ToString(),
                FirstName = "John",
                LastName = "Doe",
                Email = "john@example.com",
                EmployeeId = "EMP-001"
            };
            var relieverEmp = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId.ToString(),
                FirstName = "Sarah",
                LastName = "Smith",
                Email = "sarah@example.com",
                EmployeeId = "REL-001"
            };
            db.Employees.AddRange(primaryEmp, relieverEmp);

            var workstation = new Workstation
            {
                Id = workstationId,
                TenantId = tenantId,
                CameraId = cameraId,
                Name = "Packaging Station 1",
                Code = "WS-PKG-01",
                HandoverThresholdSeconds = 60,
                CurrentStatus = WorkstationStatus.Staffed
            };
            db.Workstations.Add(workstation);
            await db.SaveChangesAsync();

            var trackingService = new ReliefTrackingService(db, NullLogger<ReliefTrackingService>.Instance);

            // Step 1: Primary worker exits station
            var exitTime = DateTime.UtcNow.AddMinutes(-5);
            var exitEvent = new ReliefEventIngestRequest
            {
                TenantId = tenantId,
                WorkstationId = workstationId,
                CameraId = "CAM-PKG-01",
                EventType = "PRIMARY_EXIT",
                EmployeeExternalId = "EMP-001",
                Role = "Primary",
                Timestamp = exitTime
            };

            var exitResp = await trackingService.ProcessReliefEventAsync(exitEvent);
            exitResp.Should().NotBeNull();
            exitResp!.Status.Should().Be(ReliefSessionStatus.PendingHandover);

            var updatedWs1 = await db.Workstations.FindAsync(workstationId);
            updatedWs1!.CurrentStatus.Should().Be(WorkstationStatus.PendingHandover);

            // Step 2: Reliever arrives 20 seconds later
            var arriveTime = exitTime.AddSeconds(20);
            var arriveEvent = new ReliefEventIngestRequest
            {
                TenantId = tenantId,
                WorkstationId = workstationId,
                CameraId = "CAM-PKG-01",
                EventType = "RELIEVER_ENTER",
                EmployeeExternalId = "REL-001",
                Role = "Reliever",
                Timestamp = arriveTime
            };

            var arriveResp = await trackingService.ProcessReliefEventAsync(arriveEvent);
            arriveResp.Should().NotBeNull();
            arriveResp!.Status.Should().Be(ReliefSessionStatus.ActiveRelief);
            arriveResp.HandoverLatencySeconds.Should().Be(20);

            var updatedWs2 = await db.Workstations.FindAsync(workstationId);
            updatedWs2!.CurrentStatus.Should().Be(WorkstationStatus.UnderRelief);

            // Step 3: Primary returns 10 minutes later
            var returnTime = arriveTime.AddMinutes(10);
            var returnEvent = new ReliefEventIngestRequest
            {
                TenantId = tenantId,
                WorkstationId = workstationId,
                CameraId = "CAM-PKG-01",
                EventType = "PRIMARY_RETURN",
                EmployeeExternalId = "EMP-001",
                Role = "Primary",
                Timestamp = returnTime
            };

            var returnResp = await trackingService.ProcessReliefEventAsync(returnEvent);
            returnResp.Should().NotBeNull();
            returnResp!.Status.Should().Be(ReliefSessionStatus.Completed);
            returnResp.ReliefDurationSeconds.Should().Be(600); // 10 mins

            var updatedWs3 = await db.Workstations.FindAsync(workstationId);
            updatedWs3!.CurrentStatus.Should().Be(WorkstationStatus.Staffed);
        }

        [Fact]
        public async Task ReliefAnalyticsService_Calculates_Accurate_Summary_Metrics()
        {
            using var db = CreateInMemoryDbContext();
            var tenantId = Guid.NewGuid();
            var cameraId = Guid.NewGuid();
            var wsId = Guid.NewGuid();

            var ws = new Workstation
            {
                Id = wsId,
                TenantId = tenantId,
                CameraId = cameraId,
                Name = "Inspection Station 1",
                Code = "WS-INSP-01",
                IsActive = true
            };
            db.Workstations.Add(ws);

            var relieverId = Guid.NewGuid();
            var reliever = new Employee
            {
                Id = relieverId,
                TenantId = tenantId.ToString(),
                FirstName = "Maria",
                LastName = "Santos",
                Email = "maria@example.com",
                EmployeeId = "REL-999"
            };
            db.Employees.Add(reliever);

            // Add 2 completed relief sessions
            db.ReliefSessions.AddRange(
                new ReliefSession
                {
                    TenantId = tenantId,
                    WorkstationId = wsId,
                    CameraId = cameraId,
                    RelieverEmployeeId = relieverId,
                    RelieverEmployeeExternalId = "REL-999",
                    PrimaryLeftAt = DateTime.UtcNow.AddHours(-2),
                    RelieverArrivedAt = DateTime.UtcNow.AddHours(-2).AddSeconds(30),
                    HandoverLatencySeconds = 30,
                    ReliefEndedAt = DateTime.UtcNow.AddHours(-1).AddMinutes(-45),
                    ReliefDurationSeconds = 900,
                    Status = ReliefSessionStatus.Completed
                },
                new ReliefSession
                {
                    TenantId = tenantId,
                    WorkstationId = wsId,
                    CameraId = cameraId,
                    RelieverEmployeeId = relieverId,
                    RelieverEmployeeExternalId = "REL-999",
                    PrimaryLeftAt = DateTime.UtcNow.AddHours(-1),
                    RelieverArrivedAt = DateTime.UtcNow.AddHours(-1).AddSeconds(40),
                    HandoverLatencySeconds = 40,
                    ReliefEndedAt = DateTime.UtcNow.AddMinutes(-40),
                    ReliefDurationSeconds = 1200,
                    Status = ReliefSessionStatus.Completed
                }
            );
            await db.SaveChangesAsync();

            var analyticsService = new ReliefAnalyticsService(db, NullLogger<ReliefAnalyticsService>.Instance);
            var summary = await analyticsService.GetAnalyticsSummaryAsync(tenantId);

            summary.Should().NotBeNull();
            summary.TotalWorkstations.Should().Be(1);
            summary.TotalReliefEventsCount.Should().Be(2);
            summary.AverageHandoverLatencySeconds.Should().Be(35.0);
            summary.TopRelievers.Should().HaveCount(1);
            summary.TopRelievers[0].EmployeeName.Should().Be("Maria Santos");
            summary.TopRelievers[0].ReliefCount.Should().Be(2);
        }

        [Fact]
        public async Task GetActiveCamerasInternal_Returns_Both_Sop_And_Workstation_Rules()
        {
            using var db = CreateInMemoryDbContext();
            var tenantId = Guid.NewGuid();
            var cameraId = Guid.NewGuid();
            var workstationId = Guid.NewGuid();
            var sopViolationTypeId = Guid.NewGuid();

            db.Tenants.Add(new Tenant { Id = tenantId, TenantName = "Factory Tenant", Slug = "factory", City = "Metropolis", Country = "USA" });

            var sopType = new SopViolationType
            {
                Id = sopViolationTypeId,
                Name = "Hairnet Required",
                ModelIdentifier = "restaurant-ppe-v1",
                TriggerLabels = "no-hairnet"
            };
            db.SopViolationTypes.Add(sopType);

            var camera = new Camera
            {
                Id = cameraId,
                TenantId = tenantId,
                CameraId = "CAM-LINE-1",
                Name = "Line 1 Main View",
                Status = CameraStatus.Active,
                IsDetectionEnabled = true,
                RtspUrlEncrypted = "enc_url",
                ActiveViolationTypes = new List<CameraViolationType>
                {
                    new CameraViolationType
                    {
                        CameraId = cameraId,
                        SopViolationTypeId = sopViolationTypeId,
                        SopViolationType = sopType,
                        TriggerLabels = "no-hairnet",
                        RuleConfigurationJson = """{"type":"geofence","polygon":[[0,0],[1,0],[1,1],[0,1]]}"""
                    }
                }
            };
            db.Cameras.Add(camera);

            var workstation = new Workstation
            {
                Id = workstationId,
                TenantId = tenantId,
                CameraId = cameraId,
                Name = "Packaging Station 1",
                Code = "WS-PKG-01",
                PolygonJson = "[[0.2, 0.2], [0.8, 0.2], [0.8, 0.8], [0.2, 0.8]]",
                RequiredPrimaryWorkers = 1,
                HandoverThresholdSeconds = 60,
                MaxReliefDurationSeconds = 900,
                IsActive = true
            };
            db.Workstations.Add(workstation);
            await db.SaveChangesAsync();

            var encMock = new Moq.Mock<IEncryptionService>();
            encMock.Setup(e => e.Decrypt(Moq.It.IsAny<string>())).Returns("rtsp://mock-stream");

            var controller = new Controllers.CamerasController(
                Moq.Mock.Of<ICameraService>(),
                NullLogger<Controllers.CamerasController>.Instance,
                Moq.Mock.Of<ICurrentTenantService>(),
                db,
                encMock.Object
            );

            var actionResult = await controller.GetActiveCamerasInternal() as Microsoft.AspNetCore.Mvc.OkObjectResult;

            actionResult.Should().NotBeNull();
            var list = actionResult!.Value as List<DTOs.Responses.InternalCameraDto>;
            list.Should().NotBeNull();
            list!.Should().HaveCount(1);

            var camDto = list.Single();
            camDto.ViolationRules.Should().HaveCount(2, "camera must output both the SOP rule and the Workstation Reliever rule");

            var ppeRule = camDto.ViolationRules.Single(r => r.ModelIdentifier == "restaurant-ppe-v1");
            ppeRule.TriggerLabels.Should().Be("no-hairnet");

            var relieverRule = camDto.ViolationRules.Single(r => r.ModelIdentifier == "human-detection-v1");
            relieverRule.TriggerLabels.Should().Be("person");
            relieverRule.RuleConfigurationJson.Should().Contain("reliever");
            relieverRule.RuleConfigurationJson.Should().Contain(workstationId.ToString());
        }

        [Fact]
        public void ValidateOperatingSchedule_ValidNonOverlappingWindows_Succeeds()
        {
            var json = @"[
                {""id"":""w1"",""label"":""Day Shift"",""startTime"":""08:00"",""endTime"":""16:00"",""daysOfWeek"":[1,2,3,4,5],""isActive"":true},
                {""id"":""w2"",""label"":""Night Shift"",""startTime"":""16:00"",""endTime"":""00:00"",""daysOfWeek"":[1,2,3,4,5],""isActive"":true}
            ]";

            var act = () => RuleConfigurationValidator.ValidateOperatingSchedule(json);
            act.Should().NotThrow();
        }

        [Fact]
        public void ValidateOperatingSchedule_OverlappingDaytimeWindows_ThrowsInvalidOperationException()
        {
            var json = @"[
                {""id"":""w1"",""label"":""Morning Shift"",""startTime"":""08:00"",""endTime"":""14:00"",""daysOfWeek"":[1,2,3],""isActive"":true},
                {""id"":""w2"",""label"":""Midday Shift"",""startTime"":""13:00"",""endTime"":""18:00"",""daysOfWeek"":[2,4],""isActive"":true}
            ]";

            var act = () => RuleConfigurationValidator.ValidateOperatingSchedule(json);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*overlaps*");
        }

        [Fact]
        public void ValidateOperatingSchedule_OvernightWindowOverlap_ThrowsInvalidOperationException()
        {
            var json = @"[
                {""id"":""w1"",""label"":""Overnight Shift"",""startTime"":""22:00"",""endTime"":""06:00"",""daysOfWeek"":[1],""isActive"":true},
                {""id"":""w2"",""label"":""Early Morning Shift"",""startTime"":""05:00"",""endTime"":""09:00"",""daysOfWeek"":[1],""isActive"":true}
            ]";

            var act = () => RuleConfigurationValidator.ValidateOperatingSchedule(json);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*overlaps*");
        }

        [Fact]
        public async Task CamerasController_WithOperatingSchedule_SerializesScheduleInRuleConfig()
        {
            var tenantId = Guid.NewGuid();
            var cameraId = Guid.NewGuid();
            var workstationId = Guid.NewGuid();

            var schedJson = @"[{""id"":""w1"",""label"":""Day Shift"",""startTime"":""08:00"",""endTime"":""16:00"",""daysOfWeek"":[1,2,3,4,5],""isActive"":true}]";

            using var db = CreateInMemoryDbContext();
            db.Tenants.Add(new Tenant { Id = tenantId, TenantName = "Sched Tenant", Slug = "sched-tenant", City = "Metropolis", Country = "USA" });
            db.Cameras.Add(new Camera
            {
                Id = cameraId,
                TenantId = tenantId,
                CameraId = "CAM-SCHED-01",
                Name = "Sched Cam",
                RtspUrlEncrypted = "encrypted-rtsp-sched",
                Status = CameraStatus.Active,
                IsDetectionEnabled = true,
                CreatedAt = DateTime.UtcNow
            });

            db.Workstations.Add(new Workstation
            {
                Id = workstationId,
                TenantId = tenantId,
                CameraId = cameraId,
                Name = "Assembly Station 4",
                Code = "WS-04",
                PolygonJson = "[[0.1, 0.1], [0.9, 0.1], [0.9, 0.9], [0.1, 0.9]]",
                OperatingScheduleJson = schedJson,
                HandoverThresholdSeconds = 60,
                MaxReliefDurationSeconds = 900,
                RequiredPrimaryWorkers = 1,
                IsActive = true
            });
            await db.SaveChangesAsync();

            var encMock = new Moq.Mock<IEncryptionService>();
            encMock.Setup(e => e.Decrypt(Moq.It.IsAny<string>())).Returns("rtsp://sched-feed");

            var controller = new Controllers.CamerasController(
                Moq.Mock.Of<ICameraService>(),
                NullLogger<Controllers.CamerasController>.Instance,
                Moq.Mock.Of<ICurrentTenantService>(),
                db,
                encMock.Object
            );

            var actionResult = await controller.GetActiveCamerasInternal() as Microsoft.AspNetCore.Mvc.OkObjectResult;
            actionResult.Should().NotBeNull();
            var list = actionResult!.Value as List<DTOs.Responses.InternalCameraDto>;
            list.Should().NotBeNull();
            var cam = list!.Single(c => c.Id == cameraId);

            var wsRule = cam.ViolationRules.Single(r => r.SopViolationTypeId == workstationId);
            wsRule.RuleConfigurationJson.Should().Contain("operating_schedules");
            wsRule.RuleConfigurationJson.Should().Contain("Day Shift");
            wsRule.RuleConfigurationJson.Should().Contain("08:00");
        }
    }
}
