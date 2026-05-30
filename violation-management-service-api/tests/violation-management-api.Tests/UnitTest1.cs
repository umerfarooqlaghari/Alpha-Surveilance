using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Data;
using AlphaSurveilance.Data.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace violation_management_api.Tests
{
    /// <summary>
    /// Integration-style tests for ViolationRepository.
    ///
    /// Uses SQLite in-memory (not EF InMemory) because the repository uses
    /// ExecuteUpdateAsync / ExecuteDeleteAsync which require a real SQL provider.
    ///
    /// Critical invariant: every repository method that mutates or reads data
    /// must be scoped to the supplied tenantId. Removing the tenantId WHERE
    /// predicate would expose one tenant's violations to another.
    /// </summary>
    public class ViolationRepositoryTests : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ViolationRepositoryTests()
        {
            // Keep the connection open for the lifetime of the test so the
            // in-memory SQLite database is not destroyed between operations.
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            // Disable FK enforcement — tests focus on query-filter logic, not
            // referential integrity (there is no Tenant/SopViolationType row to seed).
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys = OFF;";
            cmd.ExecuteNonQuery();
        }

        public void Dispose() => _connection.Dispose();

        private AppViolationDbContext BuildDb()
        {
            var db = new AppViolationDbContext(
                new DbContextOptionsBuilder<AppViolationDbContext>()
                    .UseSqlite(_connection)
                    .Options);
            db.Database.EnsureCreated();
            return db;
        }

        private static ViolationRepository BuildRepo(AppViolationDbContext db)
            => new(db, Mock.Of<IConfiguration>());

        private static Violation MakeViolation(Guid tenantId, bool isFp = false, string corrId = "")
            => new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CorrelationId = string.IsNullOrEmpty(corrId) ? Guid.NewGuid().ToString() : corrId,
                Timestamp = DateTime.UtcNow,
                IsFalsePositive = isFp,
                FalsePositiveMarkedAt = isFp ? DateTime.UtcNow : null
            };

        // ════════════════════════════════════════════════════════════════════
        // GetAllAsync
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetAllAsync_HidesFalsePositives_ByDefault()
        {
            var db = BuildDb();
            var t = Guid.NewGuid();
            db.Violations.AddRange(MakeViolation(t, isFp: false), MakeViolation(t, isFp: true));
            await db.SaveChangesAsync();

            var results = await BuildRepo(db).GetAllAsync(t);

            results.Should().ContainSingle("false-positive row must be hidden from the default view");
            results.Single().IsFalsePositive.Should().BeFalse();
        }

        [Fact]
        public async Task GetAllAsync_ShowsFalsePositives_WhenFlagIsTrue()
        {
            var db = BuildDb();
            var t = Guid.NewGuid();
            db.Violations.AddRange(MakeViolation(t, isFp: false), MakeViolation(t, isFp: true));
            await db.SaveChangesAsync();

            var results = await BuildRepo(db).GetAllAsync(t, includeFalsePositives: true);

            results.Should().HaveCount(2,
                "includeFalsePositives=true must return all rows including FP ones");
        }

        [Fact]
        public async Task GetAllAsync_DoesNotReturnViolationsFromOtherTenants()
        {
            var db = BuildDb();
            var t1 = Guid.NewGuid();
            var t2 = Guid.NewGuid();
            db.Violations.AddRange(MakeViolation(t1), MakeViolation(t2));
            await db.SaveChangesAsync();

            var results = await BuildRepo(db).GetAllAsync(t1);

            results.Should().ContainSingle();
            results.Single().TenantId.Should().Be(t1,
                "GetAllAsync must be scoped to the supplied tenantId — " +
                "removing the WHERE predicate would leak all tenants' violations");
        }

        // ════════════════════════════════════════════════════════════════════
        // GetByIdAsync — cross-tenant isolation
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetByIdAsync_ReturnsNull_WhenTenantIdDoesNotMatch()
        {
            // A TenantAdmin from T2 must not be able to fetch T1's violation by ID.
            var db = BuildDb();
            var t1 = Guid.NewGuid();
            var t2 = Guid.NewGuid();
            var v = MakeViolation(t1);
            db.Violations.Add(v);
            await db.SaveChangesAsync();

            var result = await BuildRepo(db).GetByIdAsync(v.Id, t2);

            result.Should().BeNull(
                "GetByIdAsync must filter by both Id AND tenantId; " +
                "removing the tenantId predicate would allow cross-tenant ID enumeration");
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsViolation_WhenTenantIdMatches()
        {
            var db = BuildDb();
            var t = Guid.NewGuid();
            var v = MakeViolation(t);
            db.Violations.Add(v);
            await db.SaveChangesAsync();

            var result = await BuildRepo(db).GetByIdAsync(v.Id, t);

            result.Should().NotBeNull();
            result!.Id.Should().Be(v.Id);
        }

        // ════════════════════════════════════════════════════════════════════
        // MarkFalsePositiveAsync — tenant isolation + idempotency
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task MarkFalsePositiveAsync_EmptyIdList_ReturnsZero_NoUpdate()
        {
            var db = BuildDb();
            var t = Guid.NewGuid();
            db.Violations.Add(MakeViolation(t));
            await db.SaveChangesAsync();
            var countBefore = db.Violations.Count(v => v.IsFalsePositive);

            var result = await BuildRepo(db).MarkFalsePositiveAsync(
                new List<Guid>(), t, "u", "r");

            result.Should().Be(0);
            db.Violations.Count(v => v.IsFalsePositive).Should().Be(countBefore);
        }

        [Fact]
        public async Task MarkFalsePositiveAsync_OnlyAffectsTargetTenantRows()
        {
            // The WHERE predicate must include tenantId. If removed, marking FP
            // for tenant T1 would also mark T2's rows.
            var db = BuildDb();
            var t1 = Guid.NewGuid();
            var t2 = Guid.NewGuid();
            var v1 = MakeViolation(t1);
            var v2 = MakeViolation(t2);
            db.Violations.AddRange(v1, v2);
            await db.SaveChangesAsync();

            await BuildRepo(db).MarkFalsePositiveAsync(
                new[] { v1.Id, v2.Id }, t1, "u", "r");

            // ExecuteUpdateAsync bypasses the change tracker — clear it so the
            // subsequent read hits the database, not the in-memory cache.
            db.ChangeTracker.Clear();
            db.Violations.Single(v => v.Id == v1.Id).IsFalsePositive.Should().BeTrue();
            db.Violations.Single(v => v.Id == v2.Id).IsFalsePositive.Should().BeFalse(
                "marking FP for T1 must NOT affect T2's violation, even when the same ID is listed");
        }

        [Fact]
        public async Task MarkFalsePositiveAsync_AlreadyFalsePositive_ReturnsZero()
        {
            // The WHERE clause has `!v.IsFalsePositive` — double-marking returns 0.
            var db = BuildDb();
            var t = Guid.NewGuid();
            var v = MakeViolation(t, isFp: true);
            db.Violations.Add(v);
            await db.SaveChangesAsync();

            var result = await BuildRepo(db).MarkFalsePositiveAsync(
                new[] { v.Id }, t, "u", "r");

            result.Should().Be(0,
                "already-FP violations must not be double-counted; " +
                "removing the !IsFalsePositive predicate would report incorrect affected counts");
        }

        [Fact]
        public async Task MarkFalsePositiveAsync_SetsAuditFields()
        {
            var db = BuildDb();
            var t = Guid.NewGuid();
            var v = MakeViolation(t);
            db.Violations.Add(v);
            await db.SaveChangesAsync();

            await BuildRepo(db).MarkFalsePositiveAsync(
                new[] { v.Id }, t, "reviewer@alpha.com", "mirror reflection");

            db.ChangeTracker.Clear();
            var updated = db.Violations.Single();
            updated.FalsePositiveMarkedBy.Should().Be("reviewer@alpha.com");
            updated.FalsePositiveReason.Should().Be("mirror reflection");
            updated.FalsePositiveMarkedAt.Should().NotBeNull();
        }

        // ════════════════════════════════════════════════════════════════════
        // UnmarkFalsePositiveAsync
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task UnmarkFalsePositiveAsync_ReturnsZero_ForNonFpViolations()
        {
            var db = BuildDb();
            var t = Guid.NewGuid();
            var v = MakeViolation(t, isFp: false);
            db.Violations.Add(v);
            await db.SaveChangesAsync();

            var result = await BuildRepo(db).UnmarkFalsePositiveAsync(new[] { v.Id }, t);

            result.Should().Be(0,
                "unmark must only operate on rows where IsFalsePositive=true; " +
                "removing that predicate would corrupt non-FP violations");
        }

        [Fact]
        public async Task UnmarkFalsePositiveAsync_ClearsAuditFields()
        {
            var db = BuildDb();
            var t = Guid.NewGuid();
            var v = MakeViolation(t, isFp: true);
            db.Violations.Add(v);
            await db.SaveChangesAsync();

            await BuildRepo(db).UnmarkFalsePositiveAsync(new[] { v.Id }, t);

            db.ChangeTracker.Clear();
            var updated = db.Violations.Single();
            updated.IsFalsePositive.Should().BeFalse();
            updated.FalsePositiveMarkedAt.Should().BeNull();
            updated.FalsePositiveMarkedBy.Should().BeNull();
            updated.FalsePositiveReason.Should().BeNull();
        }

        [Fact]
        public async Task UnmarkFalsePositiveAsync_DoesNotAffectOtherTenants()
        {
            var db = BuildDb();
            var t1 = Guid.NewGuid();
            var t2 = Guid.NewGuid();
            var vT1 = MakeViolation(t1, isFp: true);
            var vT2 = MakeViolation(t2, isFp: true);
            db.Violations.AddRange(vT1, vT2);
            await db.SaveChangesAsync();

            await BuildRepo(db).UnmarkFalsePositiveAsync(new[] { vT1.Id, vT2.Id }, t1);

            db.ChangeTracker.Clear();
            db.Violations.Single(v => v.Id == vT1.Id).IsFalsePositive.Should().BeFalse();
            db.Violations.Single(v => v.Id == vT2.Id).IsFalsePositive.Should().BeTrue(
                "unmark for T1 must NOT restore T2's violation");
        }

        // ════════════════════════════════════════════════════════════════════
        // GetFalsePositivesAsync
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetFalsePositivesAsync_ReturnsOnlyFpRows_ForCorrectTenant()
        {
            var db = BuildDb();
            var t = Guid.NewGuid();
            db.Violations.AddRange(
                MakeViolation(t, isFp: false),
                MakeViolation(t, isFp: true),
                MakeViolation(Guid.NewGuid(), isFp: true)  // another tenant
            );
            await db.SaveChangesAsync();

            var results = await BuildRepo(db).GetFalsePositivesAsync(t);

            results.Should().ContainSingle();
            results.Single().IsFalsePositive.Should().BeTrue();
            results.Single().TenantId.Should().Be(t);
        }

        // ════════════════════════════════════════════════════════════════════
        // ExistsByCorrelationIdAsync — dedup guard
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task ExistsByCorrelationIdAsync_ReturnsTrue_ForExistingId()
        {
            var db = BuildDb();
            var v = MakeViolation(Guid.NewGuid(), corrId: "CORR-EXISTS");
            db.Violations.Add(v);
            await db.SaveChangesAsync();

            var exists = await BuildRepo(db).ExistsByCorrelationIdAsync("CORR-EXISTS");
            exists.Should().BeTrue();
        }

        [Fact]
        public async Task ExistsByCorrelationIdAsync_ReturnsFalse_ForMissingId()
        {
            var exists = await BuildRepo(BuildDb()).ExistsByCorrelationIdAsync("NO-SUCH-ID");
            exists.Should().BeFalse();
        }

        [Fact]
        public async Task GetExistingCorrelationIdsAsync_ReturnsOnlyMatchingIds()
        {
            var db = BuildDb();
            db.Violations.AddRange(
                MakeViolation(Guid.NewGuid(), corrId: "CORR-A"),
                MakeViolation(Guid.NewGuid(), corrId: "CORR-B")
            );
            await db.SaveChangesAsync();

            var result = (await BuildRepo(db)
                .GetExistingCorrelationIdsAsync(new[] { "CORR-A", "CORR-C" }))
                .ToList();

            result.Should().ContainSingle().Which.Should().Be("CORR-A");
        }
    }
}
