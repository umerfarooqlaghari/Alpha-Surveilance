using AlphaSurveilance.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using violation_management_api.Core.Entities;
using violation_management_api.DTOs.Requests;
using violation_management_api.Services;

namespace violation_management_api.Tests;

public class SopServiceModelLinkTests
{
    private static AppViolationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<AppViolationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppViolationDbContext(options);
    }

    private static IConfiguration BuildConfiguration()
        => new ConfigurationBuilder().AddInMemoryCollection().Build();

    private static SopService BuildService(AppViolationDbContext db)
        => new(db, NullLogger<SopService>.Instance, BuildConfiguration());

    [Fact]
    public async Task CreateSopViolationTypeAsync_AssignsAiModelId_FromModelRegistry()
    {
        using var db = BuildDb();
        var sop = new Sop { Id = Guid.NewGuid(), Name = "Open Operations", CreatedAt = DateTime.UtcNow };
        var model = new AiModel
        {
            Id = Guid.NewGuid(),
            ModelKey = "locate-anything-v1",
            DisplayName = "Locate Anything",
            Description = "desc",
            ModelType = AiModelType.OpenVocabGrounding,
            Status = AiModelStatus.Registered,
            CreatedAt = DateTime.UtcNow
        };

        db.Sops.Add(sop);
        db.AiModels.Add(model);
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var result = await sut.CreateSopViolationTypeAsync(sop.Id, new CreateSopViolationTypeRequest
        {
            Name = "Open Operations Activity",
            ModelIdentifier = "locate-anything-v1",
            TriggerLabels = "[\"person\"]",
            Description = "desc"
        });

        result.ModelIdentifier.Should().Be("locate-anything-v1");

        var saved = await db.SopViolationTypes.SingleAsync(v => v.Id == result.Id);
        saved.AiModelId.Should().Be(model.Id);
    }

    [Fact]
    public async Task CreateSopViolationTypeAsync_UnknownModel_ThrowsInvalidOperation()
    {
        using var db = BuildDb();
        var sop = new Sop { Id = Guid.NewGuid(), Name = "Open Operations", CreatedAt = DateTime.UtcNow };
        db.Sops.Add(sop);
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        var act = async () => await sut.CreateSopViolationTypeAsync(sop.Id, new CreateSopViolationTypeRequest
        {
            Name = "Open Operations Activity",
            ModelIdentifier = "missing-model",
            TriggerLabels = "[\"person\"]",
            Description = "desc"
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not exist in the model registry*");
    }

    [Fact]
    public async Task UpdateSopViolationTypeAsync_UpdatesAiModelLink_WhenModelChanges()
    {
        using var db = BuildDb();
        var sop = new Sop { Id = Guid.NewGuid(), Name = "SOP", CreatedAt = DateTime.UtcNow };
        var oldModel = new AiModel
        {
            Id = Guid.NewGuid(),
            ModelKey = "human-detection-v1",
            DisplayName = "Human",
            Description = "desc",
            ModelType = AiModelType.YoloLocal,
            Status = AiModelStatus.Available,
            CreatedAt = DateTime.UtcNow
        };
        var newModel = new AiModel
        {
            Id = Guid.NewGuid(),
            ModelKey = "locate-anything-v1",
            DisplayName = "Locate",
            Description = "desc",
            ModelType = AiModelType.OpenVocabGrounding,
            Status = AiModelStatus.Registered,
            CreatedAt = DateTime.UtcNow
        };
        var violationType = new SopViolationType
        {
            Id = Guid.NewGuid(),
            SopId = sop.Id,
            Name = "Rule",
            ModelIdentifier = oldModel.ModelKey,
            AiModelId = oldModel.Id,
            TriggerLabels = "[\"person\"]",
            Description = "desc"
        };

        db.Sops.Add(sop);
        db.AiModels.AddRange(oldModel, newModel);
        db.SopViolationTypes.Add(violationType);
        await db.SaveChangesAsync();

        var sut = BuildService(db);

        await sut.UpdateSopViolationTypeAsync(violationType.Id, new UpdateSopViolationTypeRequest
        {
            ModelIdentifier = newModel.ModelKey
        });

        var saved = await db.SopViolationTypes.SingleAsync(v => v.Id == violationType.Id);
        saved.ModelIdentifier.Should().Be(newModel.ModelKey);
        saved.AiModelId.Should().Be(newModel.Id);
    }
}