import ActivityKit
import SwiftUI
import WidgetKit

// The widget extension (0.20.0): draws the live workout's Live Activity in the Dynamic Island and on the Lock Screen.
// The data comes from the app (WorkoutActivityAttributes); nothing here reads the database. Since Phase 24 it also
// holds the Home Screen widgets (iOS 17) and the Control Center controls (iOS 18), in HomeWidgets.swift.

@main
struct GainStreakWidgets: WidgetBundle {
    var body: some Widget {
        WorkoutLiveActivity()
        if #available(iOS 17.0, *) {
            StatWidget()
            PlanWidget()
            ProgressWidget()
            BodyWidget()
            OverviewWidget()
        }
        if #available(iOS 18.0, *) { StartWorkoutControl() }
        if #available(iOS 18.0, *) { TodayPlanControl() }
        if #available(iOS 18.0, *) { LogWeightControl() }
    }
}

/// The app's accent and its dark page (GainStreakResources' Accent and PageBg).
private let accent = Color(red: 0xC6 / 255, green: 0xF4 / 255, blue: 0x32 / 255)
private let track = Color.white.opacity(0.18)
private let page = Color.black

struct WorkoutLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: WorkoutActivityAttributes.self) { context in
            LockScreenView(attributes: context.attributes, state: context.state)
                .activityBackgroundTint(page)
                .activitySystemActionForegroundColor(accent)
        } dynamicIsland: { context in
            let state = context.state
            return DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    Ring(percent: state.percent, lineWidth: 5) { Dumbbell(size: 18) }
                    .frame(width: 48, height: 48)
                    .padding(.leading, 4)
                }
                DynamicIslandExpandedRegion(.trailing) {
                    WorkoutTimer(attributes: context.attributes)
                        .font(.system(size: 15, weight: .semibold))
                        .foregroundStyle(.secondary)
                        .frame(maxHeight: .infinity)
                        .padding(.trailing, 4)
                }
                DynamicIslandExpandedRegion(.center) {
                    DayTitle(dayName: context.attributes.dayName)
                        .font(.system(size: 15, weight: .semibold))
                        .lineLimit(1)
                }
                DynamicIslandExpandedRegion(.bottom) {
                    IslandProgress(state: state).padding(.horizontal, 4)
                }
            } compactLeading: {
                Ring(percent: state.percent, lineWidth: 2.5) { Dumbbell(size: 9) }
                    .frame(width: 22, height: 22)
            } compactTrailing: {
                Text("\(state.percent)%")
                    .font(.system(size: 14, weight: .bold))
                    .monospacedDigit()
                    .foregroundStyle(accent)
            } minimal: {
                // Sharing the island with another app (music, a timer): the same ring and dumbbell, on one side
                Ring(percent: state.percent, lineWidth: 2.5) { Dumbbell(size: 10) }
                .frame(width: 24, height: 24)
            }
            .keylineTint(accent)
        }
    }
}

/// The streak flame's orange (the drafts' flame, 0.27.1).
private let flameOrange = Color(red: 0xFF / 255, green: 0x7A / 255, blue: 0x1A / 255)

/// "Push" with the app's outline flame in orange before it (0.27.1), not the emoji.
struct DayTitle: View {
    let dayName: String

    var body: some View {
        HStack(spacing: 5) {
            Image(systemName: "flame").foregroundStyle(flameOrange)
            Text(dayName)
        }
    }
}

/// The dumbbell inside the island's ring (0.27.1).
struct Dumbbell: View {
    let size: CGFloat

    var body: some View {
        Image(systemName: "dumbbell.fill")
            .font(.system(size: size, weight: .bold))
            .foregroundStyle(accent)
    }
}

/// The expanded island's bottom row (0.27.1): the exercise on the card, the percent in lime, and a bar.
struct IslandProgress: View {
    let state: WorkoutActivityAttributes.ContentState

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(state.nextExercise ?? "All sets done")
                    .font(.system(size: 14, weight: .medium))
                    .lineLimit(1)
                Spacer()
                Text("\(state.percent)%")
                    .font(.system(size: 14, weight: .bold))
                    .monospacedDigit()
                    .foregroundStyle(accent)
            }
            ProgressView(value: Double(min(state.doneSets, state.plannedSets)), total: Double(max(state.plannedSets, 1)))
                .tint(accent)
        }
    }
}

/// The percent as an arc, like the live card's.
struct Ring<Label: View>: View {
    let percent: Int
    let lineWidth: CGFloat
    @ViewBuilder let label: () -> Label

    var body: some View {
        ZStack {
            Circle().stroke(track, lineWidth: lineWidth)
            Circle()
                .trim(from: 0, to: CGFloat(min(max(percent, 0), 100)) / 100)
                .stroke(accent, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
            label()
        }
    }
}

/// Counts up from the start; stops where the session would stop by itself.
struct WorkoutTimer: View {
    let attributes: WorkoutActivityAttributes

    var body: some View {
        Text(timerInterval: attributes.startedAt...max(attributes.startedAt, attributes.endsAt), countsDown: false)
            .monospacedDigit()
            .multilineTextAlignment(.trailing)
    }
}

/// "Now: Bench press", the sets, and a bar (the Lock Screen).
struct Details: View {
    let state: WorkoutActivityAttributes.ContentState

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(state.nextExercise.map { "Now: \($0)" } ?? "All sets done")
                    .font(.system(size: 14, weight: .medium))
                    .lineLimit(1)
                Spacer()
                Text("\(state.doneSets) of \(state.plannedSets) sets")
                    .font(.system(size: 13))
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
            ProgressView(value: Double(min(state.doneSets, state.plannedSets)), total: Double(max(state.plannedSets, 1)))
                .tint(accent)
        }
    }
}

struct LockScreenView: View {
    let attributes: WorkoutActivityAttributes
    let state: WorkoutActivityAttributes.ContentState

    var body: some View {
        HStack(spacing: 14) {
            Ring(percent: state.percent, lineWidth: 6) {
                Text("\(state.percent)%").font(.system(size: 15, weight: .bold)).monospacedDigit()
            }
            .frame(width: 58, height: 58)
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    DayTitle(dayName: attributes.dayName).font(.system(size: 16, weight: .semibold)).lineLimit(1)
                    Spacer()
                    WorkoutTimer(attributes: attributes)
                        .font(.system(size: 14, weight: .medium))
                        .foregroundStyle(.secondary)
                        .frame(width: 70, alignment: .trailing)
                }
                Details(state: state)
            }
        }
        .foregroundStyle(.white)
        .padding(16)
    }
}
