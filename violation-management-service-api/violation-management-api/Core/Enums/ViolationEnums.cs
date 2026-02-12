namespace AlphaSurveilance.Core.Enums
{
    public enum ViolationType
    {
        Unknown = 0,
        Safety = 1,
        Security = 2,
        Operational = 3,
        Compliance = 4
    }

    public enum ViolationSeverity
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }

    public enum AuditStatus
    {
        Pending = 0,
        Audited = 1,
        FailedAudit = 2
    }
}
