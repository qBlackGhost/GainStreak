import ActivityKit
import Foundation

// The app's side of the Live Activity (0.20.0). ActivityKit has no Objective-C API, so the C# head can't call it
// directly: these two C functions are what IosLiveActivity calls, through function pointers. They return at once; the
// work runs in order on a task chain, so a quick show-show-end can't arrive out of order.

private let lock = NSLock()
private var chain: Task<Void, Never>?

/// Runs `work` after everything queued before it.
private func enqueue(_ work: @escaping @Sendable () async -> Void) {
    lock.lock()
    let previous = chain
    chain = Task {
        await previous?.value
        await work()
    }
    lock.unlock()
}

/// Starts the activity, or updates the one for this workout. A leftover activity from another workout ends. Starting
/// only works while the app is in front; from the background only the update goes through, which is all it needs: the
/// app keeps the activity up from the moment the workout starts.
@_cdecl("gs_live_activity_show")
public func gs_live_activity_show(_ dayName: UnsafePointer<CChar>, _ percent: Int32, _ doneSets: Int32,
                                  _ plannedSets: Int32, _ nextExercise: UnsafePointer<CChar>?,
                                  _ startedAt: Double, _ endsAt: Double) {
    let day = String(cString: dayName)
    let next = nextExercise.map { String(cString: $0) }
    guard #available(iOS 16.2, *) else { return }
    let attributes = WorkoutActivityAttributes(dayName: day, startedAt: Date(timeIntervalSince1970: startedAt),
                                               endsAt: Date(timeIntervalSince1970: endsAt))
    let state = WorkoutActivityAttributes.ContentState(percent: Int(percent), doneSets: Int(doneSets),
                                                       plannedSets: Int(plannedSets), nextExercise: next)
    enqueue {
        let content = ActivityContent(state: state, staleDate: attributes.endsAt)
        var mine: Activity<WorkoutActivityAttributes>?
        for activity in Activity<WorkoutActivityAttributes>.activities {
            if mine == nil, activity.activityState == .active, activity.attributes.startedAt == attributes.startedAt {
                mine = activity
            } else {
                await activity.end(nil, dismissalPolicy: .immediate)
            }
        }
        if let mine {
            if mine.content.state != state { await mine.update(content) }
        } else if ActivityAuthorizationInfo().areActivitiesEnabled {
            _ = try? Activity.request(attributes: attributes, content: content, pushType: nil)
        }
    }
}

/// Ends every workout activity: the workout finished, ended or stopped by itself, or the user signed out.
@_cdecl("gs_live_activity_end")
public func gs_live_activity_end() {
    guard #available(iOS 16.2, *) else { return }
    enqueue {
        for activity in Activity<WorkoutActivityAttributes>.activities {
            await activity.end(nil, dismissalPolicy: .immediate)
        }
    }
}
