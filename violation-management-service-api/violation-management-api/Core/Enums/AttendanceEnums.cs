namespace AlphaSurveilance.Core.Enums
{
    public enum AttendanceMode
    {
        None = 0,
        MarkIn = 1,
        MarkOut = 2,
        Bidirectional = 3
    }

    public enum AttendanceRecordStatus
    {
        Active = 0,
        Present = 1,
        Completed = 2,
        AutoClosed = 3
    }

    public enum AttendanceEventType
    {
        CheckIn = 0,
        CheckOut = 1,
        Heartbeat = 2
    }
}
