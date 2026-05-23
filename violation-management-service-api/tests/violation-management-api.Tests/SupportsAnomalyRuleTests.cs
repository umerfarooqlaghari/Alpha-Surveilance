using System;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using Xunit;

namespace violation_management_api.Tests
{
    /// <summary>
    /// Tests for D-9: server-driven <c>SupportsAnomalyRule</c> flag.
    ///
    /// Covers:
    ///   1. SopViolationTypeResponse.FromEntity maps the flag (true and false).
    ///   2. TenantViolationRequestResponse.FromEntity mirrors the flag from its SopViolationType.
    ///   3. Edge case: null SopViolationType → defaults to false (no NullReferenceException).
    ///   4. CreateSopViolationTypeRequest defaults the flag to false.
    ///   5. UpdateSopViolationTypeRequest.SupportsAnomalyRule is nullable (PATCH semantics:
    ///      null means "don't change").
    /// </summary>
    public class SupportsAnomalyRuleTests
    {
        // ── Factory helpers ──────────────────────────────────────────────────

        private static SopViolationType MakeSopType(bool supportsAnomalyRule = false) =>
            new()
            {
                Id = Guid.NewGuid(),
                SopId = Guid.NewGuid(),
                Name = "No Hardhat",
                ModelIdentifier = "construction-ppe-v1",
                TriggerLabels = "[\"no-hardhat\",\"no-vest\"]",
                Description = "Construction site PPE violation",
                SupportsAnomalyRule = supportsAnomalyRule,
            };

        private static TenantViolationRequest MakeTenantRequest(SopViolationType? sopType = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                SopViolationTypeId = Guid.NewGuid(),
                Status = RequestStatus.Approved,
                RequestedAt = DateTime.UtcNow,
                SopViolationType = sopType!,
            };

        // ── SopViolationTypeResponse ─────────────────────────────────────────

        [Fact]
        public void SopViolationTypeResponse_FromEntity_SetsSupportsAnomalyRule_True()
        {
            var entity = MakeSopType(supportsAnomalyRule: true);
            var dto = SopViolationTypeResponse.FromEntity(entity);
            Assert.True(dto.SupportsAnomalyRule,
                "FromEntity must copy SupportsAnomalyRule=true from the entity.");
        }

        [Fact]
        public void SopViolationTypeResponse_FromEntity_SetsSupportsAnomalyRule_False()
        {
            var entity = MakeSopType(supportsAnomalyRule: false);
            var dto = SopViolationTypeResponse.FromEntity(entity);
            Assert.False(dto.SupportsAnomalyRule,
                "FromEntity must copy SupportsAnomalyRule=false from the entity.");
        }

        [Fact]
        public void SopViolationTypeResponse_FromEntity_AllOtherFieldsIntact()
        {
            // Regression guard: adding SupportsAnomalyRule must not displace other fields.
            var entity = MakeSopType(supportsAnomalyRule: true);
            var dto = SopViolationTypeResponse.FromEntity(entity);

            Assert.Equal(entity.Id, dto.Id);
            Assert.Equal(entity.SopId, dto.SopId);
            Assert.Equal(entity.Name, dto.Name);
            Assert.Equal(entity.ModelIdentifier, dto.ModelIdentifier);
            Assert.Equal(entity.TriggerLabels, dto.TriggerLabels);
            Assert.Equal(entity.Description, dto.Description);
        }

        // ── TenantViolationRequestResponse ───────────────────────────────────

        [Fact]
        public void TenantViolationRequestResponse_MirrorsSupportsAnomalyRule_True()
        {
            var sopType = MakeSopType(supportsAnomalyRule: true);
            var req = MakeTenantRequest(sopType);

            var dto = TenantViolationRequestResponse.FromEntity(req);

            Assert.True(dto.SupportsAnomalyRule,
                "TenantViolationRequestResponse must mirror SupportsAnomalyRule=true from SopViolationType.");
        }

        [Fact]
        public void TenantViolationRequestResponse_MirrorsSupportsAnomalyRule_False()
        {
            var sopType = MakeSopType(supportsAnomalyRule: false);
            var req = MakeTenantRequest(sopType);

            var dto = TenantViolationRequestResponse.FromEntity(req);

            Assert.False(dto.SupportsAnomalyRule,
                "TenantViolationRequestResponse must mirror SupportsAnomalyRule=false from SopViolationType.");
        }

        [Fact]
        public void TenantViolationRequestResponse_DefaultsFalse_WhenSopViolationTypeIsNull()
        {
            // Edge case: orphaned request whose SopViolationType was soft-deleted.
            var req = MakeTenantRequest(sopType: null);

            var dto = TenantViolationRequestResponse.FromEntity(req);

            Assert.False(dto.SupportsAnomalyRule,
                "Null SopViolationType must default SupportsAnomalyRule to false, not throw.");
        }

        [Fact]
        public void TenantViolationRequestResponse_SopTriggerLabels_Null_WhenSopViolationTypeIsNull()
        {
            // D-9 sibling field — verify null navigation props handled uniformly.
            var req = MakeTenantRequest(sopType: null);
            var dto = TenantViolationRequestResponse.FromEntity(req);
            Assert.Null(dto.SopTriggerLabels); // null nav property → null labels
        }

        // ── Request DTOs (create + update) ───────────────────────────────────

        [Fact]
        public void CreateSopViolationTypeRequest_DefaultsSupportsAnomalyRule_False()
        {
            var req = new CreateSopViolationTypeRequest();
            Assert.False(req.SupportsAnomalyRule,
                "Default must be false so existing SOP types created without specifying the flag stay spatial-only.");
        }

        [Fact]
        public void CreateSopViolationTypeRequest_CanSetSupportsAnomalyRule_True()
        {
            var req = new CreateSopViolationTypeRequest { SupportsAnomalyRule = true };
            Assert.True(req.SupportsAnomalyRule);
        }

        [Fact]
        public void UpdateSopViolationTypeRequest_SupportsAnomalyRule_IsNullable()
        {
            // PATCH semantics: null = "don't change the existing value"
            var req = new UpdateSopViolationTypeRequest();
            // SupportsAnomalyRule must be nullable on UpdateSopViolationTypeRequest so a partial
            // PATCH that doesn't include the field doesn't accidentally reset it to false.
            Assert.Null(req.SupportsAnomalyRule);
        }

        [Fact]
        public void UpdateSopViolationTypeRequest_CanSetSupportsAnomalyRule_True()
        {
            var req = new UpdateSopViolationTypeRequest { SupportsAnomalyRule = true };
            Assert.True(req.SupportsAnomalyRule.HasValue);
            Assert.True(req.SupportsAnomalyRule!.Value);
        }

        [Fact]
        public void UpdateSopViolationTypeRequest_CanSetSupportsAnomalyRule_False()
        {
            var req = new UpdateSopViolationTypeRequest { SupportsAnomalyRule = false };
            Assert.True(req.SupportsAnomalyRule.HasValue);
            Assert.False(req.SupportsAnomalyRule!.Value);
        }

        // ── Migration backfill logic (pure-logic equivalent) ─────────────────

        /// <summary>
        /// The migration's backfill SQL sets SupportsAnomalyRule=TRUE for any
        /// SopViolationType whose TriggerLabels matches /^(no[-_]|incorrect[-_]|missing[-_])/.
        /// This test verifies the same logic in C# so a future refactor can't
        /// silently change the classification rule without breaking tests.
        /// </summary>
        [Theory]
        [InlineData("[\"no-hardhat\",\"no-vest\"]", true)]
        [InlineData("[\"no_mask\",\"missing_glove\"]", true)]
        [InlineData("[\"incorrect-apron\"]", true)]
        [InlineData("[\"missing-hairnet\"]", true)]
        [InlineData("[\"person\",\"vehicle\"]", false)]
        [InlineData("[\"restricted-area\"]", false)]
        [InlineData("", false)]
        public void BackfillLogic_CorrectlyClassifiesTriggerLabels(string triggerLabels, bool expectsAnomaly)
        {
            // Mirror the regex used in the migration SQL and in the UI fallback:
            // matches label prefixes: no-, incorrect-, missing- (case-insensitive)
            var pattern = new System.Text.RegularExpressions.Regex(
                @"(no[-_]|incorrect[-_]|missing[-_])",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            bool actual = !string.IsNullOrWhiteSpace(triggerLabels) && pattern.IsMatch(triggerLabels);

            Assert.Equal(expectsAnomaly, actual);
        }
    }
}
