# Changelog

Versions follow the rules in the README: before 1.0, **0.&lt;phase&gt;.&lt;fix&gt;**, where the middle number is the implementation plan's phase and the last counts fixes within it. 1.0.0 will be the first public release.

## 0.27.1

- **Invite links from Instagram (and other apps' browsers) open the app:** Instagram, Facebook, TikTok and similar apps open links in their own browser, which never hands an invite link to GainStreak, so your friend always ended up in TestFlight, even with the app installed. A phone that opens an invite link in a browser now sees a page with **Open in GainStreak**, which opens the app at the invite, and a button to get the app. On Android, **Open in GainStreak** goes to Google Play when the app isn't installed. If nothing happens, the page says to open the link in Safari or Chrome. Your friend needs this version of the app for **Open in GainStreak** to go straight to the invite.
- Links opened from Messages, WhatsApp, Mail or the camera keep opening the app directly, as before.
- **Text that ran off the screen on phones:** the "… is now your split" confirmation and the Map's "Today logged at …" note now wrap (the note's **Undo** and close buttons stay on screen). So do a long gym name in the cardio card and in the live workout's "At … ✓" pill, and the gym form's "OpenStreetMap only knows the street here." and its address.
- **Adding a custom gym on a phone:** the gym list drew over the **Add a gym** form, the form ran under the **Cancel** button, and tapping **Name** often did nothing. While the form is open it now takes the list's place and scrolls on its own; the list comes back when you save or cancel.
- **Changing today's gym on a narrow phone:** in **Where did you train today?**, **Cancel** ran off the right edge. **No gym today** now has its own row above **Add custom gym** and **Cancel**.
- **Custom gym names** can be up to 40 characters (was 60), so they fit on the map, the cardio card and the live workout. Gyms you already named keep their names.
- **Fewer scroll bars (phones):** pages scroll without a bar; only lists (gyms, exercises, history) keep theirs.
- **Home after a workout (phones):** once today's workout is finished, **Today's workout** moves down under **Recent PRs**, just above **Friends**, folded; **Show** opens its exercises.
- **Live workout (phones):**
  - **Skip for now** goes to the next exercise and leaves the skipped one where it is in the list (marked Skipped); it still comes back once the others are done.
  - **Finish** is no longer beside **Next**: a **Finish workout** link sits at the bottom of the card, where **End workout** is before half the exercises are done (only one of them shows at a time).
  - With every exercise done, **Finish workout** finishes straight away, without asking again.
  - Logged sets read "1: 60×8" in slimmer chips (three fit on a row), and a dumbbell exercise says **Per dumbbell** under the weight.
- **After a workout:** "Want to log your gym?" and "Show this check-in to friends?" wait until you've answered **Continue with a cardio session?**, instead of covering it.
  Home no longer flashes for a moment before the cardio screen.
- **Adding exercises to a day:** an exercise picked from **Add exercises** shows **Add exercise** instead of **Save sets**, and pressing it goes back to the Add exercises list for the next one.
- **Exercise page on small phones:** the **Warm-up sets** and **Working sets** pills and the **reps** boxes no longer cut off their words.
- **Live Activity (iPhone):** an orange flame before the day's name, and a dumbbell in the progress ring in the Dynamic Island (also when it shares the island with music or a timer). The island's bottom row shows the exercise and the percent.
- **Live Activity count fixed:** right after **Start workout**, the Lock Screen could keep showing the starting numbers while you logged your first sets. It now updates from the first set. (Warm-ups still don't count toward the sets; only working sets do.)
- **Weigh-in Habit counts weigh-ins you log in GainStreak:** weigh-ins that come over from Apple Health still give Bodyweight Club your body weight, but they no longer build the Weigh-in Habit badge. Log your weight in the app to keep the run going.
- **Body data:** with Apple Health not connected yet, a **Sync with Apple Health** row at the top connects it. Once every body data field is filled and you have no calorie goal yet, saving shows **Set calorie goal**, which opens Calories with the goal sheet open (a weigh-in isn't needed). In the calorie goal's custom macros, the **g** now sits outside the number boxes.
- **Apple Health shares what you allow:** if you share only some things (say height but not weigh-ins), those still fill in; one type GainStreak can't read no longer stops the others.
- **Dark by default (phones):** the app, the sign-in screens included, now starts dark. Accounts still set to **System** move to **Dark**; switch back under **Profile → Theme** (System follows your phone).
- **Migration:** the app's database (data only: `Users.ThemePreference` from System to Dark).

## 0.27.0

- **A calmer Calories screen (phones):** the first card is now **Calories**, with a bigger ring that fills in lime as you eat and turns red once you're over your target range. Beside it, **Goal** and **Eaten** in plain numbers and your target range. **Last 7 days** fills the rest of the screen; **Calorie goal** and **Maintenance** sit just below, a short scroll away.
- **Macro bars:** under the ring, protein, carbs and fat each have a bar with "eaten / target" ("90 / 144 g"). A macro past its target turns red. Without targets, a bar shows that macro's share of the day's calories.
- **Manage food:** the **Add food** button is now **Manage food**. With something logged, the sheet opens at today's list, where you can change or remove an item.
- **Past days:** tap a past day's bar in **Last 7 days** for a card with that day's ring split into protein, carbs and fat, the grams and percentages, how it went against that day's goal, and **See what you ate**.
- **Macros for what you type:** a **New food** and a **Quick add** now ask for protein, carbs and fat (0 is fine) and work out the calories for you ("= 330 kcal"). Foods from the catalogue and Open Food Facts keep their label values; one with calories but no macros asks for them first.
- **Calorie goal, macros first:** pick **Lose**, **Maintain** or **Gain**, then **Recommended** or **Custom**.
  - **Recommended** sets protein, carbs and fat in grams per kilo of body weight, different for men and women, a little more carbs the more active you are, and your goal is what they add up to. It never goes under the safe minimum. It follows your weight as a 2-week average and only moves when that changes by a kilo, so one heavy morning doesn't change your targets; when it does, the Calories screen tells you once.
  - **Custom:** type your own grams; the calorie goal is worked out from them. **Start from recommended** fills them in to adjust.
  - A goal you set before this update stays as it is until you pick one of the two.
- **Maintenance:** the row shows your maintenance calories and opens **Profile, Body data**.
- **Home (phones):** the calories card matches the Calories screen: the same ring, Goal and Eaten, macro bars and **Manage food**.
- **Profile (phones):** a new **Body** card with your weight chart, **Log**, **History** and **Body data**. **Vacation** has its own row. **Body data** now shows only your measurements.
  - **The weight chart** works like Apple Health's: **W / M / 6M**, touch and hold then slide to read any day under your thumb, swipe sideways for earlier weeks, or tap a day.
  - **History** lists every weigh-in by month, with the change since the one before, and lets you delete one.
- **Gyms load much faster** and no longer fail when the public map server is busy: in Romania the app now gets a city's gyms from the GainStreak server, in well under a second. Elsewhere, or if our server can't answer, it asks the public map server as before. Gyms are the same OpenStreetMap data, refreshed weekly.
- The desktop app doesn't change.
- **Migration:** the app's database (`MacroGoals`, `MacroPlan`: macro targets and the goal's mode) and the server's Postgres (`OsmGyms`: the gyms table). Run `migrate`, then `import-gyms --country romania` once, before deploying the server, and schedule `import-gyms` weekly. 0.21.0 and later keep working.

## 0.26.1

- **Home (phones):** the date and greeting above the level card are gone, so the level card and the whole **Gym streak** card now fit on one screen without scrolling. The streak line ("Do Push today to keep your streak.") moved into the streak card, with a lime strip when a workout is still owed today and ice blue on a rest day. The level card is smaller, with **History** on the title line, and the streak number, **Best streak** and **Gyms this month** share one row. The gauge is bigger.
- **Settings button (Profile):** the sliders icon in the corner is now a **Settings** button with a gear and the word, so it's easy to find.
- **Body data:** the **Weight** card now shows on the Body data page too. Before, the Home setup card and Calories sent you there to add your weight, but the page had no weight field, and the step stayed open until you logged it from Profile.
- **Your QR code:** the QR sheet is smaller, so it fits without a scroll bar.
- **Finding gyms in a city:** the loading line and the error message wrap on narrow screens instead of running off the edge. A server that doesn't answer within 20 seconds now makes the app try the other one, instead of giving up.
- The desktop app doesn't change.
- **Migration:** none. The server doesn't change.

## 0.26.0

- **Food log (phones):** Calories now keeps what you ate, not just one number. **Add food** (on the ring card, or **+ Add** next to a meal) opens a sheet with **Breakfast / Lunch / Dinner / Snacks**.
  - **Search:** type a food and your own foods and recent ones come first, then generic foods from a USDA catalogue that is built into the app and works offline, then packaged products from Open Food Facts when you're online. Names follow your phone's language where Open Food Facts has them.
  - **Scan a barcode:** the barcode button opens the camera; point it at a packaged food and it opens at its amount. No camera access, or no camera? **Enter a barcode number** types it instead. On Android the scanner is Google's and downloads once on first use ("The scanner is getting ready" until then).
  - **Barcode not found:** **Add it as my food** saves it under that barcode, so the next scan finds it, or **Search by name**.
  - **The amount:** grams or millilitres, or a serving where the food has one; calories, protein, carbs and fat update as you type, then **Log**. Star a food to keep it near the top.
  - **Your own foods:** **New food** (per 100 g or per serving, with an optional serving) and **Edit this food**. Editing a catalogue or Open Food Facts food makes your own copy. Numbers that look wrong (more than 100 g of macros per 100 g, no energy) open the form to fix first.
  - **Quick add:** type just a number of calories, with an optional label, as before; it shows as "Quick add".
  - **The day adds itself up:** the Calories screen lists each meal with its foods; tap one to change the amount or delete it. The ring, week bars, Home's card, streak and the +10 points for hitting your target all read the day's total, so nothing about points changes. Days you logged before as one number show as "Logged as a total".
  - **Past days** show their meals read-only; you log today only. Changing a food later never changes what you logged.
  - **Privacy:** what you eat stays on your phone. A barcode or search text goes to Open Food Facts with no account or identifier. The camera only reads the number; nothing is recorded. Profile's About credits USDA FoodData Central and Open Food Facts.
- The desktop app doesn't change.
- **Migration:** the app's database (`FoodLog`: foods and food entries). The server doesn't change, and 0.21.0 and later keep working.

## 0.25.0

- **Badges (phones):** GainStreak now awards badges for what you already log. Profile has a new **Badges** card; **See all** opens the badges screen.
  - **21 badges to work towards** in four groups: Consistency (Streak, Perfect Week, On Ice, Weekend Warrior, Split Finisher), Strength (PR Breaker, Heavy Lifter, Century, Bodyweight Club, Rep Machine, Exercise Explorer, PR Week), Body & health (Fuel Streak, Weigh-in Habit, Cardio Minutes) and Social & gyms (Regular, Explorer, We Go Jim, Recruiter, Top of the Board, and a Monthly Challenge from December).
  - **Levels:** most badges climb from **Steel** to **Lime**, **Fire** and **Blaze**. One-time badges are lime. Heavy Lifter shows how much you've lifted in all: a car, an elephant, a blue whale, a space station.
  - **18 hidden badges** show as a dashed **?** with a hint until you earn one. No spoilers here.
  - **The badges screen** shows how many you have, the closest one to earn next and an **All / Earned / Locked** switch. Tap a badge for its rule, its levels and your progress ("61 t / 150 t", in lb if you use pounds).
  - **Earned is earned:** a badge or level is never taken away, even if you later edit or delete the log that earned it.
  - **Your history counts:** the first start after updating awards what your past logs already earned, quietly, with one sheet: "You earned 9 badges from your history".
  - **New badge sheet:** earning a badge or a new level shows a sheet with its level, **Share with friends** and **Done**. Several at once: swipe through them.
  - **Badges don't add points,** so levels and the leaderboard work as before.
- **Featured badge:** one badge shows on your avatar's corner, after your name on the friends' leaderboard and next to your name in a gym's card on the map (tap the pin; pins themselves stay plain). You can feature a secret badge too: friends who haven't found it see a **?** instead, and the picker tells you so. Until you pick one it's your highest level, newest first. **Change** on the Badges card opens a picker with a preview, or **Show no badge**.
- **Friends' badges:** tap a friend on the leaderboard or in the friends list, or **Badges** in a gym's card on the map, to see their badges: their featured badge, how many they have, how many you both have, and **All / Both have / Only <name>**. Under each badge you see how you compare: your level ("You: Fire"), or **You:** with a green check or a red cross. A hidden badge you haven't earned stays a **?**. Offline, the last copy shows with a note.
- **Friends can see my badges** (on the Badges card, on by default): off hides your badges and your featured badge from friends everywhere.
- The desktop app doesn't change.
- **Migration:** the app's database (badges, featured badge, sharing) and the server's Postgres (`UserBadges`, invite records, featured badge and sharing on users). The server keeps the last 21 days of uploaded stats for last week's leaderboard. Run `migrate` before deploying the server. 0.21.0 and later keep working.

## 0.24.1

- **A clearer map:** the Map's streets are drawn at full strength instead of dimmed, so the grey haze over the map is gone, and zooming in and out no longer leaves darker and lighter patches while new tiles load.
- **The map fills the Friends tab:** on the phones it now runs right up to the top bar, with rounder corners, and the **Leaderboard / Map** switch floats on it instead of sitting on a black strip above it.
- **One pill on the map:** on the phones the map keeps just **Today / 7 days / 30 days**, a little slimmer. **Latest / All visits** moved to the map settings (the sliders button) as **Every gym visited**: off shows each friend at their latest gym, on shows a pin at every gym they went to in that time.
- **More room for the map:** the map's bottom sheet no longer has the **Today · Check in** row on the phones; check in from Home. The sheet starts with **Your gym**, so more of the map shows.
- **No grey square on the top bar:** touching the bell (Inbox) or your avatar (Profile) no longer flashes a grey square behind it.
- **No migration.** The server doesn't change.

## 0.24.0

- **Home Screen widgets on the iPhone (iOS 17 and later):** long-press the Home Screen, tap **Edit → Add Widget** and search for GainStreak.
  - **GainStreak (small):** one widget, four choices. Long-press it, **Edit Widget**, and pick **Streak**, **Level & points**, **Today's plan** or **Body**. Each copy keeps its own choice, so two can sit side by side.
  - **Today's plan (medium):** today's day of your split and its exercises, with last time's top set, as on Home.
  - **Progress (medium):** your streak next to your level and points.
  - **Body (medium):** your weight with its trend, and this week's steps from Apple Health. **Edit Widget** picks whether the weight change is over 7 or 30 days.
  - **Overview (large):** streak, level, today's plan, weight and steps. **Edit Widget** can hide the weight and steps row.
  - **Tap a widget** and the app opens where it points: the streak on **Home**, the level on the **points history**, the plan on **today's workout day** (Splits on a rest day), weight and steps on **Profile → Body data**.
  - **They follow the app:** the streak turns orange once today is logged, and says when a day is pending, a rest day, missed or on vacation. They update when you log something, change screens or leave the app, and move on to tomorrow's plan at midnight. If the app hasn't been opened since, they ask you to open it rather than guess.
  - **Light and dark** follow the phone. Logged out, they show no one's data.
- **Control Center controls (iOS 18 and later):** **Start workout** opens Home and starts today's workout (on a rest day, the exercise picker), **Today's plan** opens Home and **Log weight** opens Body data. Add them from Control Center's **+**.
- **Steps on the widgets** need Apple Health on in Profile; without it the Body widget says so. Nothing from the widgets leaves your phone.
- Android and the desktop app don't change.
- **No migration.** The server doesn't change.

## 0.23.1

- **Profile is tidier on the phones:** it now shows just you: your streak, level and points, your weight, and two rows for **Body data** and **Vacation**. The body data row sums it up ("Male · Age 28 · 180 cm · Active") with your maintenance estimate under it; tap it to edit. Unsaved edits wait there and the row says so.
- **Settings:** the new button at the top right of your profile card opens a list of **Email**, **Password**, **Notifications**, **Apple Health** (Health Connect on Android), **Home city** and **Appearance**, each showing what it's set to. Tap one for its page; the arrow at the top (or Android's back) takes you back. **Send feedback**, **Log out** and **Delete account** are at the bottom of the list.
- **Edit body data** in Calories and **Fill in your body data** on Home open the Body data page straight away.
- **Apple Health asking something?** Its row says **Needs an answer** until you pick, so the question isn't missed now that it's a page away.
- **New switches** that spring across with a check when on, in Profile's settings and the Map's settings (Visible to friends, Ask for gym). The training day reminder's time is set on two wheels right in the card, no popup.
- **Avatar letters:** an avatar without a picture shows one letter for a one-word username ("cristi" → C) and two for two words split by `_`, `.` or `-` ("andrei.m" → AM), everywhere: the app bar, Profile, friends, the leaderboard, the Inbox and the map's pins.
- **New usernames need a letter:** one made only of digits (and `_`, `.`, `-`) is refused with "Your username needs at least one letter." Existing accounts keep theirs. Digits before a letter don't count for the avatar: "1cristi" shows C.
- The desktop app doesn't change.
- **No migration.** The server refuses digits-only usernames too (it shares the rule), for every app version.

## 0.23.0

- **Health Connect (Android):** turn it on in **Profile → Health Connect**, and choose in Android's sheet what GainStreak may use. It's off until you do. It works like Apple Health on the iPhone.
  - **Saved to Health Connect:** your finished workouts (with the day's name), your cardio sessions and the weigh-ins you log. Fix a weigh-in or undo a workout and Health Connect follows; the last 30 days are kept in step.
  - **Read from Health Connect:** weigh-ins from a smart scale or another app fill the days you haven't logged; your height fills an empty field, and where Health Connect says something different, the card asks **Use Health Connect's** or **Keep mine**; your steps of the last 30 days suggest an activity level (**Use it** / **Not now**). Health Connect has no birth date or gender, so those stay yours to fill in.
  - **Your typed values win,** nothing from Health Connect earns points or changes your streak, and it never leaves your phone.
  - **Turned something off in the sheet?** The card says what isn't saved, and **Open Settings** takes you to Health Connect, where **App permissions** has GainStreak.
  - **Android 13 or older:** Health Connect is a separate app there. Turning it on opens it on Google Play; install it and turn it on again.
  - **Turning it off** stops reading and saving; what's already in Health Connect stays there.
- **The privacy policy** has a Health Connect section.
- **No migration:** it uses the same settings as Apple Health. The server doesn't change.

## 0.22.0

- **Friend notifications on your phone:** GainStreak tells you when someone sends you a friend request, accepts yours, or adds you with your QR code, even when the app is closed.
  - **Tap it** and a request opens the **Inbox** on **Requests**, ready to accept; an accepted request or a new friend opens **Friends**.
  - **Turn them on** in **Profile → Notifications → Friend requests**, or with **Turn on** on the card in Friends, which shows once you have friends or requests. If you already allowed notifications for Reminders, they're on from the start.
  - **While the app is open,** a notification shows at the top, unless you're already on the Inbox or Friends: then the list just updates.
  - **Only on this phone,** and only for the account signed in on it. Logging out stops them at once, and another account signed in on the same phone never gets yours.
  - **What's sent:** Apple or Google deliver each notification with your friend's username and what happened, nothing else. Declined, cancelled or removed requests and blocks never notify anyone.
  - **No more than one** "wants to be your friend" from the same person in 12 hours.
  - On the iPhone and on Android; the desktop app doesn't change.
- **Profile:** Reminders moved into the new **Notifications** card, next to Friend requests.
- **The privacy policy** has a row for the push token, and says what Apple and Google see.
- **Migration:** server Postgres (`PushDevices`: one push token per signed-in session, deleted with it) and app SQLite (`AddFriendNotifications`: the Friend requests switch and the card's Not now).
## 0.21.2

- **Uploads that could go missing:** when the server was busy (its rate limit), slow, or your session had ended, the app treated the answer as a final "no" and dropped the upload. Your points, check-ins, picture or the sharing setting then stayed out of date for friends until you changed them again. They now wait and go on the next sync, as they do offline. This matters most for **turning sharing off**: that now always reaches the server.
- **Turning sharing on:** if the check-ins got to the server before the setting, they waited up to 15 minutes. They now go right after the setting, in the same sync.
- **Removed or blocked friends offline:** someone you removed or blocked could still show in the friends list, the leaderboard or on the map if you opened those with no connection afterwards. They're now gone from all three at once. The same goes for a request you accepted, declined or cancelled.
- **Faster with a long history:** Home, the check-in popup, the live workout card and the rest-day exercise picker read only the sets they show (today's and each exercise's last session) instead of every set you ever logged. With three years of workouts they load two to three times faster.
- **Server, account safety:** changing your email or password and deleting your account now allow 10 tries a minute at the current password; after that the app says "Too many tries". Email codes sent at the same moment are each counted as a try. Neither needs an app update.
- **Phone polish:**
  - **Get set up** on Home is one checklist: the next step is open with its button, the others are rows you tap (a lock marks a step that waits for the one before). Home no longer repeats "Pick a split" in the streak card and Today's workout.
  - **Split cards** say their days a week once, are about half as tall, and the whole card opens the split. A custom split keeps **Edit** and **Delete** under it.
  - **This week** on a new account no longer shows red crosses for days before you picked a split or logged a workout.
  - **Buttons dim when pressed**, so a tap is acknowledged at once.
  - **Switches** in Profile are lime when on, not the system's blue.
  - **Smaller fixes:** the scroll indicator no longer covers the right edge of cards, Home's level card is as wide as the others, the birth date boxes line up, and Calories says "No days logged yet" where it said "0 of 0 logged days on target".

## 0.21.1

- **Signing up on a busy network:** a mistake when signing up, such as a username that's taken, no longer uses up a try. Many phones on one mobile network can now sign up within the same hour, where five tries used to block everyone sharing that network's address for an hour.
- Only the server changes, so no app update is needed.

## 0.21.0

- **Add friends with a QR code:** in **Friends → Add friends**, tap **Show my QR code**. A friend scans it with their phone's camera, and GainStreak opens on their Friends tab with **"yourname wants to be your friend"**. They tap **Accept** and you're friends at once, with no request to wait on.
  - **No app yet?** The link takes them to the App Store or Google Play first. Once they have the app, they scan again.
  - **Copy link or Share** to send it in a message instead. The link works the same as the code. On a computer, the link shows the QR code to scan with a phone.
  - **Reset link** makes a new code and link; the old ones stop working. Use it if a link got somewhere it shouldn't.
  - **Offline:** the sheet shows your saved link. Opening someone's link offline says so, with **Try again**.
  - **Opened before you've signed in?** Sign in first, and Friends opens with the invite.
  - **The usual rules hold:** 50 friends at most, and a code from someone you blocked, or who blocked you, doesn't work. Your username isn't in the link.
- **The privacy policy** has a row for the invite code.
- **Migration:** `FriendInvites` on the server (a new `FriendInvites` table: one code per account, deleted with it) and in the app's SQLite (the same table, for the TestBench's local mode).

## 0.20.0

- **Your workout in the Dynamic Island (iPhone):** leave the app during a workout and the Dynamic Island shows how much of it you've done, as a ring and a percent. Touch and hold it for the day, the next exercise, your sets so far and a running timer. iPhones without a Dynamic Island show the same on the Lock Screen.
  - **The percent counts working sets:** 7 of 15 planned sets is 47%, so it moves with every set. Warm-ups and extra sets don't count.
  - **It goes away** when you finish or end the workout, or when it stops by itself after 4 hours or at midnight.
  - Needs iOS 16.2 or later, with Live Activities allowed for GainStreak in Settings.
- **Apple Health (iPhone):** turn it on in **Profile → Apple Health**, and choose in iOS's sheet what GainStreak may use. It's off until you do.
  - **Saved to Health:** your finished workouts (with the day's name), your cardio sessions and the weigh-ins you log. Fix a weigh-in or undo a workout and Health follows; the last 30 days are kept in step.
  - **Read from Health:** weigh-ins from a smart scale or another app fill the days you haven't logged; height, birth date and gender fill empty body data, and where Health says something different, the card asks **Use Health's** or **Keep mine**; your steps of the last 30 days suggest an activity level (**Use it** / **Not now**).
  - **Your typed values win:** nothing you entered is overwritten without asking, and a weigh-in you type replaces the one from Health that day.
  - **Nothing from Health earns points** or changes your streak, and it never leaves your phone.
  - **Turning it off** stops reading and saving; what's already in Health stays there.
- **Migration:** `AppleHealth` in the app's SQLite (`UserProfiles` gets the Apple Health setting, when it was turned on, and what was last suggested or kept from Health; `WeightLogs` gets whether a weigh-in came from Health). The server doesn't change.

## 0.19.0

- **Vacation mode:** going away? Put your streak on vacation and it stays frozen until you're back, instead of breaking.
  - **Up to 7 days, once every 2 months:** pick the day you're back, at most 7 days away. The 2 months count from when a vacation starts, and ending one early still uses it up.
  - **Frozen, not growing:** on vacation days the streak neither grows nor breaks, and no day is missed. Home shows it on ice: **ON VACATION · FROZEN · Back on …**, with a moon on those days in This week.
  - **Training on vacation:** a workout verified at a gym still adds a day. One that isn't verified, including any workout logged on the desktop, is logged and earns its points but doesn't add a day.
  - **Today already logged?** Then the vacation starts tomorrow, so today's workout keeps counting.
  - **A missed day comes first:** if yesterday's missed day still needs a choice, or a day is owed, settle that before the vacation can start.
  - **Where:** Profile has a new **Vacation** card on every device: when you're back, or when the next one is available, and **Plan a vacation**.
  - **I'm back:** end it early from Home or Profile. Today stays frozen and your streak counts again from tomorrow. A vacation that hasn't started yet is cancelled instead, and doesn't use anything up.
  - **Welcome back!** on the first normal day, on Home and as a 9:00 reminder (with Reminders on). No training-day or missed-day reminders while you're away.
  - **The week bonus** is judged on your other training days; a week that's all vacation earns none.
- **"Away from Timișoara? On vacation?" (phones):** when you open the app far from your home city (more than 40 km outside it), Home asks. **Yes, freeze my streak** picks the day you're back; **Not on vacation** isn't asked again on this trip.
  - **When:** at most every 6 hours, only if location is already allowed and precise; the app never asks for location for this. There's no check without a home city, without a streak to keep, on vacation, or within 2 months of the last one.
  - **Your location stays on your phone:** it's compared there with your home city and dropped.
- **Gyms near you, on vacation (phones):** **Show me gyms near me** on Home finds the city you're in and shows its gyms on the map, with **Back to Timișoara** to see home. A workout at one of them is verified and adds a day.
  - **What's sent:** only your position rounded to about 1 km, to OpenStreetMap, to name the city, and only when you tap. If that doesn't work, or you'd rather not, **Search for the city instead**.
  - **The city stays** on the map until the vacation ends. Your home city, your gym and your friends' map don't change.
- **The location prompt** now also mentions noticing trips and gyms near you. The privacy policy says the same.
- **Migration:** `Vacations` in the app's SQLite (a new `Vacations` table: the dates, the city being visited and whether Welcome back was shown; and two `UserProfiles` columns, the time of the last look for a trip and "Not on vacation"). The server doesn't change.

## 0.18.2

- **Time your cardio after a workout (phones):** when you finish a workout, GainStreak asks **Continue with a cardio session?** Tap **Continue with cardio** and a timer starts on Home, under the streak card. **Skip** if you're done for the day.
  - **The ring** fills toward your next point and the time counts up in the middle. Cardio earns the same as before: +1 point for every 10 minutes, up to +3 at 30 minutes.
  - **Pause and Resume** any time. **Finish cardio** saves the whole minutes onto today's workout and adds the points. Under a minute saves nothing.
  - **Stay at the gym:** when your workout was verified at a gym, the timer checks once a minute that you're still there. Leave, and it pauses and offers to save what you did; come back and it goes on by itself.
  - **Locked phone:** your phone can't be checked while it's locked. When you open the app again at the gym, the whole time counts. Opened somewhere else, the timer pauses at the last check that found you at the gym.
  - **Not verified:** after a workout that wasn't verified at a gym (location off, or not at one of your gyms), the timer runs without checks and says so.
  - **Stops by itself** after 4 hours or at midnight, like a workout, and saves what it counted.
  - **Finish** no longer asks you to type cardio minutes. The desktop's "I went today" keeps its cardio box.
  - **Undo** for today's workout is hidden while the timer runs, since the minutes go onto it.
- **Migration:** `CardioTimer` in the app's SQLite (a new `CardioSessions` table: the timer's times and the gym it checks, never a position). The server doesn't change.

## 0.18.1

- **Rest days keep your streak, they don't grow it:** a rest day no longer adds a day. Your streak is kept but frozen until your next workout.
  - **On ice:** on a rest day the streak turns cold blue instead of fire: the flame and number on Home, the arc, the app bar and Profile. It's back on fire with your next workout.
  - **Training on a rest day:** a workout verified at one of your gyms (phones) adds a day as before. A workout that isn't verified, including every workout logged on the desktop, doesn't add one, and on its own doesn't start a new streak.
  - **Making up a missed day:** a rest day still pays for a missed training day, so the streak is kept, but it only adds a day when the workout is verified.
  - **Nothing taken away:** rest days before you update keep their +1, so your streak doesn't drop. The new rule starts the first day you open 0.18.1. Best streaks are unchanged.
  - **Friends** see your streak under the same rule. Points don't change.
- **Pick your own exercises on a rest day (phones):** **Start workout anyway** now opens a list of every exercise instead of asking which day of your split you're doing. Pick as many as you like, up to 12, and start.
  - **Finding them:** search by name, or use the chips: **Recent** (what you did lately, with your top set last time), **All**, or a muscle group.
  - **Your picks** show in a tray at the bottom in the order you picked them, and the workout follows that order.
  - **Sets:** an exercise that's in one of your days keeps its planned sets; any other gets 3 working sets.
  - **Same rules as any workout:** it counts once half the exercises are done, and full points need 70% and the gym. Verified at your gym, it adds a day to your streak.
  - **No split day needed:** you don't have to build a workout first to train on a rest day. The desktop keeps the "I went today" check-in.
- **Live workout card on phones:** during a workout, the card under the streak showed a line of code instead of the exercise and its sets. It shows the card again.
- **First workout on iPhone:** after answering the location question at your first **Start workout**, the workout didn't open until you restarted the app. It opens straight away now.
- **Migration:** `StreakRestRule` and `PickedRestDayExercises` in the app's SQLite (a new `Users` column, the day the rest-day rule started for you; new `ExerciseIds` columns on `LiveWorkouts` and `WorkoutLogs`, and a live workout's split day can now be empty). The server doesn't change.

## 0.18.0

- **Live workouts on phones:** on a phone you now log at the gym, as you train. Tap **Start workout** on Home, and the card under the streak shows one exercise and one set at a time: type the weight and reps, then **Next**. The weight starts from your last set or last time.
  - **Any order:** **Skip for now** moves an exercise to the end, and tapping an exercise in the list does it now, so a busy machine doesn't hold you up.
  - **Changing a set:** tap a logged set to change its weight or reps.
  - **Bar exercises:** a Total / Per side switch and the bar's weight, like the Exercise screen. It locks after an exercise's first set, so all its sets are typed the same way.
  - **Focus:** while a workout runs, Home shows only the streak card and the workout. The semicircle fills as you finish exercises, and calories, PRs and friends come back when you're done.
  - **Finish** appears once half the exercises are done. **End workout** stops early without logging; your sets are kept.
  - **Not on the desktop:** the desktop keeps the "I went today" check-in. This is the first time phones and the desktop log differently.
- **Verified at gym:** during a workout the phone checks whether you're at one of your gyms (within about 150 m, 250 m for your own gyms). If you are, the workout is verified there: "At Iron Gym ✓", and that gym is logged for you.
  - **On the phone only:** your location never leaves it, and it's only read while a workout is open.
  - **Saying no:** you can refuse location; your workouts still count.
  - **Precise location:** it's needed to tell which gym you're at. Android explains this before it asks, and with approximate location the card asks you to turn precise on.
  - **Not at a gym:** the card says so, with **Add this gym**.
- **Points for a workout:** the full **+5** now needs a verified workout with at least 70% of its exercises done. Everything else that counts earns **+2**: under 70%, or not verified. That includes **every workout logged on the desktop**. Workouts from before 0.18.0 keep their +5. Week bonuses and cardio are unchanged.
- **Points history:** workouts now read "Workout at Iron Gym", "Workout (not verified)" or "(partial)".
- **The app pushes for the whole workout:** nothing during a workout says it already counts, and finishing early tells you what you'd miss ("do 1 more for full points"). The result appears after **Finish**.
- **"Continue your workout?":** leave the app for 12 minutes with a set logged and you get a notification. Coming back shows **Continue** or **Finish workout early**, and says beforehand when finishing now wouldn't count or would earn half points. Notifications are asked for at your first workout.
- **Workouts stop by themselves** after 4 hours or at midnight. Home tells you once how it went, or that it didn't count and your streak needs a workout today or tomorrow.
- **Custom gyms need an address** now, picked from the search, so workouts there can be verified. Gyms you added before keep working, with a note to add one.
- **Log in on phones:** the text at the bottom is smaller, so the whole screen fits without scrolling.
- **Friends' leaderboard:** your points there follow the same rule. Older builds are turned away once 0.18.0 is out.
- **Migration:** `LiveWorkouts` in the app's SQLite (a new `LiveWorkouts` table, and new `WorkoutLogs` and `Users` columns for live workouts and workout verification). The server doesn't change.

## 0.17.1

- **Signing up and changing your email:** these no longer wait for the email to go out, so a slow mail server can't make them look offline. The code is sent when the **Confirm your email** screen opens, which now also opens after you change your email.
- **Codes that didn't send:** if a code can't be sent, you can ask for another straight away.

## 0.17.0

- **Report or block someone:** tap the flag beside a friend or a search result, or **Report or block** on anything in your inbox. Pick what's wrong, add a note if you like, and send it. **Also block** is on by default.
- **Blocking:** ends any friendship or request between you, and neither of you can find the other or send a request. They aren't told: to them your username just doesn't exist. Splits and workouts waiting between you are deleted.
- **Blocked list:** the people you've blocked are in a new **Blocked** card on Friends, with **Unblock**. Unblocking doesn't bring the friendship back; either of you can send a new request.
- **Confirm your email:** after signing up, you get a 6-digit code by email to type in. You can do it later: nothing is held back, and Profile reminds you until it's done. Changing your email means confirming the new one.
- **Forgot password?** on Log in: enter your email, then the code and a new password. It signs out every device.
- **Terms of use:** creating an account now means ticking a box to agree to the [terms](https://qblackghost.github.io/GainStreak-releases/terms.html), which also go on the website.
- **Show password:** the eye inside the password boxes is gone. A small **Show password** button sits beside the Password label on Log in and Create account, where it shows both boxes at once, and they stay shown as you move between fields.
- **Keyboard on phones:** on Log in and Create account the keyboard's key moves to the next field, and on the last Create account field it closes the keyboard. Tapping outside a field closes the keyboard on every screen.
- **Usernames:** new accounts can't use slurs or profanity, or names that pass as staff (`admin`, `support`, `gainstreak`). Existing usernames keep working.
- **Suspended accounts:** an account that breaks the rules can be suspended after a report. It's signed out everywhere, can't sign in, and disappears from search, leaderboards, friend lists, the map and shares. Its workouts stay on the phone, and lifting the suspension brings everything back.
- **Leaderboard on phones:** a long username no longer runs into the **YOU** tag. The name gets the whole line, the tag sits beside your level, and a streak of 0 is hidden.
- **Migration:** `AddUserBlocks` in the app's SQLite (a new `UserBlocks` table, used by the TestBench's local mode) and `AddUserBlocksAndReports`, `AddUserSuspension` and `AddEmailConfirmation` in the server's Postgres (new `UserBlocks`, `UserReports` and `EmailCodes` tables, two new `Users` columns). Run the server's `migrate` before deploying it.

## 0.16.1

- **Splits:** the small **New split** button is gone; the dashed card under your splits does the same.
- **Avatars:** tap your avatar on Profile to pick one of nine colours and show your initials or one of eight icons. It shows in the app bar, the sidebar, the leaderboard and your friends list, and friends see it too.
- **Level rings:** every avatar on the leaderboard and friends list, and your own, has a ring that follows the level: dark at levels 1–2, silver at 3–4, lime at 5–6 and gold at Legend.
- **Migration:** `AddAvatar` in the app's SQLite (a new `UserProfiles` and a new `SyncStates` column) and `AddUserAvatar` in the server's Postgres (a new `Users` column). Run the server's `migrate` before deploying it.

## 0.16.0

- **GainStreak on phones:** first beta builds for Android (Google Play internal testing, or an APK sent directly) and iPhone (TestFlight). They're the same app as on the desktop: same account, same rules, same server. Workouts stay on the device they were logged on, so a phone and a computer signed in to one account each keep their own.
- **Phone layout:**
  - Five tabs at the bottom: Splits, Workouts, a raised Home button, Calories and Friends. Profile and the inbox are in the top bar.
  - Popups open as sheets from the bottom, and the Android back button goes back.
  - Friends holds both the leaderboard and the map behind a Leaderboard / Map switch.
  - The map fills the screen. Its sheet shows your gym until you drag it up, and the map's settings sit behind the sliders button.
- **Signing in on a phone:** the session is kept in the iPhone's Keychain or Android's encrypted storage.
- **Reminders (phones):** Profile → Reminders, off until you turn them on, which asks for permission. There's a reminder on training days at a time you pick (18:00 by default), and one the morning after a missed day (09:00). The desktop has no reminders.
- **"Please update":** when the server turns an old build away, the banner's **Update** button opens the Play Store on Android (iPhone once the App Store listing exists).
- **Log in and Create account:** a sliding Log in / Create account switch, a fade between the two, and a show-password eye on both.
- **Splits:** the **Set active** button is gone; **Open** does the same.
- **My exercises:** a new look. Your exercises are grouped by muscle with a search, and each one shows how many days it's in and how many sets you've logged. **New exercise** is at the top, and deleting now asks in a popup.
- **New split and New exercise are popups:** both open over the screen you're on instead of as a page, a sheet from the bottom on phones and a centered card on the desktop. For a split, tap the days in one row and a box to name each picked day appears. For an exercise, pick the muscle group from chips. An exercise created from a day goes straight into that day, and **Manage your exercises** opens your list, where **Edit** uses the same popup.
- **Friends:** the note under the title ("Friends see your username…") is gone, and so is the Sent requests card: the requests you sent are listed in the Add friend card, under the search, with **Cancel** on each.
- **Get set up:** step 2's button now opens Workouts instead of a single day.
- **Profile on phones:** Body data is more compact. Units and Gender sit side by side, the birth date fits on one row, Height sits beside your age, and activity is three short choices with their daily steps. **Save profile** sits right beside the maintenance estimate.
- **Profile:** the Units and Gender switches are smaller, and the Height box is sized like the other fields. **Visible to friends** and **Your gym on the Map** are gone from the home city card; sharing is still on the Map. On the weight chart every weigh-in is a dot: tap one (or use the arrow keys) to see its weight and day above it, the newest to start with.
- **Workouts:** an empty day's card no longer has **Copy from a day** beside **Add exercises** (it opened the same page). Copying is still on the day's page.
- **A workout day's page:** the note under the exercises now says to tap (or open) an exercise to log its sets. On phones the list fills the card and scrolls with the page instead of in a box of its own. In **Copy from another day**, **Cancel** and **Copy exercises** sit side by side across the bottom.
- **An exercise's page:** a new **Weekly stats** card at the top charts the heaviest working set of each week, six weeks at a time, with the weeks you set a PR in lime. Swipe it (or use ‹ ›) for earlier or later weeks. It replaces the PR history list; an edited PR now has **Undo edit** next to **Edit PR**. The PR hint under each set ("PR 80 kg × 6 · 7+ reps…", "New PR!") is gone; a set that beats the PR still turns its boxes lime. The Total / Per side switch is smaller, with the bar's weight on the same row.
- **An exercise's page on phones:** the top-set chart comes before today's sets, **Add set** and **Save sets** sit side by side across the card, "Working" fits on one line in the set rows, and the Warm-up / Working counters fit their pills.
- **A split's page:** the notes beside **Use this split** and under "About this split" are gone, on the desktop too. **Use this split** is bigger and in the middle of the card, with **Edit split** under it for custom splits, and the setup card is more compact.
- **A split's page on phones:** "Good for" is now a small card at the top. The start day row is gone: tap a day in **Your week** to start the split there, and the start day turns lime. "Who uses it" is smaller, with the donut beside its legend.
- **Migration:** `AddReminders` in the app's SQLite (four new `Users` columns). The server is unchanged.

## 0.15.6

- **Profile setup:** right after creating an account, a new screen asks for gender (Male, Female or Prefer not to say), birth date, height, current weight, activity and home city before Home. Everything is optional: **Skip for now** saves nothing, and **Save and continue** saves only what was filled in (the weight as today's weigh-in). A box shows how many of the five body answers are in and, once all are, the maintenance estimate. After saving, a summary lists each answer or "Not set" and offers to use the estimate as the daily calorie goal. Signing in, even on a new device, still goes straight to Home.
- **Gender:** the Profile screen's "Sex" field is now labelled "Gender", and the maintenance hint asks for your gender. The stored value is unchanged.
- **Get set up:** step 2 is now "Add exercises to every workout day": it names the split's days, says which still need exercises ("1 of 3 days are ready. Pull and Legs still need exercises.") and opens the next empty one, today's first. It's done once every day has exercises; today's day alone still unlocks check-in. Step 3 first asks for body data (birth date, height, weight and activity, opening Profile), then turns into the calorie goal with the maintenance estimate. Gender isn't required, so "Prefer not to say" doesn't block it.
- **Profile layout:** Weight sits under Body data on the left; Home city moved to the top of the right column, above Account and Appearance.
- **Exercises:** 19 new built-in exercises: Machine Chest Press, Pec Deck, Dumbbell Fly, Chest-Supported Row, Chin-Up, Close Grip Lat Pulldown, Back Extension, Machine Shoulder Press, Arnold Press, Reverse Pec Deck, Cable Curl, Rope Pushdown, Overhead Cable Triceps Extension, Goblet Squat, Stiff-Leg Deadlift, Dumbbell Romanian Deadlift, Hip Abduction, Hip Adduction (both under Glutes) and Hanging Knee Raise.
- **Migration:** `MoreExercises` in the app's SQLite (new rows in `Exercises`). The server is unchanged.

## 0.15.5

- **Inbox:** a new Inbox screen, in the sidebar above Friends, holds friend requests and the splits and workouts friends share. An All / Requests / Workouts pill filters it; new items have a dot until **Mark all read**, and the sidebar's red badge (now on every screen) counts them. Accept and Decline work right there; what was done from the inbox is listed under Earlier. Friends now only lists the requests you sent, and the dashboard's Friends card has no badge of its own.
- **Sharing:** Workouts and Splits have a lime **Share** button. Pick a workout (a day of your split) or a whole split, tick up to 10 friends and send. The friend gets a card in their inbox with a preview:
  - **A split** comes with every day and its exercises. Adding it asks which weekday day 1 starts on (a custom split keeps its gaps, shifted to that day) and, for a built-in split, whether the shared exercises replace yours on that split or it becomes a new custom split. It becomes the active split.
  - **A workout** loads into a day of your current split (a day with the same name is picked first), added to the end or replacing the list.
  - Exercises travel by name: built-ins match by name, a friend's custom exercise reuses yours of the same name or is created. Shares are snapshots, removed once added or dismissed, and dropped after 30 days or when you're no longer friends.
- **Adding exercises:** adding an exercise to a workout day opens that exercise right away, so its sets can be set up.
- **Split page:** the button on the active split reads **In use** (greyed) until you change the days per week or the start day, then **Save**.
- **Server:** `POST /v1/shares`, `GET /v1/shares` and `DELETE /v1/shares/{id}`, friends only, at most 30 shares an hour per user and 30 waiting per receiver.
- **Migration:** `Shares` in the server's Postgres (a new table). Run it before deploying. The app's database is unchanged; which inbox items were read and the Earlier list are kept in a small `inbox-<user>.json` file next to it.

## 0.15.4

- **Per side weight:** bar exercises (barbell lifts, Smith machine, EZ bar curls, picked by name; custom exercises too) have a Total / Per side toggle on the exercise screen. Per side takes the plates on one side and an optional bar weight, and each row shows the total, e.g. "= 140 kg total (60 × 2 + 20 bar)". Switching converts what's typed. Sets still store the total, so PRs, points and stats are unchanged. The mode and bar weight are remembered per exercise.
- **Cardio:** the check-in takes optional cardio minutes, saved with the day's workout. +1 point per full 10 minutes, at most +3 a session. Cardio entries show in the points history under Workouts.
- **Migrations:** `CardioMinutes` (`WorkoutLogs.CardioMinutes`) and `ExerciseBarSettings` (a new table), both in the app's SQLite. The server is unchanged.

## 0.15.3

- **Split usage:** a split's page has a "Who uses it" card under the setup card: a donut of the share of lifters on each preset and on custom splits, with this split's slice in the accent and its share in the middle. Splits under 10% (other than the one shown) are grouped into "Other", whose legend row names them. Preset cards in the split list show "N% of lifters". The numbers come from the server; without them the card and the line stay hidden, and offline the last fetched numbers are shown.
- **Recommended days:** preset cards and the split page show "Recommended for N days / week", from the preset's default number of days.
- **Server:** the stats upload (`PUT /v1/me/stats`) also carries the active split: the preset's id, or no id for a custom split. Older builds don't send it and aren't counted. `GET /v1/splits/usage` (signed in) returns how many accounts use each preset and custom splits.
- **Map:** turning "Visible to friends" on or off reloads the map, your pin and your friends' check-ins at once. Your pin now also shows when today's gym is in a city whose gyms were loaded after the Map opened (before, it appeared only after reopening the Map). Your pin never depended on the setting.
- **Gym list loading:** the public map servers can take minutes when busy. After 15 seconds the loading line says "Still loading. The map servers are busy right now." with **Try again**, which drops the waiting request and sends a new one.
- **Migration:** `SplitUsage` in the server's Postgres (`Users.SplitPresetId` and `Users.UsesCustomSplit`). Run it before deploying. The app's database is unchanged.

## 0.15.2

The first build for beta testers, with 0.15.1's light theme. 0.15.1 had no release of its own.

- **Light theme (12e):** Profile → Appearance now works: System (the default, follows Windows or macOS), Light or Dark, applied at once and kept on the account. The login screens follow the system. Every screen takes its colors from one set of light and dark tokens (`IronlogResources.axaml`) instead of fixed dark colors, after the Light page of the Gym App design canvas: off-white page, white cards, ink text, and a darker lime for accent text and outlines on white.
- **Installers and updates:** releases are built with Velopack (`build/release.sh`): a `.pkg` installing `GainStreak.app` for Apple silicon and Intel Macs, and a `Setup.exe` for Windows. Installed copies check the release folder at startup and download a newer version in the background; a banner offers "Restart now", or it installs at the next start.
- **Server address built in:** a release now carries the social server's address (`GymAppServerUrl` in `Directory.Build.props`), so testers don't set `GYMAPP_SERVER_URL`. The variable still overrides it, and publishing a release without an address stops with an error.
- **Hosted social server:** the beta server runs on Google Cloud Run (scales to zero, one instance at most) with Postgres on Neon, and release builds point at it. Secrets (the connection string and the Discord webhook) live in Secret Manager.
- **Server works on hosts that scale to zero:** the check-in cleanup runs from check-in uploads, at most once a day per instance, instead of on a daily timer an idle instance never reaches. Feedback is posted to Discord before the request answers instead of from a background queue. A failed post still answers 204, and each new feedback retries the last week's unforwarded rows (10 at most, stopping at the first failure).
- **Proxy check:** `Diagnostics__LogProxyHeaders=true` logs the proxy's address, `X-Forwarded-For`/`X-Forwarded-Proto` and the client IP the server settles on, for setting `ForwardedHeaders__KnownNetworks` on a new host. Off by default, since it logs client IPs. On Cloud Run that's `169.254.0.0/16`.
- **Migration:** `ThemePreference` in the app's database (`Users.ThemePreference`, "System" for existing accounts).

## 0.15.0

- **New name and icon:** the app is now GainStreak. Window titles, the sidebar and sign-in brand, messages and the version line in Profile use the new name, and the app icon is the fire-orange flame-arrow with a lime outline. The executable is `GainStreak` (`GainStreak.exe` on Windows), and on macOS the Dock shows the GainStreak name and icon. Code, project names and the data folder are unchanged.
- **Points history as a popup:** "History" on the dashboard's level card opens it over the page instead of a separate screen. It shows the total and progress to the next level, the ledger by week with a subtotal each, a filter (All, Workouts, Weeks, Calories) and how points are earned. Escape, the close button or a click outside closes it.

- **Phase 15, crash reports and the version check:**
  - Every `errors.log` entry names the app version and system. **Feedback**, in its own Support section of the sidebar, opens a popup to report a problem or suggest an idea. **Send** posts it to the server, which stores it and forwards it to the developer's Discord channel. A problem attaches the error report (the app, the system and the log, newest 100 KB) unless turned off; "See what's sent" shows it first, and user names are taken out of file paths before anything leaves the device. Offline or refused, the note stays and "Copy instead" puts it on the clipboard. "Show log file" opens its folder.
  - Server: `POST /v1/feedback` (signed in, 5 an hour per user), the `Feedback` table (deleted with the account) and `Feedback__DiscordWebhookUrl` for forwarding.
  - The app sends its version (`X-App-Version`) with every server call. With `MinClientVersion` set, the server answers older builds, and builds that send no version, with 426 and "please update". The app shows that as a banner, keeps signing in from its cache so workouts still work, and keeps its pending uploads for after the update.
  - **Migration:** `AddFeedback` in the server's Postgres (the `Feedback` table). The app's database is unchanged.

## 0.14.1

The app on the social server. Workouts stay on the device; accounts, friends, the leaderboard and shared check-ins move to the server.

- **Phase 14, the app on the server:**
  - Server additions: change the account email, and replace all of your shared check-ins in one call.
  - `GymApp.Client`: a typed client for the server, where every call is Ok, Refused (with the server's message) or Offline, a 10-second timeout, and the session token stored in a file only you can read.
  - Accounts on the server: register, sign in with your username or email and stay signed in until you log out. Change your email or password, and delete your account (online and on the device) from Profile. "Remember me" is gone.
  - Offline sign-in on your own device with your last password; a wrong password from the server never falls back to it.
  - "Move this account online" for accounts from before the server: keeps their workouts, points and streak.
  - Friends, requests and both leaderboards from the server, kept on the device and shown offline with "Last updated ...". Friend actions need a connection.
  - Uploads of your sharing setting, the last 31 days of check-ins and the 14-day points and streak projection, retried until they get through (after a check-in or undo, a gym change, a sharing change, at sign-in, every 15 minutes and when the connection comes back).
  - The friends map from the server, matched to the gyms and cities on this device.
  - TestBench: a Social switch, Local (the default, no server needed) or Server.
- **Migration:** `SocialServerLink` in the app's database (the link to the server account, the offline cache and the pending uploads). Existing data is unchanged.

## 0.13.0

- **Phase 13, the social server:** an ASP.NET Core API on Postgres for accounts, friends, the leaderboard and shared gym check-ins, with rate limits, a Docker image and migrations run on their own. The app doesn't use it yet.

## 0.12.0

The first complete version of the app. It covers Phases 0 to 12 of the implementation plan.

- **Phase 0, project setup:** a solution with Core, Data, Services, Desktop (Avalonia) and Tests projects.
- **Phase 1, data model:** EF Core with SQLite, the first migration, and seeded preset splits (PPL, Upper/Lower, PPL + Upper/Lower, Full Body, Bro Split).
- **Phase 2, accounts:** register and sign in with hashed passwords, and a "remember me" sign-in.
- **Phase 3, splits:** pick a preset split or build a custom one with your own training days.
- **Phase 4, schedule and streaks:** preset layouts with a chosen start day, schedule history so changes never rewrite the past, the "I went today" log, and a streak where rest days count and a miss can be made up the next day (the grace rule).
- **Phase 5, exercises and PRs:** a built-in exercise list per muscle group, custom exercises, exercises per split day with planned sets, set logging, PR history with an Epley 1RM estimate, and manual PR edits.
- **Phase 6, profile:** sex, birth date, height, activity level, units (metric or imperial), weigh-ins, and a maintenance-calorie estimate.
- **Phase 7, calorie goals:** lose, maintain or gain with a suggested goal and safe limits, plus daily calorie logs with a target range.
- **Phase 8, points and levels:** a points ledger for workouts, complete weeks (+50, or +25 with a made-up day) and calorie targets, with levels computed from the total.
- **Phase 9, gyms:** a home city from Nominatim, gyms from Overpass cached per city, a list or map picker, custom gyms with address search, the gym logged per workout, and gyms visited per month.
- **Phase 10, friends:** friend requests and a friends-only leaderboard (this week or all time) with each friend's streak.
- **Phase 11, check-in rules:** undo today's check-in (and the points it earned), a check-in that needs most of the day's exercises filled in, and last session's sets as placeholders.
- **Phase 12, polish:**
  - Streak fix: a user with no streak no longer flips between 1 and 0.
  - Missed-day choice the day after a miss: shift the whole schedule (once a week), make it up before the next rest day (twice a week), or skip it.
  - Home is now a dashboard: today's workout, this week's row, recent PRs and the friends card.
  - Validation and error handling: stricter usernames and passwords, whole-number parsing, messages next to each field, Retry for online searches, an offline note on the map, and a global error handler that writes `errors.log`.
  - Light and dark themes with a theme setting, and the app icon. *(12e, still to do.)*
  - Release prep: one shared version, the version shown in Profile, the README and this changelog.
