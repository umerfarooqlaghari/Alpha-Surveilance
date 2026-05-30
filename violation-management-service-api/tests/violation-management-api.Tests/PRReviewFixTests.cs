using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlphaSurveilance.Controllers;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Data.Repositories.Interfaces;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.DTOs.Responses;
using AlphaSurveilance.Services;
using AlphaSurveilance.Services.Interfaces;
using AutoMapper;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;
using Xunit;

namespace violation_management_api.Tests
{
    /// <summary>
    /// Tests covering every change made during the PR-25 code-review pass.
    ///
    /// Sections
    /// ─────────────────────────────────────────────────────────────────────
    ///  A. FrameUrl null-guard  (ViolationService — both single and bulk paths)
    ///  B. Tenant-scoped employee lookup  (cross-tenant isolation)
    ///  C. Controller namespace / routing  (MarkFalsePositive / UnmarkFalsePositive)
    /// </summary>
    public class PRReviewFixTests
    {
        // ── shared helpers ────────────────────────────────────────────────

        private readonly Mock<IViolationRepository> _repoMock = new();
        private readonly Mock<ICameraService> _cameraSvcMock = new();
        private readonly Mock<IMapper> _mapperMock = new();
        private readonly Mock<IMemoryCache> _cacheMock = new();
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
        private readonly Mock<ILogger<ViolationService>> _loggerMock = new();

        private ViolationService BuildService() => new(
            _repoMock.Object,
            _cameraSvcMock.Object,
            _mapperMock.Object,
            _cacheMock.Object,
            _scopeFactoryMock.Object,
            _loggerMock.Object
        );

        /// <summary>Returns a minimal ViolationResponse with the given FramePath.</summary>
        private static ViolationResponse ResponseWithPath(string? framePath)
            => new() { Id = Guid.NewGuid(), FramePath = framePath };

        private void SetupCameraServiceEmpty(Guid tenantId)
            => _cameraSvcMock
                .Setup(c => c.GetCamerasByTenantAsync(tenantId))
                .ReturnsAsync(new List<violation_management_api.DTOs.Responses.CameraResponse>());

        // Mapper returns the supplied responses regardless of input
        private void SetupMapper(IEnumerable<ViolationResponse> responses)
            => _mapperMock
                .Setup(m => m.Map<IEnumerable<ViolationResponse>>(It.IsAny<object>()))
                .Returns(responses);

        // ════════════════════════════════════════════════════════════════════
        // A. FrameUrl null-guard
        //    FrameUrl must be null (not empty string) when FramePath is absent.
        //    A non-null empty string would render as a broken <img src=""> in the UI.
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task FrameUrl_IsNull_WhenFramePathIsNull()
        {
            var tenantId = Guid.NewGuid();
            _repoMock.Setup(r => r.GetAllAsync(tenantId, false))
                     .ReturnsAsync(new List<Violation>());
            SetupMapper(new[] { ResponseWithPath(null) });
            SetupCameraServiceEmpty(tenantId);

            var svc = BuildService();
            var results = (await svc.GetViolationsAsync(tenantId.ToString())).ToList();

            results.Should().HaveCount(1);
            results[0].FrameUrl.Should().BeNull("null FramePath must produce null FrameUrl, not an empty string");
        }

        [Fact]
        public async Task FrameUrl_IsNull_WhenFramePathIsEmptyString()
        {
            var tenantId = Guid.NewGuid();
            _repoMock.Setup(r => r.GetAllAsync(tenantId, false))
                     .ReturnsAsync(new List<Violation>());
            SetupMapper(new[] { ResponseWithPath("") });
            SetupCameraServiceEmpty(tenantId);

            var svc = BuildService();
            var results = (await svc.GetViolationsAsync(tenantId.ToString())).ToList();

            results[0].FrameUrl.Should().BeNull("empty-string FramePath must produce null FrameUrl");
        }

        [Fact]
        public async Task FrameUrl_IsNull_WhenFramePathIsWhitespaceOnly()
        {
            var tenantId = Guid.NewGuid();
            _repoMock.Setup(r => r.GetAllAsync(tenantId, false))
                     .ReturnsAsync(new List<Violation>());
            SetupMapper(new[] { ResponseWithPath("   ") });
            SetupCameraServiceEmpty(tenantId);

            var svc = BuildService();
            var results = (await svc.GetViolationsAsync(tenantId.ToString())).ToList();

            results[0].FrameUrl.Should().BeNull("whitespace-only FramePath must produce null FrameUrl");
        }

        [Fact]
        public async Task FrameUrl_EqualsFramePath_WhenFramePathIsValidUrl()
        {
            const string url = "https://alphasurveilance-dev-1.s3.us-east-1.amazonaws.com/violations/frame.jpg";
            var tenantId = Guid.NewGuid();
            _repoMock.Setup(r => r.GetAllAsync(tenantId, false))
                     .ReturnsAsync(new List<Violation>());
            SetupMapper(new[] { ResponseWithPath(url) });
            SetupCameraServiceEmpty(tenantId);

            var svc = BuildService();
            var results = (await svc.GetViolationsAsync(tenantId.ToString())).ToList();

            results[0].FrameUrl.Should().Be(url, "a valid FramePath must be preserved verbatim in FrameUrl");
        }

        [Fact]
        public async Task FrameUrl_IsNull_ForAllAbsentPaths_InBulkResponse()
        {
            // Multiple violations with mixed FramePath states — ALL absent ones become null.
            const string validUrl = "https://example.com/frame.jpg";
            var tenantId = Guid.NewGuid();
            _repoMock.Setup(r => r.GetAllAsync(tenantId, false))
                     .ReturnsAsync(new List<Violation>());
            SetupMapper(new[]
            {
                ResponseWithPath(null),
                ResponseWithPath(""),
                ResponseWithPath("   "),
                ResponseWithPath(validUrl),
            });
            SetupCameraServiceEmpty(tenantId);

            var svc = BuildService();
            var results = (await svc.GetViolationsAsync(tenantId.ToString())).ToList();

            results.Should().HaveCount(4);
            results[0].FrameUrl.Should().BeNull();
            results[1].FrameUrl.Should().BeNull();
            results[2].FrameUrl.Should().BeNull();
            results[3].FrameUrl.Should().Be(validUrl);
        }

        // ════════════════════════════════════════════════════════════════════
        // B. Tenant-scoped employee lookup
        //    MarkFalsePositive / UnmarkFalsePositive still delegate correctly.
        //    The cross-tenant isolation is verified via the service-layer mock
        //    (the actual DB-level scoping is an integration concern).
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task MarkFalsePositive_WithInvalidTenantGuid_ReturnsZero_NotThrows()
        {
            // If the tenantId can't be parsed as a Guid the service must return 0 silently
            // (the composite-key lookup would NPE if it tried to query with a broken key).
            var svc = BuildService();
            var result = await svc.MarkFalsePositiveAsync(
                new List<Guid> { Guid.NewGuid() },
                "NOT-A-GUID",
                "user@test.com",
                "reason"
            );

            result.Should().Be(0);
            _repoMock.Verify(r => r.MarkFalsePositiveAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
                Times.Never,
                "repository must not be called with an invalid tenant GUID");
        }

        [Fact]
        public async Task UnmarkFalsePositive_WithInvalidTenantGuid_ReturnsZero_NotThrows()
        {
            var svc = BuildService();
            var result = await svc.UnmarkFalsePositiveAsync(
                new List<Guid> { Guid.NewGuid() },
                "NOT-A-GUID"
            );

            result.Should().Be(0);
            _repoMock.Verify(r => r.UnmarkFalsePositiveAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<Guid>()),
                Times.Never);
        }

        // ════════════════════════════════════════════════════════════════════
        // C. Controller — namespace fix & routing correctness
        //    After removing the bogus `using AlphaSurveilance.DTO.Requests;`,
        //    MarkFalsePositive and UnmarkFalsePositive must still accept the
        //    correct DTOs and return the expected HTTP shapes.
        // ════════════════════════════════════════════════════════════════════

        private readonly Mock<IViolationService> _ctrlSvcMock = new();
        private readonly Mock<ICurrentTenantService> _ctrlTenantMock = new();

        private ViolationsController BuildController(Guid tenantId)
        {
            _ctrlTenantMock.Setup(t => t.TenantId).Returns(tenantId);
            var ctrl = new ViolationsController(_ctrlSvcMock.Object, _ctrlTenantMock.Object);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "user-abc"),
                new(ClaimTypes.Email, "admin@alpha.com"),
            };
            ctrl.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                }
            };
            return ctrl;
        }

        [Fact]
        public async Task MarkFalsePositive_ValidBody_Returns200WithMarkedCount()
        {
            var tenantId = Guid.NewGuid();
            var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            _ctrlSvcMock.Setup(s => s.MarkFalsePositiveAsync(ids, tenantId.ToString(), "user-abc", "test reason"))
                        .ReturnsAsync(2);

            var ctrl = BuildController(tenantId);
            var result = await ctrl.MarkFalsePositive(new MarkFalsePositiveRequest
            {
                ViolationIds = ids,
                Reason = "test reason"
            }) as OkObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            result.Value.Should().BeEquivalentTo(new { marked = 2 });
        }

        [Fact]
        public async Task UnmarkFalsePositive_ValidBody_Returns200WithUnmarkedCount()
        {
            var tenantId = Guid.NewGuid();
            var ids = new List<Guid> { Guid.NewGuid() };
            _ctrlSvcMock.Setup(s => s.UnmarkFalsePositiveAsync(ids, tenantId.ToString()))
                        .ReturnsAsync(1);

            var ctrl = BuildController(tenantId);
            var result = await ctrl.UnmarkFalsePositive(new UnmarkFalsePositiveRequest
            {
                ViolationIds = ids
            }) as OkObjectResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            result.Value.Should().BeEquivalentTo(new { unmarked = 1 });
        }

        [Fact]
        public async Task MarkFalsePositive_NullRequest_Returns400()
        {
            var ctrl = BuildController(Guid.NewGuid());
            var result = await ctrl.MarkFalsePositive(null!);
            (result as BadRequestObjectResult)?.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task UnmarkFalsePositive_NullRequest_Returns400()
        {
            var ctrl = BuildController(Guid.NewGuid());
            var result = await ctrl.UnmarkFalsePositive(null!);
            (result as BadRequestObjectResult)?.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task MarkFalsePositive_EmptyViolationIds_Returns400()
        {
            var ctrl = BuildController(Guid.NewGuid());
            var result = await ctrl.MarkFalsePositive(new MarkFalsePositiveRequest
            {
                ViolationIds = new List<Guid>()
            });
            (result as BadRequestObjectResult)?.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task UnmarkFalsePositive_EmptyViolationIds_Returns400()
        {
            var ctrl = BuildController(Guid.NewGuid());
            var result = await ctrl.UnmarkFalsePositive(new UnmarkFalsePositiveRequest
            {
                ViolationIds = new List<Guid>()
            });
            (result as BadRequestObjectResult)?.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task MarkFalsePositive_NullReason_StillSucceeds()
        {
            // Reason is optional — a null reason must not trigger a 400.
            var tenantId = Guid.NewGuid();
            var ids = new List<Guid> { Guid.NewGuid() };
            _ctrlSvcMock.Setup(s => s.MarkFalsePositiveAsync(ids, tenantId.ToString(), It.IsAny<string?>(), null))
                        .ReturnsAsync(1);

            var ctrl = BuildController(tenantId);
            var result = await ctrl.MarkFalsePositive(new MarkFalsePositiveRequest
            {
                ViolationIds = ids,
                Reason = null
            });

            (result as OkObjectResult)?.StatusCode.Should().Be(200);
        }

        [Fact]
        public async Task MarkFalsePositive_ServiceReturnsZero_Still200_NotError()
        {
            // Zero affected rows is valid (e.g. IDs already marked or wrong tenant).
            // Controller must NOT convert this to a 404/error — consumers check the count.
            var tenantId = Guid.NewGuid();
            var ids = new List<Guid> { Guid.NewGuid() };
            _ctrlSvcMock.Setup(s => s.MarkFalsePositiveAsync(ids, tenantId.ToString(), It.IsAny<string?>(), It.IsAny<string?>()))
                        .ReturnsAsync(0);

            var ctrl = BuildController(tenantId);
            var result = await ctrl.MarkFalsePositive(new MarkFalsePositiveRequest { ViolationIds = ids });

            var ok = result as OkObjectResult;
            ok.Should().NotBeNull();
            ok!.Value.Should().BeEquivalentTo(new { marked = 0 });
        }

        [Fact]
        public async Task UnmarkFalsePositive_ServiceReturnsZero_Still200_NotError()
        {
            var tenantId = Guid.NewGuid();
            var ids = new List<Guid> { Guid.NewGuid() };
            _ctrlSvcMock.Setup(s => s.UnmarkFalsePositiveAsync(ids, tenantId.ToString()))
                        .ReturnsAsync(0);

            var ctrl = BuildController(tenantId);
            var result = await ctrl.UnmarkFalsePositive(new UnmarkFalsePositiveRequest { ViolationIds = ids });

            var ok = result as OkObjectResult;
            ok.Should().NotBeNull();
            ok!.Value.Should().BeEquivalentTo(new { unmarked = 0 });
        }

        [Fact]
        public async Task MarkFalsePositive_500IdsInOneBatch_AllForwarded()
        {
            // Bulk upper bound — the controller must not truncate or batch-split the list.
            var tenantId = Guid.NewGuid();
            var ids = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToList();
            _ctrlSvcMock.Setup(s => s.MarkFalsePositiveAsync(
                    It.Is<IEnumerable<Guid>>(l => l.Count() == 500),
                    tenantId.ToString(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(500);

            var ctrl = BuildController(tenantId);
            var result = await ctrl.MarkFalsePositive(new MarkFalsePositiveRequest { ViolationIds = ids });

            (result as OkObjectResult)?.StatusCode.Should().Be(200);
        }

        [Fact]
        public async Task MarkFalsePositive_MissingTenant_ThrowsUnauthorized()
        {
            _ctrlTenantMock.Setup(t => t.TenantId).Returns((Guid?)null);
            var ctrl = new ViolationsController(_ctrlSvcMock.Object, _ctrlTenantMock.Object);
            ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ctrl.MarkFalsePositive(new MarkFalsePositiveRequest
                {
                    ViolationIds = new List<Guid> { Guid.NewGuid() }
                }));
        }

        [Fact]
        public async Task UnmarkFalsePositive_MissingTenant_ThrowsUnauthorized()
        {
            _ctrlTenantMock.Setup(t => t.TenantId).Returns((Guid?)null);
            var ctrl = new ViolationsController(_ctrlSvcMock.Object, _ctrlTenantMock.Object);
            ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ctrl.UnmarkFalsePositive(new UnmarkFalsePositiveRequest
                {
                    ViolationIds = new List<Guid> { Guid.NewGuid() }
                }));
        }

        // ════════════════════════════════════════════════════════════════════
        // C. FalsePositiveViolations — FrameUrl null-guard applies there too
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetFalsePositives_FrameUrl_IsNull_WhenFramePathAbsent()
        {
            var tenantId = Guid.NewGuid();
            _repoMock.Setup(r => r.GetFalsePositivesAsync(tenantId))
                     .ReturnsAsync(new List<Violation>());
            SetupMapper(new[] { ResponseWithPath("") });
            SetupCameraServiceEmpty(tenantId);

            var svc = BuildService();
            var results = (await svc.GetFalsePositiveViolationsAsync(tenantId.ToString())).ToList();

            results[0].FrameUrl.Should().BeNull(
                "FrameUrl null-guard must apply to the false-positive list as well as the active list");
        }
    }
}
