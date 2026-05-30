using System;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.Services;
using Xunit;

namespace violation_management_api.Tests
{
    /// <summary>
    /// Unit tests for EdgeDeviceService — direct service layer (no controller).
    /// Uses EF Core InMemory so every test gets an isolated, pre-seeded database.
    ///
    /// Coverage targets
    /// ────────────────
    ///  Registration  : idempotency, unknown tenant, empty identifier, hostname refresh
    ///  Heartbeat     : updates LastSeenAt, graceful on missing/deleted device
    ///  CRUD          : create, duplicate identifier guard, cross-tenant location guard
    ///  Assignment    : camera↔device link, cross-tenant guard, double-unassign
    ///  Delete        : soft-delete + cascade unassign to shared pool (DeviceId = NULL)
    ///  Backward-compat: new cameras default DeviceId = NULL
    ///  Response shape: CameraResponse carries DeviceId + DeviceName
    /// </summary>
    public class EdgeDeviceTests
    {
        // ── helpers ──────────────────────────────────────────────────────────

        private AppViolationDbContext BuildDb() =>
            new(new DbContextOptionsBuilder<AppViolationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        private EdgeDeviceService BuildService(AppViolationDbContext db) =>
            new(db, Mock.Of<ILogger<EdgeDeviceService>>());

        /// <summary>Seed a minimal Tenant row and return its Id.</summary>
        private static Guid SeedTenant(AppViolationDbContext db, string name = "Acme Corp")
        {
            var id = Guid.NewGuid();
            db.Tenants.Add(new Tenant
            {
                Id = id,
                TenantName = name,
                Slug = name.ToLower().Replace(" ", "-") + "-" + id.ToString()[..4],
                City = "Karachi",
                Country = "PK",
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
            return id;
        }

        /// <summary>Seed a Camera (without DeviceId) and return its Id.</summary>
        private static Guid SeedCamera(AppViolationDbContext db, Guid tenantId,
            string cameraSlug = "CAM-01", Guid? locationId = null)
        {
            var id = Guid.NewGuid();
            db.Cameras.Add(new Camera
            {
                Id = id,
                TenantId = tenantId,
                LocationId = locationId,
                CameraId = cameraSlug,
                Name = cameraSlug,
                RtspUrlEncrypted = "enc://test",
                Status = CameraStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
            return id;
        }

        // ════════════════════════════════════════════════════════════════════
        // Registration
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Register_NewDevice_CreatesRowAndReturnsIsNewTrue()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);

            var (resp, isNew) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "uuid-aaa-bbb",
                Hostname = "edge-box-1",
                DisplayName = "Kitchen Device"
            });

            isNew.Should().BeTrue();
            resp.TenantId.Should().Be(tenantId);
            resp.DeviceIdentifier.Should().Be("uuid-aaa-bbb");
            resp.Hostname.Should().Be("edge-box-1");
            resp.DisplayName.Should().Be("Kitchen Device");
            resp.LastSeenAt.Should().NotBeNull();
            db.EdgeDevices.Should().ContainSingle();
        }

        [Fact]
        public async Task Register_SameIdentifierSameTenant_IsIdempotentAndReturnsIsNewFalse()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);
            var req = new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "stable-id-001",
                Hostname = "host-a"
            };

            await svc.RegisterAsync(req);
            var (resp, isNew) = await svc.RegisterAsync(req);

            isNew.Should().BeFalse();
            db.EdgeDevices.Count().Should().Be(1, "second call must not create a duplicate row");
            resp.DeviceIdentifier.Should().Be("stable-id-001");
        }

        [Fact]
        public async Task Register_SameIdentifierDifferentTenant_CreatesSeparateDevice()
        {
            // Two tenants with the same device identifier string are independent devices.
            var db = BuildDb();
            var tenant1 = SeedTenant(db, "Alpha Corp");
            var tenant2 = SeedTenant(db, "Beta Corp");
            var svc = BuildService(db);
            var id = "shared-hardware-id";

            await svc.RegisterAsync(new RegisterDeviceRequest { TenantId = tenant1, DeviceIdentifier = id });
            await svc.RegisterAsync(new RegisterDeviceRequest { TenantId = tenant2, DeviceIdentifier = id });

            db.EdgeDevices.Count().Should().Be(2);
        }

        [Fact]
        public async Task Register_DeviceIdentifierMatchingExistingDeviceId_ReattachesToThatDevice()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var existingDeviceId = Guid.NewGuid();
            db.EdgeDevices.Add(new EdgeDevice
            {
                Id = existingDeviceId,
                TenantId = tenantId,
                DeviceIdentifier = "UmersMac",
                DisplayName = "Umers Mac",
                Hostname = "Umer",
                Status = EdgeDeviceStatus.Active,
                RegisteredAt = DateTime.UtcNow.AddMinutes(-10),
                LastSeenAt = DateTime.UtcNow.AddMinutes(-10)
            });
            db.EdgeDevices.Add(new EdgeDevice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DeviceIdentifier = existingDeviceId.ToString(),
                DisplayName = "Conflicting Identifier Row",
                Hostname = "other-host",
                Status = EdgeDeviceStatus.Active,
                RegisteredAt = DateTime.UtcNow.AddMinutes(-5),
                LastSeenAt = DateTime.UtcNow.AddMinutes(-5)
            });
            await db.SaveChangesAsync();

            var svc = BuildService(db);

            var (resp, isNew) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = existingDeviceId.ToString(),
                Hostname = "Macbooks-MacBook-Pro.local"
            });

            isNew.Should().BeFalse();
            resp.Id.Should().Be(existingDeviceId);
            resp.DeviceIdentifier.Should().Be("UmersMac");
            db.EdgeDevices.Count().Should().Be(2);
            db.EdgeDevices.Single(d => d.Id == existingDeviceId).Hostname.Should().Be("Macbooks-MacBook-Pro.local");
        }

        [Fact]
        public async Task Register_EmptyTenantId_Throws()
        {
            var svc = BuildService(BuildDb());

            await svc.Invoking(s => s.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = Guid.Empty,
                DeviceIdentifier = "x"
            }))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*TenantId*");
        }

        [Fact]
        public async Task Register_WhitespaceIdentifier_Throws()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);

            await svc.Invoking(s => s.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "   "
            }))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*DeviceIdentifier*");
        }

        [Fact]
        public async Task Register_UnknownTenantId_Throws()
        {
            // The DB has no Tenant row — service must detect this and throw, NOT silently create.
            var svc = BuildService(BuildDb());

            await svc.Invoking(s => s.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = Guid.NewGuid(),
                DeviceIdentifier = "some-device"
            }))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
        }

        [Fact]
        public async Task Register_ReRegistration_RefreshesHostnameWhenChanged()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);
            await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "dev-123",
                Hostname = "old-host"
            });

            await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "dev-123",
                Hostname = "new-host"
            });

            db.EdgeDevices.Single().Hostname.Should().Be("new-host");
        }

        [Fact]
        public async Task Register_AutoGeneratesDisplayNameFromIdentifier_WhenNotSupplied()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);

            var (resp, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "abcd1234-longer-uuid",
                DisplayName = ""   // empty → should auto-generate
            });

            resp.DisplayName.Should().NotBeNullOrWhiteSpace();
            resp.DisplayName.Should().StartWith("Device ");
        }

        // ════════════════════════════════════════════════════════════════════
        // Heartbeat
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Heartbeat_ExistingDevice_UpdatesLastSeenAt()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "hb-test"
            });
            var seenBefore = db.EdgeDevices.Single().LastSeenAt;

            // Small delay so the timestamp can advance
            await Task.Delay(50);
            var ok = await svc.RecordHeartbeatAsync(device.Id);

            ok.Should().BeTrue();
            db.EdgeDevices.Single().LastSeenAt.Should().BeAfter(seenBefore!.Value);
        }

        [Fact]
        public async Task Heartbeat_NonExistentDevice_ReturnsFalse()
        {
            var ok = await BuildService(BuildDb()).RecordHeartbeatAsync(Guid.NewGuid());
            ok.Should().BeFalse("no such device row — must not throw, must return false");
        }

        [Fact]
        public async Task Heartbeat_SoftDeletedDevice_ReturnsFalse()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "hb-deleted"
            });
            await svc.DeleteAsync(device.Id);

            var ok = await svc.RecordHeartbeatAsync(device.Id);
            ok.Should().BeFalse("deleted device must not respond to heartbeat");
        }

        // ════════════════════════════════════════════════════════════════════
        // CRUD — Create
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Create_DuplicateIdentifierSameTenant_Throws()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);
            var req = new CreateEdgeDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "dup-id",
                DisplayName = "First"
            };

            await svc.CreateAsync(req);

            await svc.Invoking(s => s.CreateAsync(new CreateEdgeDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "dup-id",
                DisplayName = "Second — same identifier"
            }))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
        }

        [Fact]
        public async Task Create_AfterSoftDeleteSameIdentifier_RestoresDeviceAsync()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);

            var first = await svc.CreateAsync(new CreateEdgeDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "recreate-id",
                DisplayName = "Old Name",
                Hostname = "old-host"
            });

            var deleted = await svc.DeleteAsync(first.Id);
            deleted.Should().BeTrue();

            var recreated = await svc.CreateAsync(new CreateEdgeDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "recreate-id",
                DisplayName = "New Name",
                Hostname = "new-host"
            });

            recreated.Id.Should().Be(first.Id, "soft-deleted device should be restored instead of creating a new row");
            recreated.DisplayName.Should().Be("New Name");
            recreated.Hostname.Should().Be("new-host");

            db.EdgeDevices.IgnoreQueryFilters().Count(d => d.TenantId == tenantId && d.DeviceIdentifier == "recreate-id")
                .Should().Be(1);
            db.EdgeDevices.Single(d => d.Id == first.Id).IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task Create_LocationFromDifferentTenant_Throws()
        {
            var db = BuildDb();
            var tenant1 = SeedTenant(db, "T1");
            var tenant2 = SeedTenant(db, "T2");
            var foreignLocationId = Guid.NewGuid();
            db.Locations.Add(new Location
            {
                Id = foreignLocationId,
                TenantId = tenant2,    // belongs to T2
                Name = "HQ",
                Code = "HQ-1",
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
            var svc = BuildService(db);

            await svc.Invoking(s => s.CreateAsync(new CreateEdgeDeviceRequest
            {
                TenantId = tenant1,            // T1 device
                LocationId = foreignLocationId, // T2 location — must be rejected
                DeviceIdentifier = "cross-tenant-loc",
                DisplayName = "Bad Device"
            }))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*tenant*");
        }

        // ════════════════════════════════════════════════════════════════════
        // Camera assignment
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task AssignCamera_SetsDeviceIdOnCamera()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var cameraId = SeedCamera(db, tenantId, "CAM-ASSIGN");
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "assign-test"
            });

            var ok = await svc.AssignCameraAsync(device.Id, cameraId);

            ok.Should().BeTrue();
            db.Cameras.Single(c => c.Id == cameraId).DeviceId.Should().Be(device.Id);
        }

        [Fact]
        public async Task Update_InvalidStatus_ThrowsAsync()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);

            var created = await svc.CreateAsync(new CreateEdgeDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "status-test",
                DisplayName = "Status Device"
            });

            await svc.Invoking(s => s.UpdateAsync(created.Id, new UpdateEdgeDeviceRequest
            {
                Status = 999
            }))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid status*");
        }

        [Fact]
        public async Task AssignCamera_CrossTenantCamera_Throws()
        {
            // Device belongs to T1. Camera belongs to T2. Must be rejected.
            var db = BuildDb();
            var tenant1 = SeedTenant(db, "T1");
            var tenant2 = SeedTenant(db, "T2");
            var cameraId = SeedCamera(db, tenant2, "CAM-T2");
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenant1,
                DeviceIdentifier = "t1-device"
            });

            await svc.Invoking(s => s.AssignCameraAsync(device.Id, cameraId))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*same tenant*");
        }

        [Fact]
        public async Task AssignCamera_NonExistentCamera_Throws()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "dev-x"
            });

            await svc.Invoking(s => s.AssignCameraAsync(device.Id, Guid.NewGuid()))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Camera not found*");
        }

        [Fact]
        public async Task AssignCamera_NonExistentDevice_Throws()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var cameraId = SeedCamera(db, tenantId, "CAM-X");
            var svc = BuildService(db);

            await svc.Invoking(s => s.AssignCameraAsync(Guid.NewGuid(), cameraId))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Device not found*");
        }

        [Fact]
        public async Task AssignCamera_ReassignToSameDevice_IsIdempotent()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var cameraId = SeedCamera(db, tenantId, "CAM-IDEM");
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "idem-dev"
            });

            await svc.AssignCameraAsync(device.Id, cameraId);
            await svc.AssignCameraAsync(device.Id, cameraId); // second call

            db.Cameras.Single(c => c.Id == cameraId).DeviceId.Should().Be(device.Id);
            db.EdgeDevices.Should().ContainSingle(); // no phantom rows
        }

        // ════════════════════════════════════════════════════════════════════
        // Camera unassignment
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task UnassignCamera_SetsDeviceIdToNull()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var cameraId = SeedCamera(db, tenantId, "CAM-UN");
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "unassign-dev"
            });
            await svc.AssignCameraAsync(device.Id, cameraId);

            var ok = await svc.UnassignCameraAsync(device.Id, cameraId);

            ok.Should().BeTrue();
            db.Cameras.Single(c => c.Id == cameraId).DeviceId.Should().BeNull(
                "unassigned camera must return to shared pool (DeviceId = NULL)");
        }

        [Fact]
        public async Task UnassignCamera_CameraNotOnDevice_ReturnsFalse()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var cameraId = SeedCamera(db, tenantId, "CAM-WRONG");
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "wrong-dev"
            });

            // Camera was never assigned — unassign must silently return false
            var ok = await svc.UnassignCameraAsync(device.Id, cameraId);
            ok.Should().BeFalse();
        }

        // ════════════════════════════════════════════════════════════════════
        // Delete
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Delete_SoftDeletesDeviceAndReleasesAssignedCamerasToSharedPool()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var cam1 = SeedCamera(db, tenantId, "CAM-A");
            var cam2 = SeedCamera(db, tenantId, "CAM-B");
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "del-dev"
            });
            await svc.AssignCameraAsync(device.Id, cam1);
            await svc.AssignCameraAsync(device.Id, cam2);

            var ok = await svc.DeleteAsync(device.Id);

            ok.Should().BeTrue();
            db.EdgeDevices.IgnoreQueryFilters().Single().IsDeleted.Should().BeTrue();
            db.Cameras.Single(c => c.Id == cam1).DeviceId.Should().BeNull(
                "cameras must move back to the shared pool when their device is deleted");
            db.Cameras.Single(c => c.Id == cam2).DeviceId.Should().BeNull();
        }

        [Fact]
        public async Task Delete_NonExistentDevice_ReturnsFalse()
        {
            var ok = await BuildService(BuildDb()).DeleteAsync(Guid.NewGuid());
            ok.Should().BeFalse();
        }

        [Fact]
        public async Task Delete_AlreadyDeletedDevice_ReturnsFalse()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "double-del"
            });
            await svc.DeleteAsync(device.Id);

            var secondDelete = await svc.DeleteAsync(device.Id);
            secondDelete.Should().BeFalse("double-delete must not throw, must return false");
        }

        // ════════════════════════════════════════════════════════════════════
        // List / Get
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetByTenant_ExcludesSoftDeletedDevices()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var svc = BuildService(db);
            await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "visible"
            });
            var (toDelete, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "deleted"
            });
            await svc.DeleteAsync(toDelete.Id);

            var list = await svc.GetByTenantAsync(tenantId);

            list.Should().ContainSingle()
                .Which.DeviceIdentifier.Should().Be("visible");
        }

        [Fact]
        public async Task GetByTenant_DoesNotLeakAcrossTenants()
        {
            var db = BuildDb();
            var t1 = SeedTenant(db, "T1");
            var t2 = SeedTenant(db, "T2");
            var svc = BuildService(db);
            await svc.RegisterAsync(new RegisterDeviceRequest { TenantId = t1, DeviceIdentifier = "t1-dev" });
            await svc.RegisterAsync(new RegisterDeviceRequest { TenantId = t2, DeviceIdentifier = "t2-dev" });

            var list = await svc.GetByTenantAsync(t1);

            list.Should().ContainSingle()
                .Which.TenantId.Should().Be(t1, "must not return devices from other tenants");
        }

        [Fact]
        public async Task GetAll_ReturnsDevicesFromAllTenants()
        {
            var db = BuildDb();
            var t1 = SeedTenant(db, "T1");
            var t2 = SeedTenant(db, "T2");
            var svc = BuildService(db);
            await svc.RegisterAsync(new RegisterDeviceRequest { TenantId = t1, DeviceIdentifier = "d1" });
            await svc.RegisterAsync(new RegisterDeviceRequest { TenantId = t2, DeviceIdentifier = "d2" });

            var all = await svc.GetAllAsync();
            all.Should().HaveCount(2);
        }

        // ════════════════════════════════════════════════════════════════════
        // Response shape / backward-compat
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public void CameraResponse_NewCamera_HasNullDeviceId()
        {
            // Regression guard: existing cameras must not suddenly appear assigned.
            var camera = new Camera
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                CameraId = "CAM-BC",
                Name = "Backward Compat",
                RtspUrlEncrypted = "enc",
                Status = CameraStatus.Active,
                CreatedAt = DateTime.UtcNow
                // DeviceId intentionally not set — should default to null
            };
            var dto = violation_management_api.DTOs.Responses.CameraResponse.FromEntity(camera);

            dto.DeviceId.Should().BeNull("DeviceId must default to null for backward compat");
            dto.DeviceName.Should().BeNull();
        }

        [Fact]
        public async Task CameraResponse_AssignedCamera_CarriesDeviceIdAndName()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var cameraId = SeedCamera(db, tenantId, "CAM-RESP");
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "resp-dev",
                DisplayName = "Floor Device 3"
            });
            await svc.AssignCameraAsync(device.Id, cameraId);

            var camera = await db.Cameras
                .Include(c => c.Device)
                .Include(c => c.Tenant)
                .SingleAsync(c => c.Id == cameraId);
            var dto = violation_management_api.DTOs.Responses.CameraResponse.FromEntity(camera);

            dto.DeviceId.Should().Be(device.Id);
            dto.DeviceName.Should().Be("Floor Device 3");
        }

        [Fact]
        public async Task DeviceResponse_CameraCountIsAccurate()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            SeedCamera(db, tenantId, "CAM-1");
            SeedCamera(db, tenantId, "CAM-2");
            SeedCamera(db, tenantId, "CAM-3");
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "count-dev"
            });
            await svc.AssignCameraAsync(device.Id, db.Cameras.First().Id);
            await svc.AssignCameraAsync(device.Id, db.Cameras.Skip(1).First().Id);

            var resp = await svc.GetByIdAsync(device.Id);

            resp!.CameraCount.Should().Be(2);
        }

        // ════════════════════════════════════════════════════════════════════
        // Multi-location warning surface (DistinctLocationIds)
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task DeviceResponse_SingleLocationCameras_DistinctLocationIdsHasOneEntry()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var locId = Guid.NewGuid();
            db.Locations.Add(new Location
            {
                Id = locId, TenantId = tenantId, Name = "HQ", Code = "HQ-1", CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
            SeedCamera(db, tenantId, "CAM-L1", locId);
            SeedCamera(db, tenantId, "CAM-L2", locId);
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "same-loc-dev"
            });
            foreach (var cam in db.Cameras.ToList())
                await svc.AssignCameraAsync(device.Id, cam.Id);

            var resp = await svc.GetByIdAsync(device.Id);
            resp!.DistinctLocationIds.Should().ContainSingle();
        }

        [Fact]
        public async Task DeviceResponse_MixedLocationCameras_DistinctLocationIdsHasMultipleEntries()
        {
            var db = BuildDb();
            var tenantId = SeedTenant(db);
            var loc1 = Guid.NewGuid();
            var loc2 = Guid.NewGuid();
            foreach (var (lid, code) in new[] { (loc1, "A1"), (loc2, "B2") })
            {
                db.Locations.Add(new Location
                {
                    Id = lid, TenantId = tenantId, Name = code, Code = code, CreatedAt = DateTime.UtcNow
                });
            }
            db.SaveChanges();
            SeedCamera(db, tenantId, "CAM-A", loc1);
            SeedCamera(db, tenantId, "CAM-B", loc2);
            var svc = BuildService(db);
            var (device, _) = await svc.RegisterAsync(new RegisterDeviceRequest
            {
                TenantId = tenantId,
                DeviceIdentifier = "mixed-loc-dev"
            });
            foreach (var cam in db.Cameras.ToList())
                await svc.AssignCameraAsync(device.Id, cam.Id);

            var resp = await svc.GetByIdAsync(device.Id);
            resp!.DistinctLocationIds.Should().HaveCount(2,
                "UI uses this to decide whether to show a location-mismatch warning");
        }
    }
}
