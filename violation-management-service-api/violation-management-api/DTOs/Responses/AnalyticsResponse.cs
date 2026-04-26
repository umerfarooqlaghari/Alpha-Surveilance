using System;
using System.Collections.Generic;

namespace AlphaSurveilance.DTOs.Responses
{
    public class AnalyticsResponse
    {
        public AnalyticsSummary Summary { get; set; } = new();
        public List<TrendData> DailyTrends { get; set; } = new();
        public List<CategoryData> ByCategory { get; set; } = new();
        public List<SeverityData> BySeverity { get; set; } = new();
        public List<HeatmapData> HourlyHeatmap { get; set; } = new();
        public List<CameraData> ByCamera { get; set; } = new();
        public List<StatusData> ByStatus { get; set; } = new();
    }

    public class StatusData
    {
        public string? Status { get; set; }
        public int Count { get; set; }
    }

    public class CameraData
    {
        public string? CameraName { get; set; }
        public int Count { get; set; }
    }

    public class AnalyticsSummary
    {
        public int TotalViolations { get; set; }
        public int ActiveViolations { get; set; }
        public int ResolvedViolations { get; set; }
        public int CriticalViolations { get; set; }
    }

    public class TrendData
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
    }

    public class CategoryData
    {
        public string? Type { get; set; }
        public int Count { get; set; }
    }

    public class SeverityData
    {
        public string? Severity { get; set; }
        public int Count { get; set; }
    }

    public class HeatmapData
    {
        public string? CameraName { get; set; }
        public int Hour { get; set; } // 0-23
        public int Count { get; set; }
    }
}
