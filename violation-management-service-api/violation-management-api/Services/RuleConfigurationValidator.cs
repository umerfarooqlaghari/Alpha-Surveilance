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
        "access_control",
        "unauthorized_access",
        "reliever",
        "workstation_relief",
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
            case "reliever":
            case "workstation_relief":
                ValidateGeofence(root);
                ValidateReliever(root);
                break;
        }

        // Return canonical compact JSON so downstream consumers see a single shape.
        return JsonSerializer.Serialize(root);
    }

    private static void ValidateReliever(JsonElement root)
    {
        if (root.TryGetProperty("handover_threshold_s", out var thElem))
        {
            if (thElem.ValueKind != JsonValueKind.Number || !thElem.TryGetDouble(out var th) || th <= 0 || th > 3600)
            {
                throw new InvalidOperationException("Reliever 'handover_threshold_s' must be a positive number in (0, 3600].");
            }
        }

        if (root.TryGetProperty("max_relief_duration_s", out var maxElem))
        {
            if (maxElem.ValueKind != JsonValueKind.Number || !maxElem.TryGetDouble(out var max) || max <= 0 || max > 86400)
            {
                throw new InvalidOperationException("Reliever 'max_relief_duration_s' must be a positive number in (0, 86400].");
            }
        }

        if (root.TryGetProperty("required_primaries", out var reqElem))
        {
            if (reqElem.ValueKind != JsonValueKind.Number || !reqElem.TryGetInt32(out var req) || req < 1 || req > 100)
            {
                throw new InvalidOperationException("Reliever 'required_primaries' must be an integer between 1 and 100.");
            }
        }
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

    /// <summary>
    /// Validates an operating schedule JSON string for timing windows and enforces the Window Overlap Guard.
    /// Throws InvalidOperationException if timing windows overlap on the same days or are malformed.
    /// </summary>
    public static void ValidateOperatingSchedule(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Operating schedule must be a JSON array of timing windows.");
        }

        var windows = new List<(string Label, TimeOnly StartTime, TimeOnly EndTime, HashSet<int> Days)>();
        foreach (var elem in doc.RootElement.EnumerateArray())
        {
            if (elem.ValueKind != JsonValueKind.Object) continue;

            var isActive = !elem.TryGetProperty("isActive", out var activeElem) || activeElem.GetBoolean();
            if (!isActive) continue; // Inactive windows do not conflict

            var label = elem.TryGetProperty("label", out var labelElem) ? labelElem.GetString() ?? "Shift" : "Shift";

            if (!elem.TryGetProperty("startTime", out var startElem) || !TimeOnly.TryParse(startElem.GetString(), out var startTime))
            {
                throw new InvalidOperationException($"Shift '{label}' has an invalid or missing startTime (expected 'HH:mm').");
            }

            if (!elem.TryGetProperty("endTime", out var endElem) || !TimeOnly.TryParse(endElem.GetString(), out var endTime))
            {
                throw new InvalidOperationException($"Shift '{label}' has an invalid or missing endTime (expected 'HH:mm').");
            }

            if (startTime == endTime)
            {
                throw new InvalidOperationException($"Shift '{label}' has identical start and end time ({startTime:HH:mm}).");
            }

            var days = new HashSet<int>();
            if (elem.TryGetProperty("daysOfWeek", out var daysElem) && daysElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in daysElem.EnumerateArray())
                {
                    if (d.TryGetInt32(out var dayInt) && dayInt >= 0 && dayInt <= 6)
                    {
                        days.Add(dayInt);
                    }
                }
            }
            if (days.Count == 0)
            {
                for (int i = 0; i <= 6; i++) days.Add(i);
            }

            windows.Add((label, startTime, endTime, days));
        }

        // Window Overlap Guard: Check every pair of active windows
        for (int i = 0; i < windows.Count; i++)
        {
            for (int j = i + 1; j < windows.Count; j++)
            {
                var w1 = windows[i];
                var w2 = windows[j];

                var sharedDays = w1.Days.Intersect(w2.Days).ToList();
                if (sharedDays.Count == 0) continue;

                if (TimesOverlap(w1.StartTime, w1.EndTime, w2.StartTime, w2.EndTime))
                {
                    var dayNames = string.Join(", ", sharedDays.Select(d => ((DayOfWeek)d).ToString()));
                    throw new InvalidOperationException(
                        $"Timing window conflict: '{w1.Label}' ({w1.StartTime:HH:mm} - {w1.EndTime:HH:mm}) overlaps with '{w2.Label}' ({w2.StartTime:HH:mm} - {w2.EndTime:HH:mm}) on {dayNames}. Operating windows cannot overlap.");
                }
            }
        }
    }

    private static bool TimesOverlap(TimeOnly s1, TimeOnly e1, TimeOnly s2, TimeOnly e2)
    {
        // Decompose each window into [start, end) intervals (in total minutes from 0 to 1440)
        var intervals1 = GetIntervalsInMinutes(s1, e1);
        var intervals2 = GetIntervalsInMinutes(s2, e2);

        foreach (var (start1, end1) in intervals1)
        {
            foreach (var (start2, end2) in intervals2)
            {
                // [start1, end1) overlaps [start2, end2) if max(start1, start2) < min(end1, end2)
                if (Math.Max(start1, start2) < Math.Min(end1, end2))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static List<(int StartMin, int EndMin)> GetIntervalsInMinutes(TimeOnly start, TimeOnly end)
    {
        int s = start.Hour * 60 + start.Minute;
        int e = end.Hour * 60 + end.Minute;

        if (s < e)
        {
            // Standard window, e.g. 08:00 to 16:00
            return new List<(int, int)> { (s, e) };
        }
        else
        {
            // Overnight window spanning midnight, e.g. 22:00 to 06:00 -> [22:00, 24:00) and [00:00, 06:00)
            return new List<(int, int)> { (s, 1440), (0, e) };
        }
    }
}

