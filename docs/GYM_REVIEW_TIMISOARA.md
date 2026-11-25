# Gym review: Timișoara (pilot, 0.28 section 0a)

Working file, **not committed until reviewed**. Nothing in any database was changed.

- **OSM data:** `leisure=fitness_centre` in the box 45.68,21.10 – 45.84,21.36 (the city plus Dumbrăvița, Giroc,
  Moșnița), read from the public Overpass server on 2026-11-25: **24 gyms**.
- **Checked against:** the chains' own websites (gymone.ro, worldclass.ro, befit.ro, stayfit.ro, smartfitstudio.ro,
  iguanafitness.ro), Wellhub partner listings, and web search. No Google Maps data was used.
- **Coordinates** for moved or missing gyms come from OpenStreetMap's Nominatim geocoder. "House" means the exact
  building or address was found; "street" means only the street was found, which is **not good enough**: a live
  workout is verified within 150 m of an OSM gym (`GymProximity.OsmRadiusMeters`), so a street-level pin needs a check
  before it goes in.
- **Weak spot:** small independent gyms with no website. Web search found nothing for several of them; they stay
  "unsure" and keep their OSM entry until someone checks.

Proposed actions: **keep** (no change), **rename** (the override layer would need a rename, which section 0b doesn't
include yet; see the questions at the end), **hide** (closed, or moved with the new place added), **add**.

## A. Gyms in OpenStreetMap (24)

| # | OSM id | OSM name | Status | Found | Proposed | Confidence |
|---|---|---|---|---|---|---|
| 1 | n5224923392 | Gym One | **Moved, done in OSM** | The old Gym One 1 (Strada Divizia 9 Cavalerie area, Lipovei; OSM entry from 2017). Gym One's site now puts Gym One 1 at Strada Varșovia 3, Dumbrăvița (see B1). **Deleted on OSM 2026-11-25** (changeset 192447796). | None left: the import drops it | High |
| 2 | n4734478338 | Gym One 3 | Open | Gym One site: Aleea Studenților 1 (Complexul Studențesc). OSM point is ~75 m away. | Keep | High |
| 3 | n7205756121 | Gym One 5 | Open | Gym One site: Bega Circumvalațiunii (DN59 nr. 8). OSM point is on the same building. | Keep | High |
| 4 | n10801059118 | Gym One 6 | Open | Gym One site: Str. Ion Ionescu de la Brad 15 (Bega Lipovei). OSM point ~35 m away. | Keep | High |
| 5 | n6637001995 | World Class | Open | World Class Iulius Town, Piața Consiliul Europei 2 (Iulius Mall). ~120 m. | Keep (name could say "Iulius Town") | High |
| 6 | n10687727237 | World Class | Open | World Class Dumbrăvița, Strada Conac 50. | Keep | High |
| 7 | n11449154508 | World Class | Open | World Class ISHO, Splaiul Protopop Meletie Drăghici 3. | Keep | High |
| 8 | n6703100298 | World Class | **Renamed** | Calea Torontalului 69 (VOX Technology Park). World Class no longer lists a VOX club; BeFit lists "BeFit VOX" at this address. | Rename to BeFit VOX | High |
| 9 | n4179532756 | Smartfit Studio 2 | **Renamed** | Calea Buziașului 11. Smartfit now lists only its Cetății club in Timișoara; World Class lists "World Class Buziașului" at Calea Buziașului 11 (news: two Smartfit clubs were taken over by World Class). | Rename to World Class Buziașului | High |
| 10 | n5690958842 | Smartfit Studio | Open | Smartfit site: Bd. Cetății, corner of Calea Torontalului (OSM: Bd. Cetății 97). | Keep | High |
| 11 | n13825752338 | Befit | Open | BeFit Rebreanu, Bd. Liviu Rebreanu 126. OSM point ~10 m from the address. | Keep (name could say "Rebreanu") | High |
| 12 | n14391703992 | BeFit | Open | BeFit Paltim, Strada Jiul 1 (Paltim). Added to OSM 2026-11-18. | Keep (name could say "Paltim") | High |
| 13 | n6529801465 | Iguana | Open | Iguana Sagului, Strada Gheorghe Ranetti 28 (by Carrefour Market). ~60 m. | Keep (name could say "Sagului") | High |
| 14 | n11383257541 | Stay Fit Gym | Open | Stay Fit Moșnița, in FunShop Retail Park (str. Florentina). | Keep | High |
| 15 | n4578303549 | Extreme Fitness | Open | Str. Mureș 132, near Lidl Calea Martirilor (Wellhub listing, extremefit.ro is up). | Keep | Medium |
| 16 | n14391900791 | *(no name)* | **Not open yet** | Added 2026-11-18, ~130 m from World Class City of Mara (Calea Circumvalațiunii 1). World Class's presale rules give an estimated opening of **8 Jan 2027**. | Leave for now; recheck in January (it would show as "Unnamed gym" meanwhile) | Medium |
| 17 | n6705087878 | Continental Fitness Spa | Unsure | Hotel Continental is now "North Star Continental Resort"; booking sites still list its spa and gym, nothing says it closed. A hotel gym, may be guests-only. | Keep | Low |
| 18 | n4371190992 | Fit Force One Gym | Unsure | OSM: Str. Traian Lalescu, by the 2MV student dorm. Nothing found on the web. OSM entry last edited 2018. | Keep; needs a local check | Low |
| 19 | n12328803373 | Fitness Planet | Unsure | Strada Panselelor area (Șagului). Nothing found on the web. | Keep; needs a local check | Low |
| 20 | n11881818750 | Play Sports Club | Unsure | Strada Octavian Goga, Dumbrăvița. Nothing found on the web. | Keep; needs a local check | Low |
| 21 | n14338274101 | DHP Gym | Unsure | Strada Transilvania 3, Moșnița Veche. OSM lists dhpgym.ro, which doesn't resolve. Added to OSM 2026-11-01, so likely recent. | Keep | Low |
| 22 | w109687449 | Clubul Sportiv Timpolis | Unsure | A sports club building on Str. Dr. Alexandru Marta (Bastion). Unclear whether it has a gym open to the public. | Keep | Low |
| 23 | n13321949057 | YogaShala Shangri La | **Not a gym** | A yoga studio. | Question for you (keep or hide) | Medium |
| 24 | w229395714 | *(no name)* | Unsure | A building at Str. Dr. Grigore T. Popa 73 (Aradului). No name in OSM, nothing found. | Keep; needs a local check | Low |

## B. Gyms missing from OpenStreetMap

| # | Gym | Address | Source | Coordinates (Nominatim) | Pin | Confidence |
|---|---|---|---|---|---|---|
| B1 | Gym One 1 | Strada Varșovia 3, Dumbrăvița | [gymone.ro/locatii/gym-one-1](https://gymone.ro/locatii/gym-one-1) | **45.78637, 21.23570** (centre of the building) | House. **Added on OSM 2026-11-25** as building way 469907683 (changeset 192448168) | High. None left: the import adds it |
| B2 | Gym One 2 | Str. Mureș 116A | gymone.ro, Wellhub | 45.72862, 21.23306 | House | High |
| B3 | Gym One 4 (the site calls it "Gym One 4 Fit & Spa"; same gym) | Strada Memorandului 76-80 (OSM: 78) | [gymone4spa.ro](https://gymone4spa.ro), gymone.ro | **45.74091, 21.23332** (centre of the building) | House. **Added on OSM 2026-11-25** as building way 1584494181 (changeset 192449243). The street-level estimate was ~330 m off | High. None left: the import adds it |
| B4 | World Class Shopping City | Calea Șagului 100 (Shopping City Timișoara) | [worldclass.ro club list](https://www.worldclass.ro/harta-cluburi/?city=Timisoara) | 45.72466, 21.19870 | House (the mall; ~200 m from World Class's own pin, so the mall is big) | High |
| B5 | BeFit Hanover | Bd. Cetății 5 | [befit.ro](https://befit.ro/cluburi-befit/timisoara-hanover) | 45.76055, 21.20946 | House | High |
| B6 | BeFit Modern | Bd. Simion Bărnuțiu 56 (upstairs) | [befit.ro](https://befit.ro/cluburi-befit/timisoara-modern) | 45.76410, 21.25818 | House | High |
| B7 | Iguana Modern | Bd. Simion Bărnuțiu 49 | [iguanafitness.ro](https://www.iguanafitness.ro), Wellhub | 45.76406, 21.25607 | House | High |
| B8 | Stay Fit Central | Str. Carol Telbisz 3 (Bega Center) | [stayfit.ro](https://stayfit.ro/timisoara/central-timisoara/) | 45.75543, 21.23066 | House | High |
| B9 | Stay Fit Nord | Calea Aradului 56A (Auchan Nord) | [stayfit.ro](https://stayfit.ro/timisoara/nord-timisoara/) | 45.78558, 21.22149 | House (the Auchan) | High |
| B10 | Stay Fit Giroc | Calea Timișoarei 13, Giroc (Glass Gallery) | [stayfit.ro](https://stayfit.ro/timisoara/giroc/) | 45.70846, 21.23317 | House (Glass Gallery) | High |
| B11 | 27Fit | Calea Stan Vidrighin 14 | [Wellhub](https://wellhub.com/ro-ro/search/partners/27fit-timisoara/) | 45.74362, 21.25424 | House | Medium |
| B12 | Banu Sport | Aleea F. C. Ripensia 33 (by the Dan Păltinișanu stadium) | [Wellhub](https://wellhub.com/ro-ro/search/partners/banu-sport/) | 45.74887, 21.23753 | Street | Medium |
| B13 | Alma Libre Body & Soul | Str. Mareșal Constantin Prezan 120A (from the developer) | [Wellhub](https://wellhub.com/ro-ro/search/partners/alma-libre-body-soul/) | ~45.7310, 21.2415 (estimate) | **Estimate**: no. 120A isn't in OSM; it should be between Penny (108-110) and no. 132, but the numbers on this street aren't in order. Off by up to ~200 m | High that it exists; pin needs a check |
| B14 | World Class City of Mara | Calea Circumvalațiunii 1 | worldclass.ro presale rules | (see A16) | n/a | **Not open until ~8 Jan 2027**; don't add yet |

Seen but left out (not clearly a gym open to the public, or no address): Friends Arena (Strada Lacului 1, a sports
venue), Daf Junior Sport (Giroc, children's sport), MC Gym Motion (Bd. Iuliu Maniu, no house number), Just Dance
(a dance school), CrossFit Timișoara (listed on crossfit.com, no address found).

## Summary

- **24** OSM gyms: **12** confirmed open as listed, **2** open under another name (Smartfit Studio 2 → World Class
  Buziașului, World Class → BeFit VOX), **1** moved (Gym One → Gym One 1 in Dumbrăvița), **1** not open yet
  (City of Mara), **1** not a gym (yoga studio), **7** unsure (little or nothing on the web).
- **13** gyms missing, 10 of them chain clubs: Gym One is missing 3 of its 6 clubs, Stay Fit 3 of 4, BeFit 2 of 5.
- **3** of the added gyms (Gym One 1, Gym One 4 SPA, Alma Libre) only have a street-level pin, which isn't
  precise enough for the 150 m check.

## Decisions (2026-11-25)

1. **Renames:** yes. The override layer gets a **rename** that keeps the OSM id (A8 → BeFit VOX, A9 → World Class
   Buziașului), so past workouts keep pointing to the same gym. Added to section 0b of `docs/FUTURE_28_BACKLOG.md`.
2. **Street-level pins** (B1 Gym One 1, B3 Gym One 4, B13 Alma Libre): the developer gave the house numbers
   (Varșovia 3, Memorandului 76, Prezan 120A; "Gym One 4 SPA" and "Gym One 4" are the same gym). OSM has none of the
   three numbers, so the pins above are still estimates, not good enough for the 150 m check. These three stay out
   until each has an exact pin (coordinates from openstreetmap.org, see below).
3. **Yoga studio** (A23): hide.
4. **Unsure small gyms** (A17 to A22, A24): keep as they are.
5. **OpenStreetMap:** the developer may make the fixes there too; how to, and how it fits with the override layer:
   `docs/OSM_EDITING_GUIDE.md`.

Everything else follows the "Proposed" column. Still to do: the three exact pins, then build 0b.

**Getting an exact pin:** open <https://www.openstreetmap.org>, zoom in on the gym's building (switch the layer to
aerial imagery if the building isn't drawn), right-click its entrance and choose **Show address**. The panel on the
left starts with the coordinates (for example `45.78650, 21.23550`); paste those here. Don't take them from Google
Maps.

## Questions for the developer (answered above)

1. **Renames.** Section 0b only plans *hide* and *add*. Two gyms changed owner but stayed in the same place. Should
   the layer get a **rename** (keep the OSM id, so past workouts keep pointing to it), or should they be done as
   hide + add?
2. **Street-level pins** (B1, B3, B13): do you know these places, or should I leave them out until someone checks?
3. **Yoga studio** (A23): hide, or keep it since people may train there?
4. **Unsure small gyms** (A17 to A22, A24): keep them as they are (my suggestion), or check them on site?
5. **OpenStreetMap itself:** the fixes above could also go to OpenStreetMap, with an account of your own, so every
   app gets them and the weekly import picks them up. That's your call; I haven't edited anything there.
