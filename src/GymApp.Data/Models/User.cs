using System;
using System.Collections.Generic;
using GymApp.Core;

namespace GymApp.Data.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string? Email { get; set; }               // stored lowercased; null for accounts made before emails existed
    public string PasswordHash { get; set; } = "";   // never the plain password
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? RememberTokenHash { get; set; }
    /// <summary>The linked social-server account, the real login since Phase 14; null for an account not moved online
    /// (and in the TestBench's local mode). Email and PasswordHash are then its cache, for signing in offline.</summary>
    public int? ServerUserId { get; set; }

    public int? SelectedSplitId { get; set; }
    public WorkoutSplit? SelectedSplit { get; set; }
    public int? SelectedVariantId { get; set; }
    public SplitVariant? SelectedVariant { get; set; }
    public DayOfWeek? SplitStartDay { get; set; }   // null = Monday; only used for presets
    public string? SelectedCity { get; set; }

    public int Points { get; set; }                   // cached sum of PointsEntries; level is derived from this
    public bool PointsBackfilled { get; set; }        // the one-time full points rebuild has run
    public bool BadgesBackfilled { get; set; }        // badges from the history before Phase 25 were stored, quietly
    public bool BadgeHistorySheetPending { get; set; } // that backfill found badges: show "You earned N badges from your history" once
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    /// <summary>The first day rest days stopped adding to the streak by themselves (0.18.1): the day this user's app
    /// first recalculated the streak on that version. Null until then. Rest days before it still count +1.</summary>
    public DateOnly? StreakRestRuleFrom { get; set; }

    public int? DailyCalorieGoal { get; set; }
    public GoalType GoalType { get; set; } = GoalType.Maintain;
    /// <summary>Optional grams-per-day targets for the macro bars (Phase 27); 1 to 1,000 or null.</summary>
    public int? ProteinGoalGrams { get; set; }
    public int? CarbsGoalGrams { get; set; }
    public int? FatGoalGrams { get; set; }
    public int? LastSuggestedGoal { get; set; }
    /// <summary>Recommended: the goal and macro targets follow <see cref="MacroPlan"/>; Custom: the user's own grams, with the goal
    /// worked out from them. Null until the user picks one on the goal sheet (Phase 27); the goal then stays as it was.</summary>
    public MacroMode? MacroMode { get; set; }
    /// <summary>The trend weight the recommended targets were last worked out from; they move again only a kilo away from it.</summary>
    public double? MacroBasisWeightKg { get; set; }       // the suggestion the user last accepted or declined, so we only ask again when it changes

    public ThemePreference ThemePreference { get; set; } = ThemePreference.Dark;   // applied at sign-in (12e); Dark by default since 0.27.1

    // Reminders on the phones (16g): off until the user turns them on in Profile
    public bool RemindersEnabled { get; set; }
    /// <summary>Notification permission was asked at the first Start workout (Phase 18); Start never asks again.</summary>
    public bool WorkoutNotificationsAsked { get; set; }
    public bool TrainingReminderEnabled { get; set; } = true;
    public TimeOnly TrainingReminderTime { get; set; } = new(18, 0);
    public bool MissedDayReminderEnabled { get; set; } = true;

    /// <summary>Friend notifications on this phone (Phase 22): null until decided (asked, or turned on by itself because
    /// the phone already allows notifications), then Profile's switch.</summary>
    public bool? FriendNotifications { get; set; }
    /// <summary>"Not now" on Friends' card asking to turn them on: it never shows again (Profile still has the switch).</summary>
    public bool FriendNotificationsCardDismissed { get; set; }

    public List<WorkoutLog> WorkoutLogs { get; set; } = new();
    public List<CalorieLog> CalorieLogs { get; set; } = new();
}
