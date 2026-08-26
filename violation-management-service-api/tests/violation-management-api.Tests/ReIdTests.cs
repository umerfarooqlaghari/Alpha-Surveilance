using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector;
using violation_management_api.Controllers;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.DTOs.Responses;
using violation_management_api.Services;
using Xunit;

namespace violation_management_api.Tests;

public class ReIdTests
{
    private AppViolationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppViolationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppViolationDbContext(options);
    }

    private static List<float> CreateNormalizedVector(float seed, int dimension = 512)
    {
        var list = new List<float>(dimension);
        float sumSq = 0f;
        for (int i = 0; i < dimension; i++)
        {
            float val = (float)Math.Sin(seed + i * 0.1);
            list.Add(val);
            sumSq += val * val;
        }
        float norm = (float)Math.Sqrt(sumSq);
        for (int i = 0; i < dimension; i++)
        {
            list[i] /= norm;
        }
        return list;
    }

    [Fact]
    public async Task IdentifyAsync_WithExactMatch_ReturnsMatchedTrue()
    {
        var db = CreateInMemoryDbContext();
        var service = new ReIdService(db, NullLogger<ReIdService>.Instance);
        var tenantId = Guid.NewGuid();

        var vectorA = CreateNormalizedVector(1.0f);
        await service.EnrollWorkerProfileAsync(tenantId, "Worker_01", vectorA);

        var response = await service.IdentifyAsync(tenantId, vectorA, 0.80f);

        Assert.True(response.Matched);
        Assert.Equal("Worker_01", response.PersonTag);
        Assert.True(response.Similarity >= 0.99f);
    }

    [Fact]
    public async Task IdentifyAsync_WithDissimilarVector_ReturnsMatchedFalse()
    {
        var db = CreateInMemoryDbContext();
        var service = new ReIdService(db, NullLogger<ReIdService>.Instance);
        var tenantId = Guid.NewGuid();

        var vectorA = CreateNormalizedVector(1.0f);
        var vectorB = CreateNormalizedVector(500.0f); // very different direction

        await service.EnrollWorkerProfileAsync(tenantId, "Worker_01", vectorA);

        var response = await service.IdentifyAsync(tenantId, vectorB, 0.80f);

        Assert.False(response.Matched);
        Assert.Null(response.PersonTag);
        Assert.True(response.Similarity < 0.80f);
    }

    [Fact]
    public async Task IdentifyAsync_CrossTenantIsolation_NeverMatchesOtherTenantProfiles()
    {
        var db = CreateInMemoryDbContext();
        var service = new ReIdService(db, NullLogger<ReIdService>.Instance);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var vectorA = CreateNormalizedVector(1.0f);
        await service.EnrollWorkerProfileAsync(tenantA, "Worker_01", vectorA);

        // Querying from Tenant B should return NO match even with the exact same embedding
        var response = await service.IdentifyAsync(tenantB, vectorA, 0.80f);

        Assert.False(response.Matched);
        Assert.Null(response.PersonTag);
    }

    [Fact]
    public async Task IdentifyAsync_InvalidDimension_ThrowsArgumentException()
    {
        var db = CreateInMemoryDbContext();
        var service = new ReIdService(db, NullLogger<ReIdService>.Instance);
        var tenantId = Guid.NewGuid();

        var shortVector = new List<float> { 0.1f, 0.2f, 0.3f };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.IdentifyAsync(tenantId, shortVector, 0.80f));
    }

    [Fact]
    public async Task Controller_MissingHeaders_Returns401Unauthorized()
    {
        var db = CreateInMemoryDbContext();
        var service = new ReIdService(db, NullLogger<ReIdService>.Instance);
        var config = new ConfigurationBuilder().Build();
        var controller = new ReIdController(service, db, config, NullLogger<ReIdController>.Instance);

        var result = await controller.Identify(null, null, new IdentifyRequest
        {
            Embedding = CreateNormalizedVector(1.0f)
        });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Controller_InvalidKey_Returns401Unauthorized()
    {
        var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();
        var device = new EdgeDevice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceIdentifier = "EDGE-TEST-001",
            DisplayName = "Test Jetson",
            DeviceKey = "secret-key-123",
            Status = EdgeDeviceStatus.Active
        };
        db.EdgeDevices.Add(device);
        await db.SaveChangesAsync();

        var service = new ReIdService(db, NullLogger<ReIdService>.Instance);
        var config = new ConfigurationBuilder().Build();
        var controller = new ReIdController(service, db, config, NullLogger<ReIdController>.Instance);

        var result = await controller.Identify(device.Id.ToString(), "wrong-secret", new IdentifyRequest
        {
            Embedding = CreateNormalizedVector(1.0f)
        });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Controller_ValidCredentialsAndVector_Returns200WithMatch()
    {
        var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();
        var device = new EdgeDevice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceIdentifier = "EDGE-TEST-001",
            DisplayName = "Test Jetson",
            DeviceKey = "secret-key-123",
            Status = EdgeDeviceStatus.Active
        };
        db.EdgeDevices.Add(device);
        await db.SaveChangesAsync();

        var vector = CreateNormalizedVector(1.0f);
        var service = new ReIdService(db, NullLogger<ReIdService>.Instance);
        await service.EnrollWorkerProfileAsync(tenantId, "Reliever_A", vector);

        var config = new ConfigurationBuilder().Build();
        var controller = new ReIdController(service, db, config, NullLogger<ReIdController>.Instance);

        var actionResult = await controller.Identify(device.Id.ToString(), "secret-key-123", new IdentifyRequest
        {
            Embedding = vector,
            Threshold = 0.85f
        });

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<IdentifyResponse>(okResult.Value);

        Assert.True(response.Matched);
        Assert.Equal("Reliever_A", response.PersonTag);
        Assert.True(response.Similarity >= 0.99f);
    }

    [Fact]
    public async Task Controller_InvalidVectorLength_Returns400BadRequest()
    {
        var db = CreateInMemoryDbContext();
        var tenantId = Guid.NewGuid();
        var device = new EdgeDevice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceIdentifier = "EDGE-TEST-001",
            DisplayName = "Test Jetson",
            DeviceKey = "secret-key-123",
            Status = EdgeDeviceStatus.Active
        };
        db.EdgeDevices.Add(device);
        await db.SaveChangesAsync();

        var service = new ReIdService(db, NullLogger<ReIdService>.Instance);
        var config = new ConfigurationBuilder().Build();
        var controller = new ReIdController(service, db, config, NullLogger<ReIdController>.Instance);

        var actionResult = await controller.Identify(device.Id.ToString(), "secret-key-123", new IdentifyRequest
        {
            Embedding = new List<float> { 0.1f, 0.2f }, // wrong size
            Threshold = 0.80f
        });

        Assert.IsType<BadRequestObjectResult>(actionResult);
    }
}
