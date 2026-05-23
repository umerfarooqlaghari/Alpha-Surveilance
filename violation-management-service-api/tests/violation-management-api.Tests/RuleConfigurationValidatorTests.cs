using System;
using FluentAssertions;
using violation_management_api.Services;
using Xunit;

namespace violation_management_api.Tests;

/// <summary>
/// Unit tests for <see cref="RuleConfigurationValidator"/>.
///
/// Covers all three rule types (geofence, dwell, anomaly) plus the new
/// self-intersection topology check added to close Issue #3.
/// </summary>
public class RuleConfigurationValidatorTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>A normal convex rectangle (always valid).</summary>
    private const string ValidSquare =
        "[[100,100],[200,100],[200,200],[100,200]]";

    private static string Geofence(
        string polygon = ValidSquare,
        string mode    = "entry",
        string anchor  = "bottom_center",
        string space   = "pixel") =>
        $$"""{"type":"geofence","polygon":{{polygon}},"mode":"{{mode}}","anchor":"{{anchor}}","coordinate_space":"{{space}}"}""";

    private static string Dwell(
        string polygon   = ValidSquare,
        double durationS = 10.0,
        string mode      = "entry") =>
        $$"""{"type":"dwell","polygon":{{polygon}},"duration_s":{{durationS}},"mode":"{{mode}}"}""";

    private static string Anomaly(
        double? minScore     = null,
        string? targetLabels = null)
    {
        var parts = new System.Collections.Generic.List<string> { @"""type"":""anomaly""" };
        if (minScore.HasValue)
            parts.Add($@"""min_score"":{minScore.Value}");
        if (targetLabels is not null)
            parts.Add($@"""target_labels"":{targetLabels}");
        return "{" + string.Join(",", parts) + "}";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Null / empty input
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Null_input_returns_null()
        => RuleConfigurationValidator.ValidateAndNormalize(null).Should().BeNull();

    [Fact]
    public void Empty_string_returns_null()
        => RuleConfigurationValidator.ValidateAndNormalize("   ").Should().BeNull();

    // ──────────────────────────────────────────────────────────────────────────
    // Geofence — happy paths
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Valid_geofence_square_is_accepted()
    {
        var result = RuleConfigurationValidator.ValidateAndNormalize(Geofence());
        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("\"type\"");
    }

    [Theory]
    [InlineData("entry")]
    [InlineData("exit")]
    public void Valid_geofence_modes_are_accepted(string mode)
        => RuleConfigurationValidator.ValidateAndNormalize(Geofence(mode: mode))
            .Should().NotBeNull();

    [Theory]
    [InlineData("centroid")]
    [InlineData("bottom_center")]
    [InlineData("top_center")]
    public void Valid_geofence_anchors_are_accepted(string anchor)
        => RuleConfigurationValidator.ValidateAndNormalize(Geofence(anchor: anchor))
            .Should().NotBeNull();

    [Fact]
    public void Normalized_coordinate_space_is_accepted()
    {
        var poly = "[[0.1,0.1],[0.9,0.1],[0.9,0.9],[0.1,0.9]]";
        RuleConfigurationValidator.ValidateAndNormalize(Geofence(polygon: poly, space: "normalized"))
            .Should().NotBeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Geofence — fail paths
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Geofence_with_fewer_than_3_vertices_is_rejected()
    {
        var json = Geofence(polygon: "[[0,0],[100,100]]");
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(json);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least 3 vertices*");
    }

    [Fact]
    public void Geofence_exceeding_max_vertices_is_rejected()
    {
        var pts = string.Join(",", System.Linq.Enumerable.Range(0, 65).Select(i => $"[{i},{i}]"));
        var json = Geofence(polygon: $"[{pts}]");
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(json);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{RuleConfigurationValidator.MaxPolygonVertices}*");
    }

    [Fact]
    public void Unknown_geofence_mode_is_rejected()
    {
        var json = Geofence(mode: "loitering");
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(json);
        act.Should().Throw<InvalidOperationException>().WithMessage("*mode*");
    }

    [Fact]
    public void Unknown_anchor_is_rejected()
    {
        var json = Geofence(anchor: "elbow");
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(json);
        act.Should().Throw<InvalidOperationException>().WithMessage("*anchor*");
    }

    [Fact]
    public void Negative_pixel_coordinate_is_rejected()
    {
        var poly = "[[-10,100],[200,100],[200,200],[100,200]]";
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(Geofence(polygon: poly));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Normalized_coordinate_out_of_range_is_rejected()
    {
        var poly = "[[0.1,0.1],[1.5,0.1],[0.9,0.9],[0.1,0.9]]";
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(
            Geofence(polygon: poly, space: "normalized"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*[0, 1]*");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Issue #3 — self-intersecting polygon topology check
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Bowtie_polygon_is_rejected_as_self_intersecting()
    {
        // Classic bowtie: edges (0→1) and (2→3) cross in the middle.
        // Vertices: TL(0,0), BR(200,200), TR(200,0), BL(0,200)
        const string bowtie = "[[0,0],[200,200],[200,0],[0,200]]";
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(Geofence(polygon: bowtie));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*self-intersect*");
    }

    [Fact]
    public void Bowtie_dwell_polygon_is_also_rejected()
    {
        const string bowtie = "[[0,0],[200,200],[200,0],[0,200]]";
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(Dwell(polygon: bowtie));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*self-intersect*");
    }

    [Fact]
    public void Convex_polygon_is_not_flagged_as_self_intersecting()
    {
        // A convex polygon whose adjacent edges share vertices should not trigger.
        var json = Geofence();
        RuleConfigurationValidator.ValidateAndNormalize(json).Should().NotBeNull();
    }

    [Fact]
    public void Concave_polygon_is_accepted_when_edges_do_not_cross()
    {
        // L-shaped concave polygon — no edges cross.
        const string lShape =
            "[[0,0],[100,0],[100,50],[50,50],[50,100],[0,100]]";
        var json = Geofence(polygon: lShape);
        RuleConfigurationValidator.ValidateAndNormalize(json).Should().NotBeNull();
    }

    [Fact]
    public void Figure8_polygon_is_rejected()
    {
        // Two squares joined in a figure-8 (self-intersecting).
        const string fig8 =
            "[[0,0],[100,100],[100,0],[0,100]]";
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(Geofence(polygon: fig8));
        act.Should().Throw<InvalidOperationException>().WithMessage("*self-intersect*");
    }

    [Fact]
    public void Triangle_is_never_self_intersecting()
    {
        const string triangle = "[[0,0],[100,0],[50,100]]";
        RuleConfigurationValidator.ValidateAndNormalize(Geofence(polygon: triangle))
            .Should().NotBeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dwell — happy paths
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Valid_dwell_is_accepted()
        => RuleConfigurationValidator.ValidateAndNormalize(Dwell()).Should().NotBeNull();

    [Theory]
    [InlineData(0.5)]     // Issue #6: sub-second is valid per the schema
    [InlineData(1.0)]
    [InlineData(30.0)]
    [InlineData(3600.0)]
    public void Dwell_accepts_valid_duration_s(double d)
        => RuleConfigurationValidator.ValidateAndNormalize(Dwell(durationS: d)).Should().NotBeNull();

    // ──────────────────────────────────────────────────────────────────────────
    // Dwell — fail paths
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(3601.0)]
    public void Invalid_dwell_duration_s_is_rejected(double d)
    {
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(Dwell(durationS: d));
        act.Should().Throw<InvalidOperationException>().WithMessage("*duration_s*");
    }

    [Fact]
    public void Dwell_without_duration_s_is_rejected()
    {
        const string json = """{"type":"dwell","polygon":[[0,0],[100,0],[100,100],[0,100]]}""";
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(json);
        act.Should().Throw<InvalidOperationException>().WithMessage("*duration_s*");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Anomaly — happy paths
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Valid_anomaly_no_fields_is_accepted()
        => RuleConfigurationValidator.ValidateAndNormalize(Anomaly()).Should().NotBeNull();

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Anomaly_accepts_valid_min_score(double s)
        => RuleConfigurationValidator.ValidateAndNormalize(Anomaly(minScore: s)).Should().NotBeNull();

    [Fact]
    public void Anomaly_accepts_target_labels_array()
        => RuleConfigurationValidator.ValidateAndNormalize(
            Anomaly(targetLabels: "[\"burnt\",\"missing-label\"]")).Should().NotBeNull();

    // ──────────────────────────────────────────────────────────────────────────
    // Anomaly — fail paths
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void Anomaly_out_of_range_min_score_is_rejected(double s)
    {
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(Anomaly(minScore: s));
        act.Should().Throw<InvalidOperationException>().WithMessage("*min_score*");
    }

    [Fact]
    public void Anomaly_target_labels_not_array_is_rejected()
    {
        const string json = """{"type":"anomaly","target_labels":"burnt"}""";
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(json);
        act.Should().Throw<InvalidOperationException>().WithMessage("*target_labels*");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Unknown type
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Unknown_rule_type_is_rejected()
    {
        const string json = """{"type":"loitering","polygon":[[0,0],[100,0],[100,100],[0,100]]}""";
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(json);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Unsupported rule type*");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Payload size guard
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Oversized_json_is_rejected()
    {
        var huge = new string('x', RuleConfigurationValidator.MaxJsonBytes + 1);
        var act = () => RuleConfigurationValidator.ValidateAndNormalize(huge);
        act.Should().Throw<InvalidOperationException>().WithMessage("*exceeds*");
    }
}
