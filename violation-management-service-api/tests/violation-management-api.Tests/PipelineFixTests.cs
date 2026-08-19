using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;
using AlphaSurveilance.Data;
using AlphaSurveilance.Data.Repositories;
using AlphaSurveilance.Data.Repositories.Interfaces;
using AlphaSurveilance.DTO.Requests;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.DTOs.Responses;
using AlphaSurveilance.Mappings;
using AlphaSurveilance.Services;
using AlphaSurveilance.Services.Interfaces;
using Amazon.S3;
using Amazon.S3.Model;
using AutoMapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using violation_management_api.Services.Interfaces;
using Xunit;

namespace violation_management_api.Tests
{
    /// <summary>
    /// Tests for the end-to-end pipeline fixes.
    ///
    /// Sections
    /// ────────────────────────────────────────────────────────────────────
    ///  P1. Internal update lifecycle — GET active (cameraId, trackId) + PATCH last-seen
    ///  P5. Hub notification payload — Type / Severity must be serialized
    ///  P6. Timestamp UTC conversion — Local in → UTC out, UTC unchanged
    ///  P7. S3 pre-sign — URL detection, presign, fallback-on-failure
    ///  P8. Outbox retry backoff — exponential schedule + query eligibility
    ///  Extra: bounded violation list (limit/offset paging)
    /// </summary>
    public class PipelineFixTests
    {
        // ════════════════════════════════════════════════════════════════════
        // Shared service-layer harness (mocked repository / camera service)
        // ════════════════════════════════════════════════════════════════════

        private readonly Mock<IViolationRepository> _repoMock = new();
        private readonly Mock<ICameraService> _cameraMock = new();
        private readonly Mock<IMapper> _mapperMock = new();
        private readonly Mock<IMemoryCache> _cacheMock = new();
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
        private readonly Mock<IFramePresignService> _presignMock = new();

        public PipelineFixTests()
        {
            _presignMock
                .Setup(p => p.GetPresignedUrl(It.IsAny<string?>()))
                .Returns<string?>(path => path);
        }

        private ViolationService BuildService() => new(
            _repoMock.Object,
            _cameraMock.Object,
            _mapperMock.Object,
            _cacheMock.Object,
            _scopeFactoryMock.Object,
            _presignMock.Object,
            NullLogger<ViolationService>.Instance);

        // ════════════════════════════════════════════════════════════════════
        // P1a. GET internal/active — service layer
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetActiveViolation_UnknownTrack_ReturnsNull()
        {
            // Negative case: the vision service polls for a track that never
            // produced a violation — the service must return null (controller → 404).
            _repoMock.Setup(r => r.GetActiveByTrackAsync("cam-guid", 42))
                     .ReturnsAsync((Violation?)null);

            var result = await BuildService().GetActiveViolationAsync("cam-guid", 42);

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetActiveViolation_EmptyCameraId_ReturnsNull_WithoutHittingRepository()
        {
            var result = await BuildService().GetActiveViolationAsync("", 42);

            result.Should().BeNull();
            _repoMock.Verify(r => r.GetActiveByTrackAsync(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }

        [Fact]
        public async Task GetActiveViolation_KnownTrack_ReturnsMappedResponse()
        {
            var violation = new Violation
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                CameraId = "cam-guid",
                TrackId = 7,
                CorrelationId = "corr-1",
                Timestamp = DateTime.UtcNow
            };
            _repoMock.Setup(r => r.GetActiveByTrackAsync("cam-guid", 7)).ReturnsAsync(violation);
            _mapperMock.Setup(m => m.Map<ViolationResponse>(violation))
                       .Returns(new ViolationResponse { Id = violation.Id, TrackId = 7 });

            var result = await BuildService().GetActiveViolationAsync("cam-guid", 7);

            result.Should().NotBeNull();
            result!.Id.Should().Be(violation.Id);
            result.TrackId.Should().Be(7);
        }

        // ════════════════════════════════════════════════════════════════════
        // P1b. PATCH internal/{id} — service layer
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task UpdateViolationLifecycle_NotFound_ReturnsFalse_AndDoesNotSave()
        {
            _repoMock.Setup(r => r.GetByIdInternalAsync(It.IsAny<Guid>()))
                     .ReturnsAsync((Violation?)null);

            var ok = await BuildService().UpdateViolationLifecycleAsync(
                Guid.NewGuid(), new InternalViolationUpdateRequest { Timestamp = DateTimeOffset.UtcNow });

            ok.Should().BeFalse();
            _repoMock.Verify(r => r.SaveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task UpdateViolationLifecycle_UpdatesLastSeen_FromIsoOffsetTimestamp()
        {
            // The Python client sends {"Timestamp": "<iso with +00:00 offset>"}.
            var violation = NewPendingViolation();
            _repoMock.Setup(r => r.GetByIdInternalAsync(violation.Id)).ReturnsAsync(violation);

            var patchTime = DateTimeOffset.Parse("2026-07-11T10:15:30+00:00", CultureInfo.InvariantCulture);
            var ok = await BuildService().UpdateViolationLifecycleAsync(
                violation.Id, new InternalViolationUpdateRequest { Timestamp = patchTime });

            ok.Should().BeTrue();
            violation.LastSeenAt.Should().Be(patchTime.UtcDateTime);
            violation.Status.Should().Be(AuditStatus.Pending, "no status was supplied — must not change");
            _repoMock.Verify(r => r.UpdateAsync(violation), Times.Once);
            _repoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task UpdateViolationLifecycle_MissingTimestamp_StampsServerUtcNow()
        {
            var violation = NewPendingViolation();
            _repoMock.Setup(r => r.GetByIdInternalAsync(violation.Id)).ReturnsAsync(violation);

            var before = DateTime.UtcNow;
            var ok = await BuildService().UpdateViolationLifecycleAsync(
                violation.Id, new InternalViolationUpdateRequest());
            var after = DateTime.UtcNow;

            ok.Should().BeTrue();
            violation.LastSeenAt.Should().NotBeNull();
            violation.LastSeenAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        }

        [Fact]
        public async Task UpdateViolationLifecycle_ValidStatus_IsApplied_CaseInsensitively()
        {
            var violation = NewPendingViolation();
            _repoMock.Setup(r => r.GetByIdInternalAsync(violation.Id)).ReturnsAsync(violation);

            var ok = await BuildService().UpdateViolationLifecycleAsync(
                violation.Id, new InternalViolationUpdateRequest { Status = "audited" });

            ok.Should().BeTrue();
            violation.Status.Should().Be(AuditStatus.Audited);
        }

        [Fact]
        public async Task UpdateViolationLifecycle_InvalidStatus_IsIgnored()
        {
            var violation = NewPendingViolation();
            _repoMock.Setup(r => r.GetByIdInternalAsync(violation.Id)).ReturnsAsync(violation);

            var ok = await BuildService().UpdateViolationLifecycleAsync(
                violation.Id, new InternalViolationUpdateRequest { Status = "definitely-not-a-status" });

            ok.Should().BeTrue("a heartbeat with a bad status string must still refresh LastSeenAt");
            violation.Status.Should().Be(AuditStatus.Pending);
        }

        private static Violation NewPendingViolation() => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow.AddMinutes(-5),
            Status = AuditStatus.Pending
        };

        // ════════════════════════════════════════════════════════════════════
        // P5. Hub notification payload — Type / Severity
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task CreateViolation_HubNotification_IncludesTypeAndSeverity()
        {
            // OutboxProcessorService.NotificationPayload declares Type/Severity,
            // and the live-feed UI renders `type` / `severity` from the SignalR
            // event. The serialized HubNotification content must therefore carry
            // meaningful values, not be silently absent.
            var violation = new Violation
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                CameraId = "cam-1",
                CorrelationId = "corr-hub",
                Timestamp = DateTime.UtcNow,
                SopViolationType = new violation_management_api.Core.Entities.SopViolationType
                {
                    Id = Guid.NewGuid(),
                    Name = "No Hardhat",
                    ModelIdentifier = "ppe-v1",
                    Sop = new violation_management_api.Core.Entities.Sop { Id = Guid.NewGuid(), Name = "Construction Safety" }
                }
            };

            _mapperMock.Setup(m => m.Map<Violation>(It.IsAny<ViolationRequest>())).Returns(violation);
            _mapperMock.Setup(m => m.Map<ViolationResponse>(violation)).Returns(new ViolationResponse { Id = violation.Id });

            // Simulate an active email cooldown so the email branch (which needs
            // a real DbContext scope) is skipped — we only care about the hub message.
            object? cached = new();
            _cacheMock.Setup(c => c.TryGetValue(It.IsAny<object>(), out cached)).Returns(true);

            List<OutboxMessage> captured = new();
            _repoMock.Setup(r => r.AddOutboxMessagesAsync(It.IsAny<IEnumerable<OutboxMessage>>()))
                     .Callback<IEnumerable<OutboxMessage>>(m => captured = m.ToList())
                     .Returns(Task.CompletedTask);

            await BuildService().CreateViolationAsync(new ViolationRequest
            {
                TenantId = violation.TenantId.ToString(),
                CorrelationId = "corr-hub",
                Timestamp = violation.Timestamp
            });

            var hub = captured.SingleOrDefault(m => m.Type == "HubNotification");
            hub.Should().NotBeNull("a HubNotification outbox message must be produced for every violation");

            using var doc = JsonDocument.Parse(hub!.Content);
            var root = doc.RootElement;

            root.GetProperty("Type").GetString().Should().Be("No Hardhat",
                "Type must carry the violation type name so the UI's `type` field is not blank");
            root.GetProperty("Severity").GetString().Should().Be("Medium",
                "Severity defaults to Medium until severity becomes a first-class SOP field");
            root.GetProperty("SopName").GetString().Should().Be("Construction Safety");
            root.GetProperty("ViolationTypeName").GetString().Should().Be("No Hardhat");
            root.GetProperty("CameraId").GetString().Should().Be("cam-1");
        }

        // ════════════════════════════════════════════════════════════════════
        // P6. Timestamp UTC conversion
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public void ToUtc_LocalKind_ConvertsInstantCorrectly()
        {
            // System.Text.Json parses "2026-07-11T10:00:00+00:00" into a
            // Local-kind DateTime shifted to server-local wall time. The old
            // SpecifyKind(..., Utc) relabelled that local wall time as UTC,
            // skewing every violation by the server's UTC offset.
            var local = new DateTime(2026, 7, 11, 15, 0, 0, DateTimeKind.Local);

            var result = MappingProfile.ToUtc(local);

            result.Kind.Should().Be(DateTimeKind.Utc);
            result.Should().Be(local.ToUniversalTime(),
                "conversion must preserve the instant, not just relabel the kind");
        }

        [Fact]
        public void ToUtc_UtcKind_IsUnchanged()
        {
            var utc = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);

            var result = MappingProfile.ToUtc(utc);

            result.Should().Be(utc);
            result.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Fact]
        public void ToUtc_UnspecifiedKind_IsAssumedUtc_WithoutShifting()
        {
            var unspecified = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Unspecified);

            var result = MappingProfile.ToUtc(unspecified);

            result.Kind.Should().Be(DateTimeKind.Utc);
            result.Ticks.Should().Be(unspecified.Ticks, "Unspecified is assumed UTC — no wall-clock shift");
        }

        [Fact]
        public void PayloadMapping_LocalTimestamp_ProducesCorrectUtcInstant()
        {
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
            var local = new DateTime(2026, 7, 11, 15, 0, 0, DateTimeKind.Local);
            var payload = new ViolationPayload
            {
                TenantId = Guid.NewGuid().ToString(),
                CorrelationId = "corr-ts",
                Timestamp = local
            };

            var violation = mapper.Map<Violation>(payload);

            violation.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
            violation.Timestamp.Should().Be(local.ToUniversalTime());
            violation.LastSeenAt.Should().Be(local.ToUniversalTime(),
                "a new violation was last seen at its own detection time");
        }

        [Fact]
        public void PayloadMapping_UtcTimestamp_IsNotShifted()
        {
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
            var utc = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
            var payload = new ViolationPayload
            {
                TenantId = Guid.NewGuid().ToString(),
                CorrelationId = "corr-ts2",
                Timestamp = utc
            };

            var violation = mapper.Map<Violation>(payload);

            violation.Timestamp.Should().Be(utc, "already-UTC values must pass through unchanged");
        }

        [Theory]
        [InlineData("Pending", AuditStatus.Pending)]
        [InlineData("pending", AuditStatus.Pending)]
        [InlineData("Audited", AuditStatus.Audited)]
        [InlineData("FailedAudit", AuditStatus.FailedAudit)]
        [InlineData("not-a-status", AuditStatus.Pending)]
        [InlineData(null, AuditStatus.Pending)]
        [InlineData("", AuditStatus.Pending)]
        public void PayloadMapping_StatusString_ParsesWithPendingFallback(string? status, AuditStatus expected)
        {
            MappingProfile.ParseStatus(status).Should().Be(expected);
        }

        [Fact]
        public void PayloadMapping_TrackId_IsMapped()
        {
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
            var payload = new ViolationPayload
            {
                TenantId = Guid.NewGuid().ToString(),
                CorrelationId = "corr-track",
                Timestamp = DateTime.UtcNow,
                TrackId = 1234,
                Status = "Pending"
            };

            var violation = mapper.Map<Violation>(payload);

            violation.TrackId.Should().Be(1234);
            violation.Status.Should().Be(AuditStatus.Pending);
        }

        // ════════════════════════════════════════════════════════════════════
        // P7. S3 pre-sign — parsing, presigning, resilience
        // ════════════════════════════════════════════════════════════════════

        private static IConfiguration PresignConfig() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["S3Config:BucketName"] = "alphasurveilance-dev-1",
                ["S3Config:PresignExpiryHours"] = "24"
            })
            .Build();

        [Theory]
        [InlineData("https://my-bucket.s3.us-east-1.amazonaws.com/frames/f1.jpg", "my-bucket", "frames/f1.jpg")]
        [InlineData("https://my-bucket.s3.amazonaws.com/f1.jpg", "my-bucket", "f1.jpg")]
        [InlineData("https://s3.us-east-1.amazonaws.com/my-bucket/frames/f1.jpg", "my-bucket", "frames/f1.jpg")]
        public void TryParseS3Url_RecognisesS3ObjectUrls(string url, string expectedBucket, string expectedKey)
        {
            var ok = S3FramePresignService.TryParseS3Url(url, out var bucket, out var key);

            ok.Should().BeTrue();
            bucket.Should().Be(expectedBucket);
            key.Should().Be(expectedKey);
        }

        [Theory]
        [InlineData("https://res.cloudinary.com/demo/image/upload/frame.jpg")]
        [InlineData("https://example.com/frames/f1.jpg")]
        [InlineData("not a url at all")]
        [InlineData("/local/relative/path.jpg")]
        public void TryParseS3Url_RejectsNonS3Paths(string url)
        {
            S3FramePresignService.TryParseS3Url(url, out _, out _).Should().BeFalse();
        }

        [Fact]
        public void GetPresignedUrl_S3Url_ReturnsPresignedUrl()
        {
            var s3Mock = new Mock<IAmazonS3>();
            GetPreSignedUrlRequest? seen = null;
            s3Mock.Setup(s => s.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
                  .Callback<GetPreSignedUrlRequest>(r => seen = r)
                  .Returns("https://signed.example/frames/f1.jpg?X-Amz-Signature=abc");

            var svc = new S3FramePresignService(s3Mock.Object, PresignConfig(), NullLogger<S3FramePresignService>.Instance);
            var result = svc.GetPresignedUrl("https://my-bucket.s3.us-east-1.amazonaws.com/frames/f1.jpg");

            result.Should().Be("https://signed.example/frames/f1.jpg?X-Amz-Signature=abc");
            seen.Should().NotBeNull();
            seen!.BucketName.Should().Be("my-bucket");
            seen.Key.Should().Be("frames/f1.jpg");
            seen.Verb.Should().Be(HttpVerb.GET);
        }

        [Fact]
        public void GetPresignedUrl_PresignThrows_FallsBackToRawPath()
        {
            // Resilience requirement: an SDK failure must never break the read path.
            var s3Mock = new Mock<IAmazonS3>();
            s3Mock.Setup(s => s.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
                  .Throws(new AmazonS3Exception("simulated presign failure"));

            var raw = "https://my-bucket.s3.us-east-1.amazonaws.com/frames/f1.jpg";
            var svc = new S3FramePresignService(s3Mock.Object, PresignConfig(), NullLogger<S3FramePresignService>.Instance);

            var result = svc.GetPresignedUrl(raw);

            result.Should().Be(raw, "on presign failure the raw path must be returned, not an exception");
        }

        [Fact]
        public void GetPresignedUrl_NonS3Path_PassesThroughWithoutTouchingS3()
        {
            var s3Mock = new Mock<IAmazonS3>(MockBehavior.Strict); // any call would throw
            var svc = new S3FramePresignService(s3Mock.Object, PresignConfig(), NullLogger<S3FramePresignService>.Instance);

            var cloudinary = "https://res.cloudinary.com/demo/image/upload/frame.jpg";
            svc.GetPresignedUrl(cloudinary).Should().Be(cloudinary);
            svc.GetPresignedUrl(null).Should().BeNull();
            svc.GetPresignedUrl("").Should().Be("");
        }

        // ════════════════════════════════════════════════════════════════════
        // P8. Outbox retry backoff
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public void OutboxRetryDelay_FollowsExponentialSchedule_CappedAtOneHour()
        {
            ViolationRepository.GetRetryDelay(0).Should().Be(TimeSpan.FromSeconds(30));
            ViolationRepository.GetRetryDelay(1).Should().Be(TimeSpan.FromMinutes(2));
            ViolationRepository.GetRetryDelay(2).Should().Be(TimeSpan.FromMinutes(10));
            ViolationRepository.GetRetryDelay(3).Should().Be(TimeSpan.FromMinutes(30));
            ViolationRepository.GetRetryDelay(4).Should().Be(TimeSpan.FromHours(1));
            ViolationRepository.GetRetryDelay(7).Should().Be(TimeSpan.FromHours(1), "delay is capped at 1 hour");
            ViolationRepository.GetRetryDelay(100).Should().Be(TimeSpan.FromHours(1));
        }

        [Fact]
        public async Task OutboxQuery_SelectsMessagesAccordingToBackoffSchedule()
        {
            using var harness = new SqliteDbHarness();
            using var db = harness.BuildDb();
            var now = DateTime.UtcNow;

            var neverAttempted   = OutboxMsg(0, null, now.AddMinutes(-10));
            var retry1Due        = OutboxMsg(1, now.AddMinutes(-3), now.AddMinutes(-10));  // needs 2m  → due
            var retry1NotDue     = OutboxMsg(1, now.AddMinutes(-1), now.AddMinutes(-10));  // needs 2m  → not due
            var retry4NotDue     = OutboxMsg(4, now.AddMinutes(-30), now.AddHours(-2));    // needs 1h  → not due
            var retry7DueCapped  = OutboxMsg(7, now.AddHours(-2), now.AddHours(-3));       // cap 1h    → due
            var exhausted        = OutboxMsg(8, now.AddHours(-5), now.AddHours(-6));       // >= max(8) → never
            var processed        = OutboxMsg(0, null, now.AddMinutes(-10));
            processed.ProcessedAt = now;

            db.OutboxMessages.AddRange(neverAttempted, retry1Due, retry1NotDue, retry4NotDue, retry7DueCapped, exhausted, processed);
            await db.SaveChangesAsync();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["OutboxConfig:MaxRetryCount"] = "8" })
                .Build();
            var repo = new ViolationRepository(db, config);

            var eligible = (await repo.GetUnprocessedOutboxMessagesAsync(50)).Select(m => m.Id).ToList();

            eligible.Should().Contain(neverAttempted.Id, "messages never attempted are always due");
            eligible.Should().Contain(retry1Due.Id, "1 failure + 3 minutes elapsed exceeds the 2-minute tier");
            eligible.Should().Contain(retry7DueCapped.Id, "beyond tier 4 the delay caps at 1 hour");
            eligible.Should().NotContain(retry1NotDue.Id, "only 1 minute elapsed of the 2-minute tier");
            eligible.Should().NotContain(retry4NotDue.Id, "tier 4+ requires a full hour between attempts");
            eligible.Should().NotContain(exhausted.Id, "RetryCount at MaxRetryCount(8) is permanently parked");
            eligible.Should().NotContain(processed.Id, "processed messages are never retried");
        }

        private static OutboxMessage OutboxMsg(int retryCount, DateTime? lastAttemptAt, DateTime createdAt) => new()
        {
            Id = Guid.NewGuid(),
            Type = "EmailAlert",
            Content = "{}",
            CreatedAt = createdAt,
            RetryCount = retryCount,
            LastAttemptAt = lastAttemptAt
        };

        // ════════════════════════════════════════════════════════════════════
        // P1c + paging — repository behaviour against real SQL (SQLite in-memory)
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetActiveByTrack_UnknownTrack_ReturnsNull()
        {
            using var harness = new SqliteDbHarness();
            using var db = harness.BuildDb();
            var repo = new ViolationRepository(db, Mock.Of<IConfiguration>());

            var result = await repo.GetActiveByTrackAsync("cam-x", 999);

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetActiveByTrack_ReturnsMostRecentPending_SkippingResolvedAndFalsePositives()
        {
            using var harness = new SqliteDbHarness();
            using var db = harness.BuildDb();
            var tenant = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var older      = TrackedViolation(tenant, "cam-1", 5, now.AddMinutes(-10));
            var newest     = TrackedViolation(tenant, "cam-1", 5, now.AddMinutes(-1));
            var audited    = TrackedViolation(tenant, "cam-1", 5, now);
            audited.Status = AuditStatus.Audited;
            var fp         = TrackedViolation(tenant, "cam-1", 5, now);
            fp.IsFalsePositive = true;
            var otherTrack = TrackedViolation(tenant, "cam-1", 6, now);
            var otherCam   = TrackedViolation(tenant, "cam-2", 5, now);

            db.Violations.AddRange(older, newest, audited, fp, otherTrack, otherCam);
            await db.SaveChangesAsync();

            var repo = new ViolationRepository(db, Mock.Of<IConfiguration>());
            var result = await repo.GetActiveByTrackAsync("cam-1", 5);

            result.Should().NotBeNull();
            result!.Id.Should().Be(newest.Id,
                "must return the most recent Pending, non-FP violation for the exact (cameraId, trackId) pair");
        }

        [Fact]
        public async Task GetAllAsync_RespectsLimitAndOffset_NewestFirst()
        {
            using var harness = new SqliteDbHarness();
            using var db = harness.BuildDb();
            var tenant = Guid.NewGuid();
            var now = DateTime.UtcNow;

            for (var i = 0; i < 10; i++)
            {
                db.Violations.Add(TrackedViolation(tenant, "cam-1", i, now.AddMinutes(-i)));
            }
            await db.SaveChangesAsync();

            var repo = new ViolationRepository(db, Mock.Of<IConfiguration>());

            var limited = (await repo.GetAllAsync(tenant, false, limit: 3)).ToList();
            limited.Should().HaveCount(3);
            limited.Select(v => v.TrackId).Should().ContainInOrder(new long?[] { 0, 1, 2 },
                "newest violations (smallest age) come first");

            var paged = (await repo.GetAllAsync(tenant, false, limit: 3, offset: 3)).ToList();
            paged.Select(v => v.TrackId).Should().ContainInOrder(new long?[] { 3, 4, 5 });

            var unbounded = (await repo.GetAllAsync(tenant)).ToList();
            unbounded.Should().HaveCount(10, "omitting limit preserves the legacy return-everything behaviour");
        }

        private static Violation TrackedViolation(Guid tenantId, string cameraId, long trackId, DateTime timestamp) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CameraId = cameraId,
            TrackId = trackId,
            CorrelationId = Guid.NewGuid().ToString(),
            Timestamp = timestamp,
            Status = AuditStatus.Pending
        };

        /// <summary>
        /// SQLite in-memory database harness (same pattern as ViolationRepositoryTests):
        /// a real SQL provider is required for faithful query translation.
        /// </summary>
        private sealed class SqliteDbHarness : IDisposable
        {
            private readonly SqliteConnection _connection;

            public SqliteDbHarness()
            {
                _connection = new SqliteConnection("DataSource=:memory:");
                _connection.Open();
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "PRAGMA foreign_keys = OFF;";
                cmd.ExecuteNonQuery();
            }

            public AppViolationDbContext BuildDb()
            {
                var db = new AppViolationDbContext(
                    new DbContextOptionsBuilder<AppViolationDbContext>()
                        .UseSqlite(_connection)
                        .Options);
                db.Database.EnsureCreated();
                return db;
            }

            public void Dispose() => _connection.Dispose();
        }
    }
}
