using System;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;
using AlphaSurveilance.DTO.Requests;
using AlphaSurveilance.DTOs.Responses;
using AlphaSurveilance.Mappings;
using AutoMapper;
using FluentAssertions;
using violation_management_api.Core.Entities;
using Xunit;

namespace violation_management_api.Tests
{
    /// <summary>
    /// Tests for MappingProfile — every explicitly custom ForMember rule that
    /// could silently break if someone edits MappingProfile.cs.
    ///
    /// Sections
    /// ────────────────────────────────────────────────────────────────────
    ///  1. ViolationPayload → Violation : UTC enforcement, FP isolation
    ///  2. Violation → ViolationResponse : null-nav defaults, FrameUrl ignored
    ///  3. Structural check (keep AutoMapper config valid guard)
    /// </summary>
    public class MappingProfileTests
    {
        private readonly IMapper _mapper;

        public MappingProfileTests()
        {
            var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
            _mapper = config.CreateMapper();
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. ViolationPayload → Violation
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public void ViolationPayload_To_Violation_Timestamp_IsAlwaysUtc()
        {
            // The mapping has: DateTime.SpecifyKind(src.Timestamp, DateTimeKind.Utc)
            // If someone removes this, violations saved with Local kind cause
            // timezone bugs in analytics and front-end display.
            var localTimestamp = new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Local);
            var payload = new ViolationPayload
            {
                TenantId = Guid.NewGuid().ToString(),
                CorrelationId = "corr-utc",
                Timestamp = localTimestamp
            };

            var result = _mapper.Map<Violation>(payload);

            result.Timestamp.Kind.Should().Be(DateTimeKind.Utc,
                "mapping must enforce UTC kind regardless of the source DateTimeKind");
        }

        [Fact]
        public void ViolationPayload_To_Violation_IsFalsePositive_AlwaysFalse()
        {
            // IsFalsePositive is Ignored in the mapping; the vision service
            // must NEVER be able to inject a pre-flagged false-positive.
            // This is a security rule: only the human review endpoint can set it.
            var payload = new ViolationPayload
            {
                TenantId = Guid.NewGuid().ToString(),
                CorrelationId = "corr-fp"
            };

            var result = _mapper.Map<Violation>(payload);

            result.IsFalsePositive.Should().BeFalse(
                "IsFalsePositive must be Ignored in the mapping — " +
                "if someone removes the .Ignore() the vision service could bypass the review workflow");
        }

        [Fact]
        public void ViolationPayload_To_Violation_FalsePositiveAuditFields_AreNull()
        {
            // FalsePositiveMarkedAt/By/Reason are all Ignored.
            // Removing any of these .Ignore() calls would let the payload set them.
            var payload = new ViolationPayload
            {
                TenantId = Guid.NewGuid().ToString(),
                CorrelationId = "corr-fp2"
            };

            var result = _mapper.Map<Violation>(payload);

            result.FalsePositiveMarkedAt.Should().BeNull();
            result.FalsePositiveMarkedBy.Should().BeNull();
            result.FalsePositiveReason.Should().BeNull();
        }

        [Fact]
        public void ViolationPayload_To_Violation_TenantId_ParsedAsGuid()
        {
            var tenantGuid = Guid.NewGuid();
            var payload = new ViolationPayload
            {
                TenantId = tenantGuid.ToString(),
                CorrelationId = "corr-tenant"
            };

            var result = _mapper.Map<Violation>(payload);

            result.TenantId.Should().Be(tenantGuid,
                "TenantId string must be parsed to the correct Guid value");
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. Violation → ViolationResponse
        // ════════════════════════════════════════════════════════════════════

        private static Violation MinimalViolation() => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CorrelationId = "c",
            Timestamp = DateTime.UtcNow
        };

        [Fact]
        public void Violation_To_Response_SopName_DefaultsToGeneric_WhenSopViolationTypeIsNull()
        {
            var v = MinimalViolation();
            v.SopViolationType = null;

            var response = _mapper.Map<ViolationResponse>(v);

            response.SopName.Should().Be("Generic",
                "SopName must fall back to 'Generic' when SopViolationType is null; " +
                "changing this default breaks the UI's violation type badge");
        }

        [Fact]
        public void Violation_To_Response_ViolationTypeName_DefaultsToGeneric_WhenSopViolationTypeIsNull()
        {
            var v = MinimalViolation();
            v.SopViolationType = null;

            var response = _mapper.Map<ViolationResponse>(v);

            response.ViolationTypeName.Should().Be("Generic");
        }

        [Fact]
        public void Violation_To_Response_ModelIdentifier_DefaultsToUnknown_WhenSopViolationTypeIsNull()
        {
            var v = MinimalViolation();
            v.SopViolationType = null;

            var response = _mapper.Map<ViolationResponse>(v);

            response.ModelIdentifier.Should().Be("Unknown",
                "ModelIdentifier must default to 'Unknown'; changing breaks per-model analytics");
        }

        [Fact]
        public void Violation_To_Response_SopName_DefaultsToGeneric_WhenSopIsNull()
        {
            // SopViolationType exists but its parent Sop is null.
            var v = MinimalViolation();
            v.SopViolationType = new SopViolationType
            {
                Id = Guid.NewGuid(),
                Name = "No Hardhat",
                ModelIdentifier = "ppe-v1",
                Sop = null   // <── null Sop
            };

            var response = _mapper.Map<ViolationResponse>(v);

            response.SopName.Should().Be("Generic",
                "SopName must fall back to 'Generic' when Sop navigation property is null");
            response.ViolationTypeName.Should().Be("No Hardhat",
                "ViolationTypeName comes from SopViolationType.Name, not from Sop");
        }

        [Fact]
        public void Violation_To_Response_SopName_AndViolationTypeName_ArePopulated_WhenFullNavLoaded()
        {
            var sop = new violation_management_api.Core.Entities.Sop
            {
                Id = Guid.NewGuid(),
                Name = "Kitchen Safety SOP"
            };
            var sopType = new SopViolationType
            {
                Id = Guid.NewGuid(),
                Name = "Missing Gloves",
                ModelIdentifier = "restaurant-ppe-v2",
                Sop = sop
            };
            var v = MinimalViolation();
            v.SopViolationType = sopType;

            var response = _mapper.Map<ViolationResponse>(v);

            response.SopName.Should().Be("Kitchen Safety SOP");
            response.ViolationTypeName.Should().Be("Missing Gloves");
            response.ModelIdentifier.Should().Be("restaurant-ppe-v2");
        }

        [Fact]
        public void Violation_To_Response_FrameUrl_IsIgnoredByMapping()
        {
            // FrameUrl is explicitly .Ignore() in the mapping and set by the
            // service layer (after S3 pre-sign or pass-through logic).
            // If someone removes the .Ignore(), AutoMapper will try to match by
            // convention and either throw (config invalid) or silently return null.
            // Either way, this test makes the intent explicit.
            var v = MinimalViolation();
            v.FramePath = "https://bucket.s3.amazonaws.com/frame.jpg";

            var response = _mapper.Map<ViolationResponse>(v);

            response.FrameUrl.Should().BeNull(
                "FrameUrl is set by the service, not by the mapper; " +
                "removing .Ignore() would bypass the null-guard logic in the service");
        }

        [Fact]
        public void Violation_To_Response_CoreFields_MapCorrectly()
        {
            var id = Guid.NewGuid();
            var tenantId = Guid.NewGuid();
            var ts = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            var v = new Violation
            {
                Id = id,
                TenantId = tenantId,
                Timestamp = ts,
                CameraId = "CAM-001",
                CorrelationId = "corr-core",
                Status = AuditStatus.Pending,
                IsFalsePositive = true,
                FalsePositiveMarkedAt = ts,
                FalsePositiveMarkedBy = "admin@test.com",
                FalsePositiveReason = "test reason"
            };

            var response = _mapper.Map<ViolationResponse>(v);

            response.Id.Should().Be(id);
            response.Timestamp.Should().Be(ts);
            response.CameraId.Should().Be("CAM-001");
            response.CorrelationId.Should().Be("corr-core");
            response.Status.Should().Be(AuditStatus.Pending);
            response.IsFalsePositive.Should().BeTrue();
            response.FalsePositiveMarkedAt.Should().Be(ts);
            response.FalsePositiveMarkedBy.Should().Be("admin@test.com");
            response.FalsePositiveReason.Should().Be("test reason");
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. Structural guard
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public void AutoMapper_Configuration_IsValid()
        {
            // Catches misconfigured ForMember rules, missing .Ignore() calls, or
            // type mismatches that AutoMapper can detect at config-build time.
            _mapper.ConfigurationProvider.AssertConfigurationIsValid();
        }
    }
}
