# Phase 9: Gym map — spec for Claude Code

This is the spec for GymApp Phase 9: gyms from OpenStreetMap, a gym prompt after "I went today", custom gyms, and a monthly summary. Work through the sub-phases **in order** and stop after each one for review.

## Ground rules

- **Do not run `git commit` or `git push`.** Leave every change in the working tree for review.
- After each sub-phase, run `dotnet build` and `dotnet test`. Then report:
  - which files you created
  - which files you changed
  - which migration you added
- Follow the existing project layout:
  - **GymApp.Core**: pure logic, no EF and no HTTP. Unit-tested.
  - **GymApp.Data**: entities, `AppDbContext`, migrations.
  - **GymApp.Services**: services, EF access, HTTP clients.
  - **GymApp.Desktop**: Avalonia views and view models only, using CommunityToolkit.Mvvm.
  - **GymApp.Tests**: xUnit. Service tests run against in-memory SQLite with the real migrations, like the existing Phase 5 tests.
- Read the existing `Gym`, `WorkoutLog`, `UserProfile` and `AppDbContext` before changing anything. Field names below are proposals, so match the existing naming style. If something already exists (for example `WorkoutLog.GymId`), reuse it instead of adding a duplicate.
- Seeded ids are permanent. Don't touch existing seed data.
- Phase 9 depends on the Phase 6 `UserProfile`. If it doesn't exist yet, stop and say so.

## Decisions already made (don't change these)

1. The **home city** is an optional field on the profile. The gym picker uses it by default, and the user can search other cities.
2. The gym is **optional**. Right after "I went today" is saved, a prompt asks "Want to log your gym?". The workout is already logged at that point, so the prompt never affects the streak or points.
3. The prompt has a **"Don't ask me again"** option, stored on the profile and switchable back on in the profile screen.
4. **There is no gym picker in the Workouts menu.** The gym exists only on the Home check-in. The Workouts screen keeps only the existing reminder hint.
5. **Custom gyms** are allowed. A user can add gyms that aren't in OSM.
6. **No points of any kind for gyms.** A gym choice can't be verified.
7. Gym changes follow the no-backdating rule. Today's log can get a gym or change it until midnight. Past days are locked.
8. The visual map (Mapsui) is an optional upgrade at the end. Nothing else depends on it.

---

## 9a. Data model and migration

**New entity `City`.** It holds cities the user has picked from search results.

| Field | Details |
| --- | --- |
| Id | |
| OsmType + OsmId | The Nominatim result's OSM object. Unique together. |
| Name | For example "Timișoara". |
| DisplayName | With county and country, for disambiguation. |
| CenterLat, CenterLon | |
| South, West, North, East | The bounding box from Nominatim. |
| GymsFetchedAt | Nullable. When gyms were last fetched successfully. |

**Changes to `Gym`.** Gyms are shared across users. Custom gyms are the only exception.

| Field | Details |
| --- | --- |
| OsmType + OsmId | Both nullable (null means a custom gym). OSM ids are only unique per type, so the unique index is on the pair, filtered to non-null values. |
| CityId | Required. Points to City. |
| Name, Address | Address is nullable. |
| Lat, Lon | Nullable. Custom gyms have no coordinates. |
| OwnerUserId | Nullable. Null means the gym came from OSM. Otherwise it's a custom gym owned by that user. |
| IsActive | Default true. |

Keep the existing Gym columns unless they conflict.

**Changes to `UserProfile`:**

| Field | Details |
| --- | --- |
| HomeCityId | Nullable. Points to City. |
| AskForGymAfterCheckIn | Bool, default true. |

**`WorkoutLog`.** It needs a nullable `GymId`. Check first whether it already has one.

**Delete behaviour.**
- **A gym that a WorkoutLog points to is never hard-deleted.** It is deactivated (IsActive = false) instead.
- Use `Restrict` on WorkoutLog → Gym so a mistake fails loudly.

Add one migration, `GymMap`.

**Done when:**
- the migration applies cleanly to an existing database
- the existing tests still pass

---

## 9b. City search (Nominatim)

**`GeocodingService`** in Services:
- It searches a city name and returns up to 5 candidates: name, display name, OSM type and id, centre, bounding box.
- It sends a proper `User-Agent` (for example `GymApp/1.0 (student project)`), as Nominatim requires.
- It runs at most 1 request per second. Searches run on button press or Enter, **not on every keystroke**.
- It uses a timeout of about 10 s. Network errors come back as a result the UI can show, not as an unhandled exception.

**Choosing a candidate:**
- Choosing a candidate upserts a `City` row by OSM type and id.
- The profile screen gets a **Home city** field with search, a candidate list, and a clear button.

**Logic in Core:**
- Parsing the Nominatim JSON into a candidate model.
- Tested with fixture JSON files. Tests never call the real API.

**Done when:**
- a search for "Timișoara" returns candidates
- choosing one sets the home city
- an ambiguous name shows several candidates with their county and country

---

## 9c. Fetching and caching gyms (Overpass)

**`GymFetchService`** in Services:
- It queries Overpass for all `leisure=fitness_centre` objects inside the city's bounding box. Use `nwr` with `out center tags;` so ways and relations come back with a centre point.
- Timeout about 25 s, the same User-Agent, errors returned as a result.

**Mapping, in Core (pure and tested):**
- Name comes from the `name` tag.
- Address comes from `addr:street` plus `addr:housenumber`, if present.
- Coordinates come from the point itself or from `center`.
- Elements without a name become "Unnamed gym", with the street added if one is tagged.

**Merge, in Core.** It takes the existing OSM gyms for the city and the fetched list, and decides what to add, update, or deactivate:
- Match on OSM type and id.
- Update the name, address, and coordinates of gyms that still exist.
- Add new ones.
- Gyms that are missing from the fetch are deactivated (IsActive = false), never deleted.
- A deactivated gym that comes back is reactivated.
- Custom gyms are never touched by the merge.

**Cache rules:**
- **Opening the picker reads from the database only.**
- A fetch happens on the first use of a city (GymsFetchedAt is null), when the user taps **Refresh**, or automatically when the data is older than 30 days.
- GymsFetchedAt is set only after a successful fetch.
- If a fetch fails and a cache exists, show the cached list with a small "Couldn't refresh" note. If there's no cache, show the error and a **Retry** button.

**Done when:**
- Timișoara gyms load into the database
- refetching creates no duplicates
- the merge tests pass (add, update, deactivate, reactivate, custom gyms untouched)

---

## 9d. Gym picker and check-in prompt

**`GymService` (Services):**
- `GetGymsForCity(userId, cityId, search, sort)` returns the active OSM gyms plus that user's own custom gyms in that city.
- **Search** is a case-insensitive name match.
- **Sort** is either by name, or by distance from the city centre.
  - Use haversine in Core, tested.
  - Gyms without coordinates (custom gyms) go last, sorted by name.

**Setting the gym on a log (Services):**
- `SetGymForToday(userId, gymId or null)` sets or clears the gym on today's WorkoutLog.
- It fails if there is no log today.
- It never changes a log from another day.
- The gym must be active, and must be either an OSM gym or one owned by this user.

**Remembering the last gym:**
- The "last-used gym" is the gym on the user's most recent WorkoutLog that has one.
- It is derived, not stored.
- It is only offered if that gym is still active.

**The prompt (Desktop):**
- After "I went today" saves, and only if `AskForGymAfterCheckIn` is true, show a small inline prompt on Home:
  - **"Want to log your gym?"**
  - a one-tap button with the last-used gym (only if there is one)
  - **Choose another…**, which opens the picker
  - **Skip**
  - **Don't ask me again**, which sets the profile flag to false and closes the prompt
- **If there's no home city**, Choose another… asks for a city first. Offer to save it as the home city, but don't force it.
- After Skip the prompt doesn't come back that day.

**Today's log on Home:**
- It shows an **"Add gym"** link, or "Change gym: \<name\>" when one is set.
- The link works until midnight, then the log is read-only.
- The link stays available even when the prompt is turned off.

**The picker screen:**
- City name with a "change city" link
- search box
- sort toggle (name or distance)
- Refresh button with a "last updated" line
- the list (custom gyms get a small "custom" tag)
- an **Add custom gym** entry (see 9e)

**Profile screen:**
- an **Ask which gym after check-in** toggle

**Done when:**
- check-in → prompt → pick saves the gym on today's log
- Skip and Don't ask me again behave as described
- yesterday's log can't be changed
- the picker works offline from the cache

---

## 9e. Custom gyms

- Fields: name (1–60 characters), optional address, and a city (the picker's current city). No coordinates.
- **Limits:**
  - at most 10 custom gyms per user
  - names unique per user within a city, ignoring case
  - two users may use the same name
- Only the owner can edit or delete.
- **Deleting a custom gym:**
  - If no WorkoutLog uses it, delete it.
  - Otherwise deactivate it. It disappears from the picker but stays in history and summaries.
  - The service reports which of the two happened, so the UI can tell the user.
- **Tests:** the limit, name uniqueness, ownership checks, and delete vs. deactivate.

---

## 9f. Monthly "gyms visited" summary

**Logic in Core (pure and tested).** The input is a month's WorkoutLogs with their gym, or none. The output is:
- visits per gym, sorted by count and then by name
- the number of distinct gyms
- total workouts
- a **"No gym logged"** count

**How it's computed:**
- Calendar month, in local dates.
- Always recomputed, never stored.
- Inactive gyms are still counted and shown by name.

**Screen (Desktop):**
- reachable from Home or the profile
- previous and next month buttons (no future months)
- a simple list or bar layout

**Done when:**
- summary tests cover an empty month, a month with only "no gym" logs, several gyms, and an inactive gym

---

## 9g. Optional: Mapsui map view

- Only start this after 9a–9f have been reviewed.
- Add a map or list toggle to the picker:
  - The map shows the same cached gyms as markers, centred on the city.
  - Clicking a marker does the same thing as picking the gym from the list.
  - Custom gyms without coordinates appear only in the list.
- The map tiles are the only thing that needs the network. If tiles fail to load, the list still works.
- Check that the Mapsui Avalonia package supports the project's .NET and Avalonia versions before adding it. If it doesn't, stop and report back.
