using AlphaSurveilance.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.Services;

namespace violation_management_api.Tests;

public class AiModelServiceTests
{
    private static AppViolationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<AppViolationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppViolationDbContext(options);
    }

    private static AiModelService BuildService(AppViolationDbContext db)
        => new(db, NullLogger<AiModelService>.Instance);

    [Fact]
    public async Task RegisterAsync_EmptyModelKey_ThrowsInvalidOperation()
    {
        using var db = BuildDb();
        var sut = BuildService(db);

        var act = async () => await sut.RegisterAsync(new RegisterAiModelRequest
        {
            ModelKey = "   ",
            DisplayName = "PPE v1",
            Description = "desc",
            ModelType = AiModelType.YoloFineTuned
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ModelKey is required*");
    }

    [Fact]
    public async Task RegisterAsync_DuplicateModelKey_CaseInsensitive_Throws()
    {
        using var db = BuildDb();
        db.AiModels.Add(new AiModel
        {
            Id = Guid.NewGuid(),
            ModelKey = "restaurant-ppe-v1",
            DisplayName = "Restaurant PPE",
            Description = "desc",
            ModelType = AiModelType.YoloFineTuned,
            Status = AiModelStatus.Available,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var act = async () => await sut.RegisterAsync(new RegisterAiModelRequest
        {
            ModelKey = "Restaurant-PPE-V1",
            DisplayName = "Duplicate",
            Description = "desc",
            ModelType = AiModelType.YoloFineTuned
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task UpdateEdgeStatusAsync_InvalidStatus_ThrowsArgumentException()
    {
        using var db = BuildDb();
        var model = new AiModel
        {
            Id = Guid.NewGuid(),
            ModelKey = "pest-detection-v1",
            DisplayName = "Pest",
            Description = "desc",
            ModelType = AiModelType.YoloFineTuned,
            Status = AiModelStatus.Registered,
            CreatedAt = DateTime.UtcNow
        };
        db.AiModels.Add(model);
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var act = async () => await sut.UpdateEdgeStatusAsync(model.Id, new EdgeModelStatusUpdate
        {
            Status = "NotARealStatus",
            ErrorMessage = "bad"
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid status*");
    }

    [Fact]
    public async Task DeleteAsync_WhenModelIsReferenced_ReturnsConflictMessage()
    {
        using var db = BuildDb();

        var sop = new Sop
        {
            Id = Guid.NewGuid(),
            Name = "SOP",
            Description = "desc",
            CreatedAt = DateTime.UtcNow
        };

        var model = new AiModel
        {
            Id = Guid.NewGuid(),
            ModelKey = "human-detection-v1",
            DisplayName = "Human",
            Description = "desc",
            ModelType = AiModelType.YoloLocal,
            Status = AiModelStatus.Available,
            CreatedAt = DateTime.UtcNow
        };

        var violationType = new SopViolationType
        {
            Id = Guid.NewGuid(),
            SopId = sop.Id,
            Sop = sop,
            Name = "Unauthorized Person",
            ModelIdentifier = model.ModelKey,
            TriggerLabels = "person",
            Description = "desc",
            AiModelId = model.Id,
            AiModel = model,
            IsDeleted = false
        };

        db.Sops.Add(sop);
        db.AiModels.Add(model);
        db.SopViolationTypes.Add(violationType);
        await db.SaveChangesAsync();

        var sut = BuildService(db);
        var (success, error) = await sut.DeleteAsync(model.Id);

        success.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("still reference");

        var stillExists = await db.AiModels.AnyAsync(m => m.Id == model.Id);
        stillExists.Should().BeTrue();
    }
}
