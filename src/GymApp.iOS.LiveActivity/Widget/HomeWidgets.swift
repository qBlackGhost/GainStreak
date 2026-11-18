import AppIntents
import SwiftUI
import WidgetKit

// The Home Screen widgets and Control Center controls (Phase 24). The app writes widget.json into the App Group
// (WidgetSnapshot in GymApp.Services) whenever something changes and asks WidgetKit to reload; nothing here opens the
// database. A tap opens the app with a gainstreak://open/<target> link (GymApp.Core.WidgetLink).

// MARK: - The snapshot

struct Snapshot: Codable {
    var version: Int
    var date: String
    var signedIn: Bool
    var streak: Streak?
    var level: Level?
    var today: Day?
    var tomorrow: Day?
    var weight: Weight?
    var steps: Steps?

    struct Streak: Codable {
        var current: Int; var status: String
        /// On vacation: the first normal day again, "yyyy-MM-dd".
        var vacationUntil: String? = nil
    }
    struct Level: Codable {
        var number: Int; var name: String; var points: Int; var progress: Double; var nextName: String?; var toNext: Int?
    }
    struct Day: Codable {
        var date: String; var hasSplit: Bool; var name: String?; var exercises: [Exercise]; var exerciseCount: Int
    }
    struct Exercise: Codable { var name: String; var last: String }
    struct Weight: Codable {
        var current: String; var unit: String; var change7: String?; var change30: String?; var trend30: [Double]
    }
    struct Steps: Codable { var today: Int; var week: [Int] }

    /// The app's App Group, stamped into the extension's Info.plist by build.sh.
    static var groupId: String? { Bundle.main.object(forInfoDictionaryKey: "GainStreakAppGroup") as? String }

    static func load() -> Snapshot? {
        guard let id = groupId,
              let folder = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: id),
              let data = try? Data(contentsOf: folder.appendingPathComponent("widget.json")) else { return nil }
        return try? JSONDecoder().decode(Snapshot.self, from: data)
    }

    /// What the widget gallery and the placeholder show.
    static let sample = Snapshot(
        version: 1, date: dayString(Date()), signedIn: true,
        streak: Streak(current: 12, status: "pending"),
        level: Level(number: 1, name: "Beginner", points: 80, progress: 0.8, nextName: "Novice", toNext: 20),
        today: Day(date: dayString(Date()), hasSplit: true, name: "Legs", exercises: [
            Exercise(name: "Back squat", last: "100 kg × 5"), Exercise(name: "Romanian deadlift", last: "90 kg × 8"),
            Exercise(name: "Leg press", last: "180 kg × 10"), Exercise(name: "Leg press calf raise", last: "160 kg × 10"),
        ], exerciseCount: 4),
        tomorrow: Day(date: dayString(Date().addingTimeInterval(86400)), hasSplit: true, name: nil, exercises: [], exerciseCount: 0),
        weight: Weight(current: "78.4", unit: "kg", change7: "−0.6", change30: "−1.8",
                       trend30: [80.2, 80.0, 79.9, 79.7, 79.6, 79.2, 79.1, 78.9, 78.8, 78.6, 78.4]),
        steps: Steps(today: 6240, week: [5200, 8100, 4300, 9900, 7600, 11200, 6240]))
}

func dayString(_ date: Date) -> String {
    let f = DateFormatter()
    f.calendar = Calendar(identifier: .gregorian)
    f.locale = Locale(identifier: "en_US_POSIX")
    f.dateFormat = "yyyy-MM-dd"
    return f.string(from: date)
}

// MARK: - What a widget shows at a given moment

/// The snapshot as of an entry's date: on the snapshot's day as written; the next day from its `tomorrow`, as far as
/// it can be known without the app; later, a request to open the app.
struct Shown {
    enum Kind { case signedOut, current, stale }
    var kind: Kind
    var streak = 0
    var status = "pending"
    var day: Snapshot.Day?
    var level: Snapshot.Level?
    var weight: Snapshot.Weight?
    var steps: Snapshot.Steps?

    init(_ snapshot: Snapshot?, at date: Date) {
        // No file yet (the app not opened since this update): ask to open it, not to sign in
        guard let s = snapshot else { kind = .stale; return }
        guard s.signedIn else { kind = .signedOut; return }
        level = s.level
        weight = s.weight
        let today = dayString(date)
        if today == s.date {
            kind = .current
            streak = s.streak?.current ?? 0
            status = s.streak?.status ?? "pending"
            day = s.today
            steps = s.steps
        } else if let tomorrow = s.tomorrow, today == tomorrow.date {
            day = tomorrow
            streak = s.streak?.current ?? 0
            switch s.streak?.status ?? "" {
            case "vacation":
                kind = .current
                // The return date is a normal day again: then tomorrow reads like any other day
                if let until = s.streak?.vacationUntil, tomorrow.date >= until {
                    status = tomorrow.hasSplit && tomorrow.name == nil ? "rest" : "pending"
                } else {
                    status = "vacation"
                }
            case "done", "rest":
                kind = .current
                status = tomorrow.hasSplit && tomorrow.name == nil ? "rest" : "pending"
            default: kind = .stale   // yesterday was still open: only the app knows what became of the streak
            }
        } else {
            kind = .stale
        }
    }

    var isRest: Bool { day?.hasSplit == true && day?.name == nil }

    var dayTitle: String {
        guard let day else { return "—" }
        if !day.hasSplit { return "NO SPLIT" }
        return (day.name ?? "Rest").uppercased()
    }

    var daySub: String {
        guard let day else { return "" }
        if !day.hasSplit { return "Pick a split in the app" }
        if day.name == nil { return "Rest counts toward your streak" }
        return day.exerciseCount == 1 ? "1 exercise" : "\(day.exerciseCount) exercises"
    }

    var statusText: String {
        let name = day?.name ?? "Today"
        switch status {
        case "done": return "\(name) done today"
        case "live": return "Workout in progress"
        case "rest": return "Rest day · streak safe"
        case "atRisk": return "Train today to keep it"
        case "needsChoice": return "Yesterday was missed"
        case "broken": return "Start a new streak today"
        case "notStarted": return "Log a workout to start"
        case "vacation": return "On vacation · paused"
        default: return "\(name) not logged yet"
        }
    }

    var statusColor: Color {
        switch status {
        case "done", "live": return Palette.accentText
        case "rest", "vacation", "notStarted": return Palette.muted
        case "atRisk", "needsChoice", "broken": return Palette.bad
        default: return Palette.today
        }
    }

    var flameColor: Color { status == "done" ? Palette.fire : Palette.accent }
    var streakColor: Color { status == "done" ? Palette.fire : Palette.ink }
}

// MARK: - Look: the app's tokens (IronlogResources), light and dark

enum Palette {
    static func dynamic(_ light: UInt32, _ dark: UInt32) -> Color {
        Color(UIColor { $0.userInterfaceStyle == .dark ? UIColor(hex: dark) : UIColor(hex: light) })
    }
    static let card = dynamic(0xFFFFFF, 0x0B0B0B)
    static let line = dynamic(0xE2E2DD, 0x222222)
    static let track = dynamic(0xECECE8, 0x1A1A1A)
    /// The week's other step bars: the track is too faint for bars standing alone (IronlogResources' Border).
    static let barOff = dynamic(0xD0D0CB, 0x333333)
    static let chip = dynamic(0xEFEFEB, 0x161616)
    static let ink = dynamic(0x0B0B0B, 0xFFFFFF)
    static let soft = dynamic(0x4A4A47, 0xB5B5B5)
    static let muted = dynamic(0x62625E, 0x9A9A9A)
    static let accent = dynamic(0x4D7C0F, 0xC6F432)
    static let accentText = dynamic(0x3F6A0A, 0xC6F432)
    static let onAccent = dynamic(0xFFFFFF, 0x000000)
    static let today = dynamic(0x5B7186, 0xB9C8D6)
    static let fire = dynamic(0xC2410C, 0xFF7A00)
    static let bad = dynamic(0xB42318, 0xFF8A7A)
}

extension UIColor {
    convenience init(hex: UInt32) {
        self.init(red: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
                  blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
    }
}

private func kicker(_ text: String, color: Color = Palette.soft) -> some View {
    Text(text).font(.system(size: 11, weight: .bold)).tracking(1.1).foregroundStyle(color).lineLimit(1)
}

private func big(_ text: String, size: CGFloat, color: Color = Palette.ink) -> some View {
    Text(text).font(.system(size: size, weight: .black)).foregroundStyle(color).lineLimit(1).minimumScaleFactor(0.6)
}

private func mono(_ text: String, color: Color = Palette.muted) -> some View {
    Text(text).font(.system(size: 11, weight: .semibold, design: .monospaced)).foregroundStyle(color).lineLimit(1)
}

private func link(_ target: String) -> URL { URL(string: "gainstreak://open/\(target)")! }

private struct Flame: View {
    var color: Color
    var size: CGFloat = 14
    var body: some View {
        Image(systemName: "flame.fill").font(.system(size: size * 0.9, weight: .bold)).foregroundStyle(color)
    }
}

private struct Bar: View {
    var progress: Double
    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(Palette.track)
                Capsule().fill(Palette.accent).frame(width: max(6, g.size.width * min(max(progress, 0), 1)))
            }
        }
        .frame(height: 6)
    }
}

private struct LevelBadge: View {
    var number: Int
    var body: some View {
        VStack(spacing: 0) {
            Text("LVL").font(.system(size: 7, weight: .bold)).tracking(0.6)
            Text("\(number)").font(.system(size: 16, weight: .black))
        }
        .foregroundStyle(Palette.onAccent)
        .frame(width: 34, height: 34)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Palette.accent))
    }
}

/// Signed out, no snapshot yet, or one too old to trust.
private struct OpenAppView: View {
    var shown: Shown
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 6) { Flame(color: Palette.accent); kicker("GAINSTREAK") }
            Spacer(minLength: 0)
            Text(shown.kind == .signedOut ? "Sign in to see your streak" : "Open GainStreak to update")
                .font(.system(size: 15, weight: .semibold)).foregroundStyle(Palette.ink)
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

// MARK: - Pieces

private struct StreakBlock: View {
    var shown: Shown
    var numberSize: CGFloat = 52
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 6) { Flame(color: shown.flameColor); kicker("STREAK") }
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                big("\(shown.streak)", size: numberSize, color: shown.streakColor)
                Text(shown.streak == 1 ? "day" : "days").font(.system(size: 13, weight: .semibold)).foregroundStyle(Palette.muted)
            }
            .padding(.top, 4)
            Spacer(minLength: 0)
            Text(shown.statusText).font(.system(size: 12, weight: .semibold)).foregroundStyle(shown.statusColor)
                .lineLimit(2).minimumScaleFactor(0.85)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

private struct LevelBlock: View {
    var level: Snapshot.Level?
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if let level {
                HStack(spacing: 8) {
                    LevelBadge(number: level.number)
                    Text(level.name).font(.system(size: 13, weight: .bold)).foregroundStyle(Palette.ink).lineLimit(1)
                }
                HStack(alignment: .firstTextBaseline, spacing: 5) {
                    big("\(level.points)", size: 36)
                    Text("pts").font(.system(size: 13, weight: .semibold)).foregroundStyle(Palette.muted)
                }
                .padding(.top, 10)
                Spacer(minLength: 0)
                Bar(progress: level.progress)
                Text(level.toNext.map { t in "\(t) to \(level.nextName ?? "next")" } ?? "Top level")
                    .font(.system(size: 12)).foregroundStyle(Palette.muted).padding(.top, 6).lineLimit(1)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

private struct PlanBlock: View {
    var shown: Shown
    var title: CGFloat = 34
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            kicker("TODAY", color: Palette.accentText)
            big(shown.dayTitle, size: title).padding(.top, 6)
            Text(shown.daySub).font(.system(size: 12)).foregroundStyle(Palette.muted).lineLimit(2).padding(.top, 6)
            Spacer(minLength: 0)
            mono(shown.status == "done" ? "Logged" : shown.status == "live" ? "In progress" : shown.isRest ? "Rest day" : "Today's plan",
                 color: Palette.soft)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

private struct ExerciseList: View {
    var exercises: [Snapshot.Exercise]
    var limit: Int
    var inline = false
    var body: some View {
        VStack(alignment: .leading, spacing: inline ? 7 : 0) {
            ForEach(Array(exercises.prefix(limit).enumerated()), id: \.offset) { _, e in
                if inline {
                    HStack(alignment: .firstTextBaseline) {
                        Text(e.name).font(.system(size: 13, weight: .semibold)).foregroundStyle(Palette.ink).lineLimit(1)
                        Spacer(minLength: 8)
                        mono(e.last)
                    }
                } else {
                    VStack(alignment: .leading, spacing: 1) {
                        Text(e.name).font(.system(size: 13, weight: .semibold)).foregroundStyle(Palette.ink).lineLimit(1)
                        mono(e.last)
                    }
                    .frame(maxHeight: .infinity, alignment: .center)
                }
            }
        }
    }
}

private struct WeightBlock: View {
    var weight: Snapshot.Weight?
    var change: String?
    var changeLabel: String
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            kicker("WEIGHT")
            if let weight {
                HStack(alignment: .firstTextBaseline, spacing: 4) {
                    big(weight.current, size: 28)
                    Text(weight.unit).font(.system(size: 13, weight: .semibold)).foregroundStyle(Palette.muted)
                }
                .padding(.top, 4)
                if let change {
                    Text("\(change) \(weight.unit) \(changeLabel)").font(.system(size: 12)).foregroundStyle(Palette.muted)
                        .lineLimit(1).padding(.top, 3)
                }
            } else {
                Text("Log a weigh-in").font(.system(size: 13, weight: .semibold)).foregroundStyle(Palette.muted).padding(.top, 6)
            }
        }
    }
}

private struct Sparkline: View {
    var values: [Double]
    var body: some View {
        GeometryReader { g in
            if values.count >= 2, let lo = values.min(), let hi = values.max() {
                let span = max(hi - lo, 0.5)
                let points = values.enumerated().map { i, v in
                    CGPoint(x: g.size.width * CGFloat(i) / CGFloat(values.count - 1),
                            y: 4 + (g.size.height - 8) * CGFloat(1 - (v - lo) / span))
                }
                Path { p in p.addLines(points) }
                    .stroke(Palette.accent, style: StrokeStyle(lineWidth: 2.5, lineCap: .round, lineJoin: .round))
                Circle().fill(Palette.accent).frame(width: 8, height: 8).position(points.last!)
            }
        }
    }
}

private struct StepBars: View {
    var week: [Int]
    var body: some View {
        GeometryReader { g in
            let top = max(week.max() ?? 1, 1)
            HStack(alignment: .bottom, spacing: 5) {
                ForEach(Array(week.enumerated()), id: \.offset) { i, v in
                    RoundedRectangle(cornerRadius: 3, style: .continuous)
                        .fill(i == week.count - 1 ? Palette.accent : Palette.barOff)
                        .frame(height: max(4, g.size.height * CGFloat(v) / CGFloat(top)))
                }
            }
            .frame(maxHeight: .infinity, alignment: .bottom)
        }
    }
}

private func stepsText(_ n: Int) -> String {
    let f = NumberFormatter()
    f.numberStyle = .decimal
    f.locale = Locale(identifier: "en_US")
    return f.string(from: NSNumber(value: n)) ?? "\(n)"
}

// MARK: - Timeline

struct Entry: TimelineEntry {
    var date: Date
    var snapshot: Snapshot?
    var shown: Shown { Shown(snapshot, at: date) }
}

/// Now, then each of the next two midnights: the widget moves on to tomorrow's plan without the app.
private func entries(_ snapshot: Snapshot?) -> Timeline<Entry> {
    let now = Date()
    let cal = Calendar.current
    let first = cal.startOfDay(for: now.addingTimeInterval(86400))
    let second = cal.startOfDay(for: first.addingTimeInterval(86400 + 3600))
    return Timeline(entries: [Entry(date: now, snapshot: snapshot), Entry(date: first, snapshot: snapshot),
                              Entry(date: second, snapshot: snapshot)], policy: .atEnd)
}

struct StaticProvider: TimelineProvider {
    func placeholder(in context: Context) -> Entry { Entry(date: Date(), snapshot: .sample) }
    func getSnapshot(in context: Context, completion: @escaping (Entry) -> Void) {
        completion(Entry(date: Date(), snapshot: context.isPreview ? .sample : (Snapshot.load() ?? .sample)))
    }
    func getTimeline(in context: Context, completion: @escaping (Timeline<Entry>) -> Void) {
        completion(entries(Snapshot.load()))
    }
}

@available(iOS 17.0, *)
struct ConfiguredEntry<Intent: WidgetConfigurationIntent>: TimelineEntry {
    var date: Date
    var snapshot: Snapshot?
    var intent: Intent
    var shown: Shown { Shown(snapshot, at: date) }
}

@available(iOS 17.0, *)
struct IntentProvider<Intent: WidgetConfigurationIntent>: AppIntentTimelineProvider {
    func placeholder(in context: Context) -> ConfiguredEntry<Intent> {
        ConfiguredEntry(date: Date(), snapshot: .sample, intent: Intent())
    }
    func snapshot(for configuration: Intent, in context: Context) async -> ConfiguredEntry<Intent> {
        ConfiguredEntry(date: Date(), snapshot: context.isPreview ? .sample : (Snapshot.load() ?? .sample), intent: configuration)
    }
    func timeline(for configuration: Intent, in context: Context) async -> Timeline<ConfiguredEntry<Intent>> {
        let t = entries(Snapshot.load())
        return Timeline(entries: t.entries.map { ConfiguredEntry(date: $0.date, snapshot: $0.snapshot, intent: configuration) },
                        policy: .atEnd)
    }
}

// MARK: - Configuration

@available(iOS 17.0, *)
enum StatChoice: String, AppEnum {
    case streak, level, plan, body
    static var typeDisplayRepresentation: TypeDisplayRepresentation = "Show"
    static var caseDisplayRepresentations: [StatChoice: DisplayRepresentation] = [
        .streak: DisplayRepresentation(title: "Streak", subtitle: "Days in a row, and whether today is done"),
        .level: DisplayRepresentation(title: "Level & points", subtitle: "Your level and points to the next one"),
        .plan: DisplayRepresentation(title: "Today's plan", subtitle: "The workout your split has for today"),
        .body: DisplayRepresentation(title: "Body", subtitle: "Weight and steps from Apple Health"),
    ]
}

@available(iOS 17.0, *)
struct StatIntent: WidgetConfigurationIntent {
    static var title: LocalizedStringResource = "GainStreak"
    static var description = IntentDescription("Choose what this widget shows.")
    @Parameter(title: "Show", default: .streak) var show: StatChoice
    init() {}
}

@available(iOS 17.0, *)
enum WeightPeriod: String, AppEnum {
    case week, month
    static var typeDisplayRepresentation: TypeDisplayRepresentation = "Weight change over"
    static var caseDisplayRepresentations: [WeightPeriod: DisplayRepresentation] = [.week: "7 days", .month: "30 days"]
}

@available(iOS 17.0, *)
struct BodyIntent: WidgetConfigurationIntent {
    static var title: LocalizedStringResource = "Body"
    static var description = IntentDescription("Weight from your weigh-ins and steps from Apple Health.")
    @Parameter(title: "Weight change over", default: .week) var period: WeightPeriod
    init() {}
}

@available(iOS 17.0, *)
struct OverviewIntent: WidgetConfigurationIntent {
    static var title: LocalizedStringResource = "Overview"
    static var description = IntentDescription("Your streak, level, today's plan and body.")
    @Parameter(title: "Show weight and steps", default: true) var showBody: Bool
    init() {}
}

// MARK: - Widgets

private extension View {
    @ViewBuilder func widgetCard() -> some View {
        if #available(iOS 17.0, *) {
            containerBackground(for: .widget) { Palette.card }
        } else {
            padding().background(Palette.card)
        }
    }
}

/// Small: one widget, four choices (Edit Widget).
@available(iOS 17.0, *)
struct StatWidget: Widget {
    var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: "GainStreakStat", intent: StatIntent.self, provider: IntentProvider<StatIntent>()) { entry in
            StatView(shown: entry.shown, show: entry.intent.show).widgetCard()
        }
        .configurationDisplayName("GainStreak")
        .description("Your streak, level, today's plan or body. Edit the widget to choose.")
        .supportedFamilies([.systemSmall])
    }
}

@available(iOS 17.0, *)
struct StatView: View {
    var shown: Shown
    var show: StatChoice
    var body: some View {
        if shown.kind != .current && !(show == .level && shown.kind == .stale) && !(show == .body && shown.kind == .stale) {
            OpenAppView(shown: shown).widgetURL(link("home"))
        } else {
            switch show {
            case .streak: StreakBlock(shown: shown).widgetURL(link("home"))
            case .level: LevelBlock(level: shown.level).widgetURL(link("points"))
            case .plan: PlanBlock(shown: shown).widgetURL(link("plan"))
            case .body: SmallBody(shown: shown).widgetURL(link("body"))
            }
        }
    }
}

private struct SmallBody: View {
    var shown: Shown
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            WeightBlock(weight: shown.weight, change: shown.weight?.change7, changeLabel: "this week")
            Spacer(minLength: 0)
            if let steps = shown.steps {
                Rectangle().fill(Palette.line).frame(height: 1)
                HStack(alignment: .firstTextBaseline) {
                    kicker("STEPS")
                    Spacer(minLength: 4)
                    big(stepsText(steps.today), size: 18)
                }
                .padding(.top, 9)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

/// Medium: today's plan with its exercises and last time's top sets.
@available(iOS 17.0, *)
struct PlanWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "GainStreakPlan", provider: StaticProvider()) { entry in
            PlanMediumView(shown: entry.shown).widgetCard().widgetURL(link("plan"))
        }
        .configurationDisplayName("Today's plan")
        .description("Today's workout from your split, with last time's top sets.")
        .supportedFamilies([.systemMedium])
    }
}

struct PlanMediumView: View {
    var shown: Shown
    var body: some View {
        if shown.kind != .current {
            OpenAppView(shown: shown)
        } else {
            HStack(spacing: 14) {
                PlanBlock(shown: shown, title: 30).frame(width: 116)
                Rectangle().fill(Palette.line).frame(width: 1)
                if let day = shown.day, day.hasSplit, day.name != nil, !day.exercises.isEmpty {
                    ExerciseList(exercises: day.exercises, limit: 4).frame(maxWidth: .infinity, alignment: .leading)
                } else {
                    VStack(alignment: .leading, spacing: 6) {
                        kicker("TODAY")
                        Text(shown.isRest ? "Rest counts toward your streak." : shown.daySub)
                            .font(.system(size: 13)).foregroundStyle(Palette.soft)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
                }
            }
        }
    }
}

/// Medium: the streak beside the level.
@available(iOS 17.0, *)
struct ProgressWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "GainStreakProgress", provider: StaticProvider()) { entry in
            ProgressMediumView(shown: entry.shown).widgetCard()
        }
        .configurationDisplayName("Progress")
        .description("Your streak and your level.")
        .supportedFamilies([.systemMedium])
    }
}

struct ProgressMediumView: View {
    var shown: Shown
    var body: some View {
        if shown.kind == .signedOut {
            OpenAppView(shown: shown).widgetURL(link("home"))
        } else {
            HStack(spacing: 14) {
                Link(destination: link("home")) {
                    if shown.kind == .current { StreakBlock(shown: shown, numberSize: 48) } else { OpenAppView(shown: shown) }
                }
                Rectangle().fill(Palette.line).frame(width: 1)
                Link(destination: link("points")) { LevelBlock(level: shown.level) }
            }
        }
    }
}

/// Medium: the weight trend beside the week's steps.
@available(iOS 17.0, *)
struct BodyWidget: Widget {
    var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: "GainStreakBody", intent: BodyIntent.self, provider: IntentProvider<BodyIntent>()) { entry in
            BodyMediumView(shown: entry.shown, period: entry.intent.period).widgetCard().widgetURL(link("body"))
        }
        .configurationDisplayName("Body")
        .description("Your weight trend and this week's steps from Apple Health.")
        .supportedFamilies([.systemMedium])
    }
}

@available(iOS 17.0, *)
struct BodyMediumView: View {
    var shown: Shown
    var period: WeightPeriod
    var body: some View {
        if shown.kind == .signedOut {
            OpenAppView(shown: shown)
        } else {
            HStack(spacing: 14) {
                VStack(alignment: .leading, spacing: 0) {
                    WeightBlock(weight: shown.weight, change: period == .week ? shown.weight?.change7 : shown.weight?.change30,
                                changeLabel: period == .week ? "in 7 days" : "in 30 days")
                    Spacer(minLength: 6)
                    if let w = shown.weight, w.trend30.count >= 2 { Sparkline(values: w.trend30).frame(height: 44) }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                Rectangle().fill(Palette.line).frame(width: 1)
                VStack(alignment: .leading, spacing: 0) {
                    kicker("STEPS")
                    if let steps = shown.steps {
                        HStack(alignment: .firstTextBaseline, spacing: 4) {
                            big(stepsText(steps.today), size: 28)
                            Text("today").font(.system(size: 13, weight: .semibold)).foregroundStyle(Palette.muted)
                        }
                        .padding(.top, 4)
                        Spacer(minLength: 6)
                        StepBars(week: steps.week).frame(height: 44)
                    } else {
                        Text(shown.kind == .stale ? "Open GainStreak to update" : "Turn on Apple Health in Profile")
                            .font(.system(size: 13, weight: .semibold)).foregroundStyle(Palette.muted).padding(.top, 6)
                        Spacer(minLength: 0)
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            }
        }
    }
}

/// Large: everything, each row opening its own screen.
@available(iOS 17.0, *)
struct OverviewWidget: Widget {
    var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: "GainStreakOverview", intent: OverviewIntent.self,
                               provider: IntentProvider<OverviewIntent>()) { entry in
            OverviewView(shown: entry.shown, showBody: entry.intent.showBody).widgetCard()
        }
        .configurationDisplayName("Overview")
        .description("Your streak, level, today's plan, weight and steps.")
        .supportedFamilies([.systemLarge])
    }
}

@available(iOS 17.0, *)
struct OverviewView: View {
    var shown: Shown
    var showBody: Bool
    var body: some View {
        if shown.kind != .current {
            OpenAppView(shown: shown).widgetURL(link("home"))
        } else {
            VStack(alignment: .leading, spacing: 12) {
                Link(destination: link("home")) {
                    HStack(spacing: 8) {
                        Flame(color: shown.flameColor, size: 22)
                        big("\(shown.streak)", size: 30, color: shown.streakColor)
                        Text("day streak").font(.system(size: 13, weight: .semibold)).foregroundStyle(Palette.muted)
                        Spacer(minLength: 8)
                        if let level = shown.level {
                            HStack(spacing: 6) {
                                Text("LVL \(level.number)").foregroundStyle(Palette.accentText)
                                Text("\(level.points) pts").foregroundStyle(Palette.ink)
                            }
                            .font(.system(size: 12, weight: .bold))
                            .padding(.horizontal, 10).padding(.vertical, 5)
                            .background(Capsule().fill(Palette.chip))
                        }
                    }
                }
                Rectangle().fill(Palette.line).frame(height: 1)
                Link(destination: link("plan")) {
                    VStack(alignment: .leading, spacing: 10) {
                        HStack(alignment: .firstTextBaseline, spacing: 8) {
                            kicker("TODAY", color: Palette.accentText)
                            big(shown.dayTitle, size: 22)
                            Spacer(minLength: 8)
                            Text(shown.daySub).font(.system(size: 12)).foregroundStyle(Palette.muted).lineLimit(1)
                        }
                        if let day = shown.day, day.name != nil {
                            ExerciseList(exercises: day.exercises, limit: showBody ? 4 : 5, inline: true)
                        } else {
                            Text(shown.isRest ? "Rest counts toward your streak." : shown.daySub)
                                .font(.system(size: 13)).foregroundStyle(Palette.soft)
                        }
                        Spacer(minLength: 0)
                        Text(shown.statusText).font(.system(size: 12, weight: .semibold)).foregroundStyle(shown.statusColor)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                }
                if showBody {
                    Rectangle().fill(Palette.line).frame(height: 1)
                    Link(destination: link("body")) {
                        HStack(alignment: .top, spacing: 12) {
                            VStack(alignment: .leading, spacing: 2) {
                                kicker("WEIGHT")
                                Text(shown.weight.map { "\($0.current) \($0.unit)" } ?? "—")
                                    .font(.system(size: 20, weight: .heavy)).foregroundStyle(Palette.ink)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                            VStack(alignment: .leading, spacing: 2) {
                                kicker("STEPS")
                                Text(shown.steps.map { stepsText($0.today) } ?? "—")
                                    .font(.system(size: 20, weight: .heavy)).foregroundStyle(Palette.ink)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                        }
                    }
                }
            }
        }
    }
}

// MARK: - Control Center (iOS 18)

@available(iOS 18.0, *)
enum OpenTarget: String, AppEnum {
    case start, plan, body
    static var typeDisplayRepresentation: TypeDisplayRepresentation = "Screen"
    static var caseDisplayRepresentations: [OpenTarget: DisplayRepresentation] = [
        .start: "Start workout", .plan: "Today's plan", .body: "Log weight",
    ]
}

/// Opens the app on a screen: controls can't open the app with a link directly, so they run this.
@available(iOS 18.0, *)
struct OpenGainStreakIntent: AppIntent {
    static var title: LocalizedStringResource = "Open GainStreak"
    static var isDiscoverable = false
    @Parameter(title: "Screen") var target: OpenTarget

    init() {}
    init(_ target: OpenTarget) { self.target = target }

    func perform() async throws -> some IntentResult & OpensIntent {
        .result(opensIntent: OpenURLIntent(link(target == .plan ? "home" : target.rawValue)))
    }
}

@available(iOS 18.0, *)
struct StartWorkoutControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "GainStreakStartWorkout") {
            ControlWidgetButton(action: OpenGainStreakIntent(.start)) { Label("Start workout", systemImage: "dumbbell.fill") }
        }
        .displayName("Start workout")
        .description("Opens GainStreak and starts today's workout.")
    }
}

@available(iOS 18.0, *)
struct TodayPlanControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "GainStreakTodayPlan") {
            ControlWidgetButton(action: OpenGainStreakIntent(.plan)) { Label("Today's plan", systemImage: "calendar") }
        }
        .displayName("Today's plan")
        .description("Opens GainStreak on Home.")
    }
}

@available(iOS 18.0, *)
struct LogWeightControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "GainStreakLogWeight") {
            ControlWidgetButton(action: OpenGainStreakIntent(.body)) { Label("Log weight", systemImage: "scalemass.fill") }
        }
        .displayName("Log weight")
        .description("Opens GainStreak's body data.")
    }
}
