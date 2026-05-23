using System.Text.Json;

namespace violation_management_api.Services;

/// <summary>
/// Validates user-supplied <c>RuleConfigurationJson</c> for camera violation assignments.
///
/// Goals:
///   1. Reject malformed JSON at the API boundary (no silent drop at inference time).
///   2. Enforce a strict whitelist of <c>type</c> values so unknown / typo'd policies
///      cannot fail-open at the vision worker.
///   3. Cap polygon vertex counts to prevent CPU-DoS via huge Shapely predicates.
///   4. Validate numeric ranges (scores, coordinates) before they reach inference.
/// </summary>
public static class RuleConfigurationValidator
{
    public const int MaxPolygonVertices = 64;
    public const int MaxJsonBytes = 8 * 1024; // 8 KB is plenty for a polygon + flags

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "geofence",
        "anomaly",
        "dwell",
    };

    private static readonly HashSet<string> AllowedGeofenceModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "entry",
        "exit",
    };

    private static readonly HashSet<string> AllowedAnchors = new(StringComparer.OrdinalIgnoreCase)
    {
        "centroid",
        "bottom_center",
        "top_center",
    };

    private static readonly HashSet<string> AllowedCoordinateSpaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "pixel",
        "normalized",
    };

    /// <summary>
    /// Returns a normalized canonical JSON string when valid; throws
    /// <see cref="InvalidOperationException"/> with a user-facing message otherwise.
    /// Returns <c>null</c> for null / empty input (treated as "no policy").
    /// </summary>
    public static string? ValidateAndNormalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
        {
            throw new InvalidOperationException(
                $"RuleConfigurationJson exceeds {MaxJsonBytes} bytes.");
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"RuleConfigurationJson is not valid JSON: {ex.Message}");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("RuleConfigurationJson must be a JSON object.");
        }

        if (!root.TryGetProperty("type", out var typeElem) || typeElem.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("RuleConfigurationJson must include a 'type' string.");
        }

        var type = typeElem.GetString() ?? string.Empty;
        if (!AllowedTypes.Contains(type))
        {
            throw new InvalidOperationException(
                $"Unsupported rule type '{type}'. Allowed: {string.Join(", ", AllowedTypes)}.");
        }

        switch (type.ToLowerInvariant())
        {
            case "geofence":
                ValidateGeofence(root);
                break;
            case "anomaly":
                ValidateAnomaly(root);
                break;
            case "dwell":
                // Dwell reuses every geofence field (polygon, mode, anchor,
                // coordinate_space, source_frame_size) plus a required
                // duration_s. We validate the geofence shape first, then the
                // dwell-specific knob.
                ValidateGeofence(root);
                ValidateDwell(root);
                break;
        }

        // Return canonical compact JSON so downstream consumers see a single shape.
        return JsonSerializer.Serialize(root);
    }

    private static void ValidateGeofence(JsonElement root)
    {
        if (!root.TryGetProperty("polygon", out var polyElem) || polyElem.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Geofence rule requires a 'polygon' array.");
        }

        var vertexCount = polyElem.GetArrayLength();
        if (vertexCount < 3)
        {
            throw new InvalidOperationException("Geofence polygon requires at least 3 vertices.");
        }
        if (vertexCount > MaxPolygonVertices)
        {
            throw new InvalidOperationException(
                $"Geofence polygon exceeds {MaxPolygonVertices} vertices.");
        }

        var coordSpace = "pixel";
        if (root.TryGetProperty("coordinate_space", out var csElem) && csElem.ValueKind == JsonValueKind.String)
        {
            coordSpace = csElem.GetString() ?? "pixel";
            if (!AllowedCoordinateSpaces.Contains(coordSpace))
            {
                throw new InvalidOperationException(
                    $"Unsupported coordinate_space '{coordSpace}'. Allowed: pixel, normalized.");
            }
        }

        foreach (var vertex in polyElem.EnumerateArray())
        {
            if (vertex.ValueKind != JsonValueKind.Array || vertex.GetArrayLength() != 2)
            {
                throw new InvalidOperationException("Each polygon vertex must be [x, y].");
            }

            int i = 0;
            foreach (var coord in vertex.EnumerateArray())
            {
                if (coord.ValueKind != JsonValueKind.Number || !coord.TryGetDouble(out var d))
                {
                    throw new InvalidOperationException("Polygon coordinates must be numbers.");
                }
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    throw new InvalidOperationException("Polygon coordinates must be finite.");
                }
                if (coordSpace.Equals("normalized", StringComparison.OrdinalIgnoreCase))
                {
                    if (d < 0.0 || d > 1.0)
                    {
                        throw new InvalidOperationException(
                            "Normalized polygon coordinates must be within [0, 1].");
                    }
                }
                else
                {
                    // Pixel space: forbid negative; upper bound is unknown without frame size,
                    // so cap at a sane value to block obvious abuse.
                    if (d < 0.0 || d > 100_000.0)
                    {
                        throw new InvalidOperationException(
                            "Pixel polygon coordinates must be within [0, 100000].");
                    }
                }
                i++;
            }
        }

        if (root.TryGetProperty("mode", out var modeElem))
        {
            if (modeElem.ValueKind != JsonValueKind.String ||
                !AllowedGeofenceModes.Contains(modeElem.GetString() ?? string.Empty))
            {
                throw new InvalidOperationException(
                    $"Geofence 'mode' must be one of: {string.Join(", ", AllowedGeofenceModes)}.");
            }
        }

        if (root.TryGetProperty("anchor", out var anchorElem))
        {
            if (anchorElem.ValueKind != JsonValueKind.String ||
                !AllowedAnchors.Contains(anchorElem.GetString() ?? string.Empty))
            {
                throw new InvalidOperationException(
                    $"Geofence 'anchor' must be one of: {string.Join(", ", AllowedAnchors)}.");
            }
        }

        // Topology check: reject self-intersecting (bowtie) polygons.
        // Shapely's make_valid() converts them to MultiPolygon which the vision
        // worker then rejects fail-closed, producing zero alerts with no user
        // feedback. Catching the shape here means the API returns a 400 with a
        // clear message instead of silently eating the entire rule.
        ValidatePolygonTopology(polyElem);
        ValidateSourceFrameSize(root);
    }

    /// <summary>
    /// O(n²) crossing-edge test. For n ≤ 64 this is ~2 000 comparisons — trivial.
    /// Throws <see cref="InvalidOperationException"/> if any two non-adjacent edges
    /// properly cross (shared-vertex "touches" are excluded).
    /// </summary>
    private static void ValidatePolygonTopology(JsonElement polyElem)
    {
        var pts = new List<(double x, double y)>();
        foreach (var v in polyElem.EnumerateArray())
        {
            var coords = v.EnumerateArray().ToArray();
            // Malformed vertices are already caught above; bail gracefully here.
            if (coords.Length < 2
                || !coords[0].TryGetDouble(out var px)
                || !coords[1].TryGetDouble(out var py))
                return;
            pts.Add((px, py));
        }

        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            var a1 = pts[i];
            var a2 = pts[(i + 1) % n];
            for (int j = i + 2; j < n; j++)
            {
                // Skip the edge pair that shares the wrap-around vertex (i=0, j=n-1).
                if ((j + 1) % n == i) continue;
                var b1 = pts[j];
                var b2 = pts[(j + 1) % n];
                if (SegmentsProperlyIntersect(a1, a2, b1, b2))
                    throw new InvalidOperationException(
                        "Polygon edges self-intersect. Redraw the zone so no two edges cross.");
            }
        }
    }

    private static double Cross(
        (double x, double y) o,
        (double x, double y) a,
        (double x, double y) b)
        => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

    /// <summary>
    /// Returns true only when the two segments <em>properly</em> cross
    /// (neither endpoint lies on the other segment). Shared-vertex adjacency
    /// is intentionally excluded so a normal closed polygon doesn't trigger.
    /// </summary>
    private static bool SegmentsProperlyIntersect(
        (double x, double y) p1, (double x, double y) p2,
        (double x, double y) p3, (double x, double y) p4)
    {
        double d1 = Cross(p4, p3, p1);
        double d2 = Cross(p4, p3, p2);
        double d3 = Cross(p2, p1, p3);
        double d4 = Cross(p2, p1, p4);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static void ValidateSourceFrameSize(JsonElement root)
    {
        // Optional source_frame_size metadata: [width, height] in pixels. Lets
        // the UI auto-migrate legacy pixel-coord configs back to normalized.
        // Vision worker ignores this field; it's purely informational.
        if (!root.TryGetProperty("source_frame_size", out var sfsElem))
            return;

        if (sfsElem.ValueKind != JsonValueKind.Array || sfsElem.GetArrayLength() != 2)
        {
            throw new InvalidOperationException(
                "'source_frame_size' must be a [width, height] array.");
        }
        foreach (var dim in sfsElem.EnumerateArray())
        {
            if (dim.ValueKind != JsonValueKind.Number || !dim.TryGetDouble(out var dv) ||
                double.IsNaN(dv) || double.IsInfinity(dv) || dv <= 0 || dv > 100_000)
            {
                throw new InvalidOperationException(
                    "'source_frame_size' dimensions must be positive numbers <= 100000.");
            }
        }
    }

    private static void ValidateDwell(JsonElement root)
    {
        if (!root.TryGetProperty("duration_s", out var durElem))
        {
            throw new InvalidOperationException(
                "Dwell rule requires a 'duration_s' (seconds) number.");
        }
        if (durElem.ValueKind != JsonValueKind.Number || !durElem.TryGetDouble(out var d) ||
            double.IsNaN(d) || double.IsInfinity(d) || d <= 0 || d > 3600)
        {
            throw new InvalidOperationException(
                "Dwell 'duration_s' must be a number in (0, 3600].");
        }
    }

    private static void ValidateAnomaly(JsonElement root)
    {
        if (root.TryGetProperty("min_score", out var scoreElem))
        {
            if (scoreElem.ValueKind != JsonValueKind.Number ||
                !scoreElem.TryGetDouble(out var s) ||
                double.IsNaN(s) || double.IsInfinity(s) ||
                s < 0.0 || s > 1.0)
            {
                throw new InvalidOperationException(
                    "Anomaly 'min_score' must be a number in [0, 1].");
            }
        }

        if (root.TryGetProperty("target_labels", out var labelsElem))
        {
            if (labelsElem.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Anomaly 'target_labels' must be an array of strings.");
            }
            if (labelsElem.GetArrayLength() > 64)
            {
                throw new InvalidOperationException("Anomaly 'target_labels' is limited to 64 entries.");
            }
            foreach (var lbl in labelsElem.EnumerateArray())
            {
                if (lbl.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException(
                        "Anomaly 'target_labels' entries must be strings.");
                }
            }
        }
    }
}
