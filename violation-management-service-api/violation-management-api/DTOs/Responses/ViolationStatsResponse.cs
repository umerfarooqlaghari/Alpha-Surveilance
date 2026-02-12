namespace AlphaSurveilance.DTOs.Responses
{
    public class ViolationStatsResponse
    {
        public int TotalCameras { get; set; }
        public int ActiveViolations { get; set; }
        public int ResolvedToday { get; set; }
    }
}
