namespace AlphaSurveilance.Core.Enums
{
    public enum WorkstationStatus
    {
        Staffed = 0,
        PendingHandover = 1,
        UnderRelief = 2,
        UnattendedViolation = 3,
        Inactive = 4
    }

    public enum ReliefSessionStatus
    {
        PendingHandover = 0,
        ActiveRelief = 1,
        Completed = 2,
        UnattendedViolation = 3,
        OverdueAlert = 4
    }

    public enum WorkstationWorkerRole
    {
        Primary = 0,
        Reliever = 1
    }
}
