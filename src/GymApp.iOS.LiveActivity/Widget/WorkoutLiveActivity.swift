import ActivityKit
import SwiftUI
import WidgetKit

// The widget extension (0.20.0): draws the live workout's Live Activity in the Dynamic Island and on the Lock Screen.
// The data comes from the app (WorkoutActivityAttributes); nothing here reads the database.

@main
struct GainStreakWidgets: WidgetBundle {
    var body: some Widget {
        WorkoutLiveActivity()
    }
}

/// The app's accent and its dark page (IronlogResources' Accent and PageBg).
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
                    Ring(percent: state.percent, lineWidth: 5) {
                        Text("\(state.percent)%").font(.system(size: 13, weight: .bold)).monospacedDigit()
                    }
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
                    Text(context.attributes.dayName)
                        .font(.system(size: 15, weight: .semibold))
                        .lineLimit(1)
                }
                DynamicIslandExpandedRegion(.bottom) {
                    Details(state: state).padding(.horizontal, 4)
                }
            } compactLeading: {
                Ring(percent: state.percent, lineWidth: 3) { EmptyView() }
                    .frame(width: 18, height: 18)
            } compactTrailing: {
                Text("\(state.percent)%")
                    .font(.system(size: 14, weight: .bold))
                    .monospacedDigit()
                    .foregroundStyle(accent)
            } minimal: {
                Ring(percent: state.percent, lineWidth: 2.5) {
                    Text("\(state.percent)").font(.system(size: 9, weight: .bold)).monospacedDigit()
                }
                .frame(width: 22, height: 22)
            }
            .keylineTint(accent)
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

/// "Next: Bench press", the sets, and a bar.
struct Details: View {
    let state: WorkoutActivityAttributes.ContentState

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(state.nextExercise.map { "Next: \($0)" } ?? "All sets done")
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
                    Text(attributes.dayName).font(.system(size: 16, weight: .semibold)).lineLimit(1)
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
