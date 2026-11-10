import ActivityKit
import Foundation

/// The live workout's Live Activity (0.20.0). Compiled into both the bridge (which starts and updates it) and the
/// widget extension (which draws it), so the two always agree on its shape. ActivityKit matches them by type name.
@available(iOS 16.1, *)
struct WorkoutActivityAttributes: ActivityAttributes {
    struct ContentState: Codable, Hashable {
        /// Working sets done out of the planned ones, 0 to 100.
        var percent: Int
        var doneSets: Int
        var plannedSets: Int
        /// The exercise to do next; nil once every exercise is done.
        var nextExercise: String?
    }

    /// "Push", or "Extra workout" on a rest day.
    var dayName: String
    var startedAt: Date
    /// When the session stops by itself (4 hours, or midnight); the timer stops there.
    var endsAt: Date
}
