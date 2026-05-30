using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Data.Repositories.Interfaces;
using AlphaSurveilance.DTO.Requests;
using AlphaSurveilance.DTOs.Responses;
using AlphaSurveilance.Services;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services.Interfaces;
using Xunit;

namespace violation_management_api.Tests
{
    /// <summary>
    /// Unit tests for ViolationService — exercises real service code with mocked
    /// repository and camera-service dependencies.
    ///
    /// Sections
    /// ────────────────────────────────────────────────────────────────────
    ///  A. Tenant GUID guard — invalid string must short-circuit before hitting DB
    ///  B. FalsePositive delegation — correct tenantGuid forwarded, invalid guards
    ///  C. Camera-name enrichment — case-insensitive, tenant-scoped
    ///  D. FrameUrl enrichment — null/empty/whitespace → null, valid → pass-through
    ///  E. ProcessViolationsBulkAsync guard paths — null, empty, corrupt, dedup
    /// </summary>
    public class ViolationServiceTests
    {
        private readonly Mock<IViolationRepository> _repoMock = new();
        private readonly Mock<ICameraService> _cameraMock = new();
        private readonly Mock<IMapper> _mapperMock = new();
        private readonly Mock<IMemoryCache> _cacheMock = new();
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
        private readonly Mock<ILogger<ViolationService>> _loggerMock = new();

        private ViolationService Build() => new(
            _repoMock.Object,
            _cameraMock.Object,
            _mapperMock.Object,
            _cacheMock.Object,
            _scopeFactoryMock.Object,
            _loggerMock.Object);

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>Wire mapper to return the given responses regardless of input.</summary>
        private void SetupMapper(IEnumerable<ViolationResponse> responses)
            => _mapperMock
                .Setup(m => m.Map<IEnumerable<ViolationResponse>>(It.IsAny<object>()))
                .Returns(responses);

        // ════════════════════════════════════════════════════════════════════
        // A. Tenant GUID guard
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetViolationsAsync_InvalidTenantString_ReturnsEmpty_RepositoryNeverCalled()
        {
            var svc = Build();
            var result = await svc.GetViolationsAsync("NOT-A-GUID");

            result.Should().BeEmpty();
            _repoMock.Verify(r => r.GetAllAsync(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never,
                "repository must not be called when the tenant ID cannot be parsed as a GUID");
        }

        [Fact]
        public async Task GetViolationsAsync_EmptyString_ReturnsEmpty_RepositoryNeverCalled()
        {
            var svc = Build();
            var result = await svc.GetViolationsAsync("");

            result.Should().BeEmpty();
            _repoMock.Verify(r => r.GetAllAsync(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task GetFalsePositiveViolationsAsync_InvalidTenantString_ReturnsEmpty_RepositoryNeverCalled()
        {
            var svc = Build();
            var result = await svc.GetFalsePositiveViolationsAsync("RUBBISH");

            result.Should().BeEmpty();
            _repoMock.Verify(r => r.GetFalsePositivesAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GetViolationsAsync_ValidGuid_CallsRepositoryWithParsedGuid()
        {
            var tenantId = Guid.NewGuid();
            _repoMock.Setup(r => r.GetAllAsync(tenantId, false))
                     .ReturnsAsync(new List<Violation>());
            SetupMapper(Enumerable.Empty<ViolationResponse>());

            var svc = Build();
            await svc.GetViolationsAsync(tenantId.ToString());

            _repoMock.Verify(r => r.GetAllAsync(tenantId, false), Times.Once,
                "must forward the correctly parsed tenant GUID — if this breaks, cross-tenant data may leak");
        }

        // ════════════════════════════════════════════════════════════════════
        // B. FalsePositive delegation
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task MarkFalsePositiveAsync_InvalidTenantGuid_ReturnsZero_RepositoryNeverCalled()
        {
            var svc = Build();
            var result = await svc.MarkFalsePositiveAsync(
                new List<Guid> { Guid.NewGuid() }, "NOT-A-GUID", "user@test.com", "reason");

            result.Should().Be(0);
            _repoMock.Verify(
                r => r.MarkFalsePositiveAsync(
                    It.IsAny<IEnumerable<Guid>>(), It.IsAny<Guid>(),
                    It.IsAny<string?>(), It.IsAny<string?>()),
                Times.Never,
                "repository must never be called with an unparseable tenant string — " +
                "if this breaks, an attacker could mark another tenant's violations FP");
        }

        [Fact]
        public async Task UnmarkFalsePositiveAsync_InvalidTenantGuid_ReturnsZero_RepositoryNeverCalled()
        {
            var svc = Build();
            var result = await svc.UnmarkFalsePositiveAsync(
                new List<Guid> { Guid.NewGuid() }, "GARBAGE");

            result.Should().Be(0);
            _repoMock.Verify(
                r => r.UnmarkFalsePositiveAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<Guid>()),
                Times.Never);
        }

        [Fact]
        public async Task MarkFalsePositiveAsync_ValidTenant_DelegatesToRepoWithCorrectGuid()
        {
            // Critical: the repo must receive the *parsed* GUID not a new one.
            var tenantId = Guid.NewGuid();
            var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            _repoMock.Setup(r => r.MarkFalsePositiveAsync(ids, tenantId, "u", "r")).ReturnsAsync(2);

            var svc = Build();
            var result = await svc.MarkFalsePositiveAsync(ids, tenantId.ToString(), "u", "r");

            result.Should().Be(2);
            _repoMock.Verify(
                r => r.MarkFalsePositiveAsync(ids, tenantId, "u", "r"), Times.Once);
        }

        [Fact]
        public async Task UnmarkFalsePositiveAsync_ValidTenant_DelegatesToRepoWithCorrectGuid()
        {
            var tenantId = Guid.NewGuid();
            var ids = new List<Guid> { Guid.NewGuid() };
            _repoMock.Setup(r => r.UnmarkFalsePositiveAsync(ids, tenantId)).ReturnsAsync(1);

            var svc = Build();
            var result = await svc.UnmarkFalsePositiveAsync(ids, tenantId.ToString());

            result.Should().Be(1);
            _repoMock.Verify(r => r.UnmarkFalsePositiveAsync(ids, tenantId), Times.Once);
        }

        // ════════════════════════════════════════════════════════════════════
        // C. Camera-name enrichment — tenant-scoped and case-insensitive
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetViolationsAsync_EnrichesCameraName_CaseInsensitive()
        {
            // The camera slug in the violation is lowercase, but the camera row uses uppercase.
            // The service's cameraMap uses OrdinalIgnoreCase, so both must resolve.
            var tenantId = Guid.NewGuid();
            var violation = new Violation { TenantId = tenantId, CameraId = "cam-lobby", CorrelationId = "c1" };
            _repoMock.Setup(r => r.GetAllAsync(tenantId, false)).ReturnsAsync(new[] { violation });

            // Mapper returns a response whose CameraId matches the violation
            SetupMapper(new[] { new ViolationResponse { CameraId = "cam-lobby" } });

            // Camera service returns cameras for the SAME tenant — uppercase slug
            _cameraMock
                .Setup(c => c.GetCamerasByTenantAsync(tenantId))
                .ReturnsAsync(new List<CameraResponse>
                {
                    new() { Id = Guid.NewGuid(), CameraId = "CAM-LOBBY", Name = "Main Lobby" }
                });

            var results = (await Build().GetViolationsAsync(tenantId.ToString())).ToList();

            results.Single().CameraName.Should().Be("Main Lobby",
                "case-insensitive CameraId match must enrich CameraName correctly");
        }

        [Fact]
        public async Task GetViolationsAsync_DoesNotEnrichCameraName_WhenCameraServiceReturnsNoCameras()
        {
            // This models the cross-tenant scenario: camera service for the given tenant
            // returns nothing, so CameraName must remain null.
            var tenantId = Guid.NewGuid();
            _repoMock.Setup(r => r.GetAllAsync(tenantId, false))
                     .ReturnsAsync(new[] { new Violation { TenantId = tenantId, CameraId = "CAM-X", CorrelationId = "c2" } });
            SetupMapper(new[] { new ViolationResponse { CameraId = "CAM-X" } });
            _cameraMock
                .Setup(c => c.GetCamerasByTenantAsync(tenantId))
                .ReturnsAsync(new List<CameraResponse>()); // empty — wrong tenant, different data partition

            var results = (await Build().GetViolationsAsync(tenantId.ToString())).ToList();

            results.Single().CameraName.Should().BeNull(
                "camera name must not be populated from a different tenant's cameras");
        }

        [Fact]
        public async Task GetViolationsAsync_CameraServiceCalledWithCorrectTenantId()
        {
            // Regression guard: if the service ever passes the wrong tenantId to
            // GetCamerasByTenantAsync, cameras from another tenant could bleed in.
            var tenantId = Guid.NewGuid();
            _repoMock.Setup(r => r.GetAllAsync(tenantId, false))
                     .ReturnsAsync(new[] { new Violation { TenantId = tenantId, CorrelationId = "c3" } });
            SetupMapper(new[] { new ViolationResponse() });
            _cameraMock.Setup(c => c.GetCamerasByTenantAsync(tenantId))
                       .ReturnsAsync(new List<CameraResponse>());

            await Build().GetViolationsAsync(tenantId.ToString());

            _cameraMock.Verify(
                c => c.GetCamerasByTenantAsync(tenantId), Times.Once,
                "camera service must be called with the SAME tenantId used to fetch violations");
            _cameraMock.Verify(
                c => c.GetCamerasByTenantAsync(It.Is<Guid>(g => g != tenantId)), Times.Never,
                "camera service must NEVER be called with a different tenantId");
        }

        // ════════════════════════════════════════════════════════════════════
        // D. FrameUrl enrichment
        // ════════════════════════════════════════════════════════════════════

        private async Task<ViolationResponse> FetchSingleResponseWithFramePath(string? framePath)
        {
            var tenantId = Guid.NewGuid();
            _repoMock.Setup(r => r.GetAllAsync(tenantId, false))
                     .ReturnsAsync(new[] { new Violation { TenantId = tenantId, CorrelationId = "x" } });
            SetupMapper(new[] { new ViolationResponse { FramePath = framePath } });
            _cameraMock.Setup(c => c.GetCamerasByTenantAsync(tenantId))
                       .ReturnsAsync(new List<CameraResponse>());
            return (await Build().GetViolationsAsync(tenantId.ToString())).Single();
        }

        [Fact]
        public async Task GetViolations_FrameUrl_IsNull_WhenFramePathIsNull()
            => (await FetchSingleResponseWithFramePath(null))
                .FrameUrl.Should().BeNull("null FramePath → null FrameUrl, never empty string");

        [Fact]
        public async Task GetViolations_FrameUrl_IsNull_WhenFramePathIsEmptyString()
            => (await FetchSingleResponseWithFramePath(""))
                .FrameUrl.Should().BeNull("empty-string FramePath → null FrameUrl");

        [Fact]
        public async Task GetViolations_FrameUrl_IsNull_WhenFramePathIsWhitespace()
            => (await FetchSingleResponseWithFramePath("   "))
                .FrameUrl.Should().BeNull("whitespace-only FramePath → null FrameUrl");

        [Fact]
        public async Task GetViolations_FrameUrl_EqualFramePath_WhenPathIsValidUrl()
        {
            const string url = "https://bucket.s3.amazonaws.com/violations/frame.jpg";
            (await FetchSingleResponseWithFramePath(url))
                .FrameUrl.Should().Be(url, "valid FramePath must be preserved verbatim");
        }

        // ════════════════════════════════════════════════════════════════════
        // E. ProcessViolationsBulkAsync guard paths
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task ProcessViolationsBulkAsync_Null_ReturnsZero_NoRepositoryCall()
        {
            var svc = Build();
            var result = await svc.ProcessViolationsBulkAsync((IEnumerable<ViolationPayload>)null!);

            result.Should().Be(0);
            _repoMock.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<Violation>>()), Times.Never);
            _repoMock.Verify(r => r.AddOutboxMessagesAsync(It.IsAny<IEnumerable<OutboxMessage>>()), Times.Never);
        }

        [Fact]
        public async Task ProcessViolationsBulkAsync_EmptyList_ReturnsZero_NoRepositoryCall()
        {
            var svc = Build();
            var result = await svc.ProcessViolationsBulkAsync(new List<ViolationPayload>());

            result.Should().Be(0);
            _repoMock.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<Violation>>()), Times.Never);
        }

        [Fact]
        public async Task ProcessViolationsBulkAsync_AllItemsHaveBlankCorrelationId_ReturnsZero()
        {
            // Items without a CorrelationId are dropped before any DB call.
            var svc = Build();
            var result = await svc.ProcessViolationsBulkAsync(new[]
            {
                new ViolationPayload { TenantId = Guid.NewGuid().ToString(), CorrelationId = "" },
                new ViolationPayload { TenantId = Guid.NewGuid().ToString(), CorrelationId = "   " }
            });

            result.Should().Be(0);
            _repoMock.Verify(r => r.GetExistingCorrelationIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never,
                "correlation-id lookup must not fire for invalid payloads");
        }

        [Fact]
        public async Task ProcessViolationsBulkAsync_AllItemsHaveBlankTenantId_ReturnsZero()
        {
            var svc = Build();
            var result = await svc.ProcessViolationsBulkAsync(new[]
            {
                new ViolationPayload { TenantId = "", CorrelationId = "corr-1" },
                new ViolationPayload { TenantId = "   ", CorrelationId = "corr-2" }
            });

            result.Should().Be(0);
            _repoMock.Verify(r => r.GetExistingCorrelationIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
        }

        [Fact]
        public async Task ProcessViolationsBulkAsync_AllDuplicateCorrelationIds_ReturnsZero_AddNeverCalled()
        {
            // When every CorrelationId is already in the DB, the service must early-exit
            // after the dedup check without calling AddRangeAsync.
            var tenantId = Guid.NewGuid().ToString();
            var payload = new ViolationPayload
            {
                TenantId = tenantId,
                CorrelationId = "EXISTING-CORR",
                Timestamp = DateTime.UtcNow
            };

            _repoMock
                .Setup(r => r.GetExistingCorrelationIdsAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new[] { "EXISTING-CORR" });

            var svc = Build();
            var result = await svc.ProcessViolationsBulkAsync(new[] { payload });

            result.Should().Be(0);
            _repoMock.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<Violation>>()), Times.Never,
                "duplicate payloads must not be inserted — deduplication must prevent double-writes");
        }
    }
}
