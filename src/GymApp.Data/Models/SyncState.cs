using System;

namespace GymApp.Data.Models;

/// <summary>What still has to be uploaded to the social server for one user. A null time means in sync; a time is when it
/// first changed since the last successful upload.</summary>
public class SyncState
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime? StatsDirtySince { get; set; }
    public DateTime? CheckInsDirtySince { get; set; }
    public DateTime? SharingDirtySince { get; set; }
    /// <summary>The last refusal, for the log only; never shown or retried.</summary>
    public string? LastError { get; set; }
}
