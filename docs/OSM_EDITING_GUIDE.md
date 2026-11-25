# Fixing gyms on OpenStreetMap

GainStreak's gym list is a copy of OpenStreetMap (OSM). The server's weekly `import-gyms` job downloads the Geofabrik
extract for Romania (rebuilt daily from OSM) and keeps every `leisure=fitness_centre`. So a fix made on OSM reaches
the app by itself, usually **within about 8 days**, and it helps every other map that uses OSM too.

This guide is for fixing the gyms found in a city review (`docs/GYM_REVIEW_<CITY>.md`) with your own OSM account.
It takes about two minutes per gym once you're used to it.

## 1. Rules first (OSM is strict about these)

- **Never copy from Google Maps**, Google Street View, Waze, Apple Maps or any other map, including their pins,
  outlines and place names. That breaks their copyright and OSM deletes such edits. The same goes for Wellhub's or
  other directories' coordinates.
- **Allowed sources:** what you saw yourself, the gym's **own** website or Facebook page (name, address, opening
  hours), and the aerial imagery built into the OSM editor (to place the pin on the right building).
- **Only change what you're sure of.** If a gym *might* be closed, leave it. In the review table, only lines with
  High confidence (or that you checked yourself) are ready for OSM.
- **Edit by hand, a few at a time.** You're editing for your own app, which is fine, but OSM frowns on bulk edits
  and on edits that look like advertising. Add a short note on your OSM profile, for example *"I map gyms in Romania;
  I also make a gym app that uses OSM data."* If you ever plan to fix hundreds of gyms across the country, post on the
  Romanian section of the OSM community forum (<https://community.openstreetmap.org>) first and follow OSM's
  Organised Editing Guidelines.

## 2. Set up (once)

1. Create an account at <https://www.openstreetmap.org/user/new>. Use your own name or a nickname, not the app's.
2. Confirm the email.
3. Open <https://www.openstreetmap.org>, zoom to the city, and press **Edit** (top left). This opens **iD**, the
   editor in the browser. The first time, it offers a short walkthrough: take it, it's 10 minutes and worth it.

## 3. The tags a gym needs

A gym is a point (or a building outline) with these tags. In iD you can pick the preset **"Gym / Fitness Center"**,
which sets the first one for you.

| Tag | Example | Notes |
|---|---|---|
| `leisure` | `fitness_centre` | **Required.** This is what the import looks for. |
| `name` | `Gym One 2` | What the sign says. The app shows it; without it the gym is "Unnamed gym". |
| `brand` | `Gym One` | For chains. iD suggests the brand for known chains (World Class, Stay Fit, ...); accept it. |
| `addr:street` | `Strada Mureș` | Full street name with its type, as OSM writes it nearby. |
| `addr:housenumber` | `116A` | |
| `addr:city` | `Timișoara` | |
| `website` | `https://gymone.ro` | Optional. |
| `opening_hours` | `Mo-Fr 06:00-23:00; Sa,Su 08:00-22:00` | Optional. iD has a helper for the format. |

Inside a mall (Shopping City, Iulius, Bega Center, Auchan), put the gym as a **point inside the mall's outline**, as
close as you can to where the gym's entrance is. Don't tag the whole mall as a gym.

## 4. The four kinds of fixes

### Missing gym: add it

1. In iD, press **Point** (top bar), click where the gym is. Use the aerial imagery to find the right building; if
   you know the entrance, put it there.
2. Search for **Gym / Fitness Center** and pick it. For a chain, pick the branded preset if iD offers it.
3. Fill in `name` and the address fields (section 3).

Precision matters: the app verifies a live workout within **150 m** of the gym's point. A pin on the right street
but the wrong end of it isn't good enough.

### Renamed gym (same place, new owner or name): edit it

1. Click the gym's point.
2. Change `name` (and `brand` if it has one, and `website`). If iD shows a "brand" warning, use its fix to apply the
   new brand.
3. Don't move or delete the point: keeping it keeps its OSM id, so in the app past workouts keep pointing to the same
   gym.

### Moved gym: move the point

If the same gym (same name, same business) moved to a new address, select its point and **drag it** to the new place
(or, for a long move, use **Move** from the right-click menu and click the new place), then update the address
fields. The point keeps its OSM id, so in the app it's the same gym, now at the new place.

If a *different* gym now uses the old place, edit the point as a rename instead, and add the moved gym as a new one.

### Closed gym: remove it

Only when you're sure it closed for good (you saw it, or the business says so). Select the point and **Delete**
(right-click → Delete, or the trash icon). In the app, it's deactivated, not deleted, so past workouts still show it.

If the building is an outline that's only tagged as a gym, don't delete the building: remove just the
`leisure=fitness_centre` and `name` tags.

## 5. Save with a good comment

Press **Save** (top right). iD asks for a comment and sources:

- **Comment:** say what changed, for example *"Timișoara: add Gym One 2 and Stay Fit Central, rename Smartfit
  Studio 2 to World Class Buziașului (taken over)"*.
- **Sources:** tick *survey* if you saw it yourself, and write the websites you used, for example
  `gymone.ro; stayfit.ro; Bing aerial imagery`.

Save often: one city or one small group of gyms per save is ideal.

## 6. After saving

- The edit is live on openstreetmap.org right away; you can open the point and check its **History**.
- Within a day it's in the Geofabrik extract, and the next weekly `import-gyms` puts it in the app.
- Mark the line in the review table as **done in OSM** with the date.
- Someone may comment on your changeset (you get an email). Answer politely; it's how OSM keeps quality up.
- **Handing it to Claude:** just say which gym you did. Claude reads your saved edits from the public OSM API (your
  latest changesets), takes the pin from the gym's point or building, checks the edit, and updates the review table.
  No need to copy coordinates. If you want to send something anyway, send the link to the gym (select it on
  openstreetmap.org and copy the address bar, for example `openstreetmap.org/way/469907683`).

### Check before saving: things that get glued together

When you drag a corner of a building, iD **snaps** it onto anything it passes over: a road, a fence, a power line.
The building and that line then share a point, which is wrong (it happened with Gym One 1: a building corner got
joined to a 110 kV power line). Before pressing Save:

- Open the **Issues** panel (the warning-triangle icon on the right) and read every warning about your edit.
- To drag without snapping, hold **Alt** (⌥ on a Mac) while dragging.
- To fix one that's already glued, select the shared point and press **D** (Disconnect). Then select the point left
  on the other line and delete it, so the line goes straight again.
- If an edit went wrong, press **Undo** (⌘Z) before saving. After saving, fix it with a new edit.

### Gym as a building outline or as a point

If the whole building is the gym, you can tag the building outline with `leisure=fitness_centre` (the import takes
the centre of the outline). If the gym is only part of a building (a mall, a ground floor), use a point inside it.
Either way, don't delete a neighbouring building unless it really doesn't exist.

## 7. OSM or the app's override layer?

Both fix the same problem, and they shouldn't both be used for the same gym, or the app shows it twice (for an add)
or the fix sticks after OSM changes it (for a rename or hide).

| | OSM edit | Override layer (0.28, section 0b) |
|---|---|---|
| Who sees it | Every map using OSM | Only GainStreak |
| How fast | About a week | On the next server deploy |
| Can be undone by someone else | Yes, any mapper can edit it | No |
| Best for | Facts you're sure of: missing chain clubs, renames, moves | Quick fixes, and anything you'd rather not put on OSM |

A simple rule: **put sure facts on OSM**, and use the override layer only for what OSM doesn't have yet. When an OSM
fix reaches the import, drop the matching override line (the import is planned to report these; see 0b).

## 8. Timișoara: what's ready for OSM

From `docs/GYM_REVIEW_TIMISOARA.md` (High confidence or decided):

| What | Where | Fix |
|---|---|---|
| Rename `Smartfit Studio 2` → `World Class Buziașului`, brand World Class | n4179532756, Calea Buziașului 11 | Rename |
| Rename `World Class` → `BeFit VOX`, brand BeFit | n6703100298, Calea Torontalului 69 | Rename |
| `Gym One` (old Gym One 1) | n5224923392 | Move to Strada Varșovia 3, Dumbrăvița, and rename to `Gym One 1` |
| Add Gym One 2 | Str. Mureș 116A | Add |
| Add Gym One 4 (name it `Gym One 4`) | Strada Memorandului 76 | Add |
| Add World Class Shopping City | in Shopping City, Calea Șagului 100 | Add (point inside the mall) |
| Add BeFit Hanover | Bd. Cetății 5 | Add |
| Add BeFit Modern | Bd. Simion Bărnuțiu 56, upstairs | Add |
| Add Iguana Modern | Bd. Simion Bărnuțiu 49 | Add |
| Add Stay Fit Central / Nord / Giroc | Bega Center; Auchan Nord, Calea Aradului 56A; Glass Gallery Giroc | Add (points inside the malls) |
| Add 27Fit, Banu Sport | Calea Stan Vidrighin 14; Aleea F. C. Ripensia 33 | Add (Medium: check the building first) |
| Add Alma Libre Body & Soul | Str. Mareșal Constantin Prezan 120A | Add, and set `addr:housenumber` too: OSM doesn't have this number yet |

On OSM you place these pins on the building yourself (aerial imagery or what you saw), so the house numbers are
enough there. Add `addr:housenumber` for all three: OSM doesn't have those numbers yet, and adding them helps the
address search find them next time.

Not for OSM:
- **The yoga studio** (A23). OSM tags yoga studios as `leisure=fitness_centre` (often with `sport=yoga`), so it isn't
  wrong there. Hiding it is an app decision: do it in the override layer. If other cities have many, the import could
  skip `sport=yoga` instead (to decide in 0b).
- **World Class City of Mara.** Leave it until it opens (estimated 8 January 2027), then name the unnamed point there
  (n14391900791).
