using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AlphaSurveilance.Controllers;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.DTOs.Responses;
using AlphaSurveilance.Services.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace violation_management_api.Tests
{
    /// <summary>
    /// Tests for the false-positive feature across controller, service, and
    /// downstream analytics-exclusion paths.
    ///
    /// Structure
    /// ─────────────────────────────────────────────────────────────────────
    ///   Controller layer  – GET/POST routing, request validation, HTTP codes
    ///   Service layer     – MarkFalsePositiveAsync, UnmarkFalsePositiveAsync,
    ///                       GetFalsePositiveViolationsAsync delegation
    ///   Edge / security   – empty list, wrong tenant, null body, duplicate mark
    /// </summary>
    public class FalsePositiveTests
    {
        // ── test doubles ────────────────────────────────────────────────────

        private readonly Mock<IViolationService> _svcMock = new();
        private readonly Mock<ICurrentTenantService> _tenantSvcMock = new();

        private readonly Guid _tenantId = Guid.NewGuid();
        private ViolationsController BuildController(string? userEmail = "admin@alpha.com")
        {
            _tenantSvcMock.Setup(t => t.TenantId).Returns(_tenantId);

            var ctrl = new ViolationsController(_svcMock.Object, _tenantSvcMock.Object);

            // Attach an identity so User?.FindFirst(…) works inside controller methods
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "user-sub-001"),
                new(ClaimTypes.Email, userEmail ?? ""),
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);
            ctrl.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };
            return ctrl;
        }

        // ════════════════════════════════════════════════════════════════════
        // Controller — GET /api/violations/false-positives
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetFalsePositiveViolations_ReturnsOkWithList()
        {
            var expected = new List<ViolationResponse>
            {
                new() { Id = Guid.NewGuid(), IsFalsePositive = true, FalsePositiveMarkedBy = "admin@alpha.com" },
                new() { Id = Guid.NewGuid(), IsFalsePositive = true },
            };
            _svcMock.Setup(s => s.GetFalsePositiveViolationsAsync(_tenantId.ToString()))
                    .ReturnsAsync(expected);

            var ctrl = BuildController();
            var actionResult = await ctrl.GetFalsePositiveViolations();
            var result = actionResult.Result as OkObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            result.Value.Should().BeEquivalentTo(expected);
        }

        [Fact]
        public async Task GetFalsePositiveViolations_EmptyTenant_ReturnsEmptyList()
        {
            _svcMock.Setup(s => s.GetFalsePositiveViolationsAsync(_tenantId.ToString()))
                    .ReturnsAsync(new List<ViolationResponse>());

            var actionResult = await BuildController().GetFalsePositiveViolations();
            var result = actionResult.Result as OkObjectResult;

            result.Should().NotBeNull();
            (result!.Value as IEnumerable<ViolationResponse>).Should().BeEmpty();
        }

        // ════════════════════════════════════════════════════════════════════
        // Controller — POST /api/violations/false-positives/mark
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task MarkFalsePositive_ValidRequest_ReturnsOkWithCount()
        {
            var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            _svcMock.Setup(s => s.MarkFalsePositiveAsync(ids, _tenantId.ToString(),
                            It.IsAny<string?>(), "mirror reflection"))
                    .ReturnsAsync(2);

            var req = new MarkFalsePositiveRequest { ViolationIds = ids, Reason = "mirror reflection" };
            var result = await BuildController().MarkFalsePositive(req) as OkObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            var markedProp = result.Value!.GetType().GetProperty("marked")!.GetValue(result.Value);
            markedProp.Should().Be(2);
        }

        [Fact]
        public async Task MarkFalsePositive_NullBody_ReturnsBadRequest()
        {
            var result = await BuildController().MarkFalsePositive(null!) as BadRequestObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task MarkFalsePositive_EmptyViolationIds_ReturnsBadRequest()
        {
            var req = new MarkFalsePositiveRequest { ViolationIds = new List<Guid>() };
            var result = await BuildController().MarkFalsePositive(req) as BadRequestObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(400);
            var err = result.Value!.GetType().GetProperty("error")?.GetValue(result.Value) as string;
            err.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task MarkFalsePositive_WithoutReason_StillSucceeds()
        {
            // Reason is optional — omitting it must not cause an error
            var ids = new List<Guid> { Guid.NewGuid() };
            _svcMock.Setup(s => s.MarkFalsePositiveAsync(ids, _tenantId.ToString(),
                            It.IsAny<string?>(), null))
                    .ReturnsAsync(1);

            var req = new MarkFalsePositiveRequest { ViolationIds = ids, Reason = null };
            var result = await BuildController().MarkFalsePositive(req) as OkObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
        }

        [Fact]
        public async Task MarkFalsePositive_UserId_IsPassedFromToken()
        {
            // Verify controller extracts the sub/NameIdentifier claim and forwards it.
            var ids = new List<Guid> { Guid.NewGuid() };
            string? capturedUserId = null;
            _svcMock.Setup(s => s.MarkFalsePositiveAsync(ids, _tenantId.ToString(),
                            It.IsAny<string?>(), null))
                    .Callback<IEnumerable<Guid>, string, string?, string?>((_, _, uid, _) => capturedUserId = uid)
                    .ReturnsAsync(1);

            var req = new MarkFalsePositiveRequest { ViolationIds = ids };
            await BuildController().MarkFalsePositive(req);

            capturedUserId.Should().Be("user-sub-001");
        }

        // ════════════════════════════════════════════════════════════════════
        // Controller — POST /api/violations/false-positives/unmark
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task UnmarkFalsePositive_ValidRequest_ReturnsOkWithCount()
        {
            var ids = new List<Guid> { Guid.NewGuid() };
            _svcMock.Setup(s => s.UnmarkFalsePositiveAsync(ids, _tenantId.ToString()))
                    .ReturnsAsync(1);

            var req = new UnmarkFalsePositiveRequest { ViolationIds = ids };
            var result = await BuildController().UnmarkFalsePositive(req) as OkObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            var prop = result.Value!.GetType().GetProperty("unmarked")!.GetValue(result.Value);
            prop.Should().Be(1);
        }

        [Fact]
        public async Task UnmarkFalsePositive_NullBody_ReturnsBadRequest()
        {
            var result = await BuildController().UnmarkFalsePositive(null!) as BadRequestObjectResult;
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task UnmarkFalsePositive_EmptyIds_ReturnsBadRequest()
        {
            var req = new UnmarkFalsePositiveRequest { ViolationIds = new List<Guid>() };
            var result = await BuildController().UnmarkFalsePositive(req) as BadRequestObjectResult;
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(400);
        }

        // ════════════════════════════════════════════════════════════════════
        // Service layer — delegation to repository
        // ════════════════════════════════════════════════════════════════════

        // These tests use a thin ViolationService constructed with mocked
        // dependencies and verify the service correctly delegates to the
        // repository and returns the expected count.

        private AlphaSurveilance.Services.ViolationService BuildService()
        {
            var repoMock     = _repoMock.Object;
            var cameraMock   = new Mock<violation_management_api.Services.Interfaces.ICameraService>().Object;
            var mapperMock   = new Mock<AutoMapper.IMapper>().Object;
            var cacheMock    = new Mock<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Object;
            var scopeMock    = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>().Object;
            var logMock      = new Mock<Microsoft.Extensions.Logging.ILogger<AlphaSurveilance.Services.ViolationService>>().Object;
            return new AlphaSurveilance.Services.ViolationService(repoMock, cameraMock, mapperMock, cacheMock, scopeMock, logMock);
        }

        private readonly Mock<AlphaSurveilance.Data.Repositories.Interfaces.IViolationRepository> _repoMock = new();

        [Fact]
        public async Task Service_MarkFalsePositive_DelegatesToRepository()
        {
            var ids = new List<Guid> { Guid.NewGuid() };
            _repoMock.Setup(r => r.MarkFalsePositiveAsync(ids, _tenantId, "user@a.com", "test reason"))
                     .ReturnsAsync(1);

            var svc = BuildService();
            var result = await svc.MarkFalsePositiveAsync(ids, _tenantId.ToString(), "user@a.com", "test reason");

            result.Should().Be(1);
            _repoMock.Verify(r => r.MarkFalsePositiveAsync(ids, _tenantId, "user@a.com", "test reason"), Times.Once);
        }

        [Fact]
        public async Task Service_MarkFalsePositive_InvalidTenantId_ReturnsZero()
        {
            var svc = BuildService();
            var result = await svc.MarkFalsePositiveAsync(new List<Guid> { Guid.NewGuid() }, "not-a-guid", null, null);

            result.Should().Be(0);
            _repoMock.Verify(r => r.MarkFalsePositiveAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task Service_UnmarkFalsePositive_DelegatesToRepository()
        {
            var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            _repoMock.Setup(r => r.UnmarkFalsePositiveAsync(ids, _tenantId))
                     .ReturnsAsync(2);

            var svc = BuildService();
            var result = await svc.UnmarkFalsePositiveAsync(ids, _tenantId.ToString());

            result.Should().Be(2);
            _repoMock.Verify(r => r.UnmarkFalsePositiveAsync(ids, _tenantId), Times.Once);
        }

        [Fact]
        public async Task Service_UnmarkFalsePositive_InvalidTenantId_ReturnsZero()
        {
            var svc = BuildService();
            var result = await svc.UnmarkFalsePositiveAsync(new List<Guid> { Guid.NewGuid() }, "bad-guid");

            result.Should().Be(0);
            _repoMock.Verify(r => r.UnmarkFalsePositiveAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Service_GetFalsePositiveViolations_DelegatesToRepository()
        {
            var fp = new List<AlphaSurveilance.Core.Domain.Violation>
            {
                new() { Id = Guid.NewGuid(), TenantId = _tenantId, IsFalsePositive = true, CorrelationId = "c1" }
            };
            _repoMock.Setup(r => r.GetFalsePositivesAsync(_tenantId)).ReturnsAsync(fp);

            // The service needs a mapper to produce ViolationResponse — stub it to return an empty list
            // so the call-chain is exercised without full infrastructure.
            var mapperMock = new Mock<AutoMapper.IMapper>();
            mapperMock.Setup(m => m.Map<IEnumerable<ViolationResponse>>(It.IsAny<object>()))
                      .Returns(new List<ViolationResponse> { new() { Id = fp[0].Id, IsFalsePositive = true } });

            var cameraMock = new Mock<violation_management_api.Services.Interfaces.ICameraService>();
            cameraMock.Setup(c => c.GetCamerasByTenantAsync(_tenantId))
                      .ReturnsAsync(new List<violation_management_api.DTOs.Responses.CameraResponse>());

            var svc = new AlphaSurveilance.Services.ViolationService(
                _repoMock.Object, cameraMock.Object, mapperMock.Object,
                new Mock<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Object,
                new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>().Object,
                new Mock<Microsoft.Extensions.Logging.ILogger<AlphaSurveilance.Services.ViolationService>>().Object);

            var responses = await svc.GetFalsePositiveViolationsAsync(_tenantId.ToString());

            _repoMock.Verify(r => r.GetFalsePositivesAsync(_tenantId), Times.Once);
            responses.Should().ContainSingle(r => r.Id == fp[0].Id);
        }

        [Fact]
        public async Task Service_GetFalsePositiveViolations_InvalidTenantId_ReturnsEmpty()
        {
            var svc = BuildService();
            var result = await svc.GetFalsePositiveViolationsAsync("not-a-guid");
            result.Should().BeEmpty();
        }

        // ════════════════════════════════════════════════════════════════════
        // Edge / security cases
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task MarkFalsePositive_ServiceReturnsZero_WhenViolationsBelongToDifferentTenant()
        {
            // Simulate: attacker sends ids that exist but belong to another tenant.
            // The repository WHERE clause (TenantId == tenantId) returns 0 rows matched.
            var ids = new List<Guid> { Guid.NewGuid() };
            _svcMock.Setup(s => s.MarkFalsePositiveAsync(ids, _tenantId.ToString(),
                            It.IsAny<string?>(), It.IsAny<string?>()))
                    .ReturnsAsync(0);   // cross-tenant: nothing updated

            var req = new MarkFalsePositiveRequest { ViolationIds = ids };
            var result = await BuildController().MarkFalsePositive(req) as OkObjectResult;

            result.Should().NotBeNull();
            var markedProp = result!.Value!.GetType().GetProperty("marked")!.GetValue(result.Value);
            markedProp.Should().Be(0);
        }

        [Fact]
        public async Task MarkFalsePositive_AlreadyMarked_DoesNotDoubleCount()
        {
            // Repository WHERE clause includes `&& !v.IsFalsePositive`, so already-marked
            // rows are excluded and count = 0.
            var alreadyFpId = Guid.NewGuid();
            var ids = new List<Guid> { alreadyFpId };
            _svcMock.Setup(s => s.MarkFalsePositiveAsync(ids, _tenantId.ToString(),
                            It.IsAny<string?>(), It.IsAny<string?>()))
                    .ReturnsAsync(0);   // already flagged, nothing to update

            var req = new MarkFalsePositiveRequest { ViolationIds = ids };
            var result = await BuildController().MarkFalsePositive(req) as OkObjectResult;

            result.Should().NotBeNull();
            var markedProp = result!.Value!.GetType().GetProperty("marked")!.GetValue(result.Value);
            markedProp.Should().Be(0);
        }

        [Fact]
        public async Task UnmarkFalsePositive_NotMarkedViolation_ReturnsZero()
        {
            var ids = new List<Guid> { Guid.NewGuid() };
            _svcMock.Setup(s => s.UnmarkFalsePositiveAsync(ids, _tenantId.ToString()))
                    .ReturnsAsync(0);   // row exists but IsFalsePositive=false already

            var req = new UnmarkFalsePositiveRequest { ViolationIds = ids };
            var result = await BuildController().UnmarkFalsePositive(req) as OkObjectResult;

            result.Should().NotBeNull();
            var prop = result!.Value!.GetType().GetProperty("unmarked")!.GetValue(result.Value);
            prop.Should().Be(0);
        }

        [Fact]
        public async Task MarkFalsePositive_LargeBulk_AllIdsForwarded()
        {
            // Ensure the controller does not silently truncate large batches
            var ids = new List<Guid>();
            for (int i = 0; i < 500; i++) ids.Add(Guid.NewGuid());

            IEnumerable<Guid>? capturedIds = null;
            _svcMock.Setup(s => s.MarkFalsePositiveAsync(It.IsAny<IEnumerable<Guid>>(),
                            _tenantId.ToString(), It.IsAny<string?>(), It.IsAny<string?>()))
                    .Callback<IEnumerable<Guid>, string, string?, string?>((forwarded, _, _, _) => capturedIds = forwarded)
                    .ReturnsAsync(ids.Count);

            var req = new MarkFalsePositiveRequest { ViolationIds = ids };
            await BuildController().MarkFalsePositive(req);

            capturedIds.Should().BeEquivalentTo(ids);
        }

        [Fact]
        public async Task MissingTenant_MarkFalsePositive_ThrowsUnauthorized()
        {
            // TenantId is null → GetTenantId() throws UnauthorizedAccessException
            _tenantSvcMock.Setup(t => t.TenantId).Returns((Guid?)null);
            var ctrl = new ViolationsController(_svcMock.Object, _tenantSvcMock.Object);

            var req = new MarkFalsePositiveRequest { ViolationIds = new List<Guid> { Guid.NewGuid() } };

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctrl.MarkFalsePositive(req));
        }

        [Fact]
        public async Task MissingTenant_UnmarkFalsePositive_ThrowsUnauthorized()
        {
            _tenantSvcMock.Setup(t => t.TenantId).Returns((Guid?)null);
            var ctrl = new ViolationsController(_svcMock.Object, _tenantSvcMock.Object);

            var req = new UnmarkFalsePositiveRequest { ViolationIds = new List<Guid> { Guid.NewGuid() } };

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctrl.UnmarkFalsePositive(req));
        }

        [Fact]
        public async Task MissingTenant_GetFalsePositives_ThrowsUnauthorized()
        {
            _tenantSvcMock.Setup(t => t.TenantId).Returns((Guid?)null);
            var ctrl = new ViolationsController(_svcMock.Object, _tenantSvcMock.Object);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctrl.GetFalsePositiveViolations());
        }

        // ════════════════════════════════════════════════════════════════════
        // Analytics / Stats exclusion — service delegates
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetViolations_NeverIncludesFalsePositives()
        {
            // GetViolationsAsync (used by Compliance, active list) must never contain FP rows.
            // The service calls GetAllAsync(tenantId) which defaults includeFalsePositives=false.
            _repoMock.Setup(r => r.GetAllAsync(_tenantId, false))
                     .ReturnsAsync(new List<AlphaSurveilance.Core.Domain.Violation>());

            var mapperMock = new Mock<AutoMapper.IMapper>();
            mapperMock.Setup(m => m.Map<IEnumerable<ViolationResponse>>(It.IsAny<object>()))
                      .Returns(new List<ViolationResponse>());

            var cameraMock = new Mock<violation_management_api.Services.Interfaces.ICameraService>();
            cameraMock.Setup(c => c.GetCamerasByTenantAsync(_tenantId))
                      .ReturnsAsync(new List<violation_management_api.DTOs.Responses.CameraResponse>());

            var svc = new AlphaSurveilance.Services.ViolationService(
                _repoMock.Object, cameraMock.Object, mapperMock.Object,
                new Mock<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Object,
                new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>().Object,
                new Mock<Microsoft.Extensions.Logging.ILogger<AlphaSurveilance.Services.ViolationService>>().Object);

            await svc.GetViolationsAsync(_tenantId.ToString());

            // Must call GetAllAsync with includeFalsePositives=false (the default)
            _repoMock.Verify(r => r.GetAllAsync(_tenantId, false), Times.Once);
            // Must NEVER request the full set
            _repoMock.Verify(r => r.GetAllAsync(_tenantId, true), Times.Never);
        }
    }
}
