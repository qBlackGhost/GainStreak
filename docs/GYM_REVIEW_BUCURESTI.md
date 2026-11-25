# Gym review: București (0.28 section 0c, first pass: the chains)

Working file, **not committed until reviewed**. Nothing in any database was changed.

- **OSM data:** `leisure=fitness_centre` in the box 44.33,25.95 – 44.55,26.25 (București plus Voluntari, Otopeni,
  Popești-Leordeni, Bragadiru), read from the public Overpass server on 2026-11-25: **131 gyms** (6 without a name).
- **Checked against:** the chains' own club lists: World Class (its own club JSON, 33 București-area clubs), Stay Fit
  (`stayfit.ro/bucuresti/<club>`, one page per club), BeFit (6 clubs), Columbia Fitness (4 clubs). No Google Maps data.
- **Method:** each club's official address was matched to the OSM gym with the same street and number; where OSM has no
  number, the nearest OSM gym by Nominatim geocode was used. A **geocode is only a hint** (most came back at street
  level); none of the "missing" pins below is good enough for the 150 m check until it is placed on the building.
- **Not done yet:** the ~60 independent gyms (no chain site to check against). They need web search one by one, see
  the end.

## 1. World Class (33 clubs on worldclass.ro, 35 gyms in OSM)

29 clubs match an OSM gym by address (Downtown, Charles de Gaulle, Grand, AFI Tech, AFI Cotroceni, One Cotroceni,
Campus 6, Lujerului, Plaza, Militari, Jiului, Promenada, America House, Expo, Oregon Park, Caro, Veranda, Jolie Ville,
Atlantis, Mega Mall, Titan, Titan Park, ParkLake, InCity, București Mall, Asmita Gardens, Otopeni, Upground, Sudului).

| Item | OSM id | What | Proposed | Confidence |
|---|---|---|---|---|
| Sema Park | none | Splaiul Independenței 319, clădirea E2. Not in OSM (the nearest gym is "Daso Cado Regie" at 315B, a different gym). | **Add** (needs an exact pin) | High that it exists |
| Planet | none | Str. Emil Racoviță 1, Pipera (Voluntari). Not in OSM. | **Add** (exact pin needed) | High |
| One High District | w1579475787? | Șos. Fabrica de Glucoză 14. An unnamed "World Class" building (7 levels, 44.4774, 26.1243) sits ~500 m east of Upground; probably this one. | Keep; check on the map | Medium |
| Cosmopolis | w1208539328? | Centura București, Ștefăneștii de Jos. A "World Class" building without address at 44.5369, 26.1692 is the only candidate. | Keep; check | Low |
| World Class "Le Club" | n7009390826 | Str. Aron Cotruș 20 (44.4854, 26.0874). **Not on World Class's club list.** Tagged `branch=Le Club`, surveyed. | Unsure: maybe closed or members-only; check | Low |
| Names | several | World Class's own names would help ("World Class Titan", "…Plaza", …); the OSM names are all just "World Class". | Optional rename | n/a |

## 2. Stay Fit (about 33 clubs listed for București and Ilfov, 12 gyms in OSM)

In OSM and matching an official club: Teiul Doamnei 35A, Domenii (n8026566916, ~70 m), Lemon Park (Voluntari,
n11398454547), Colosseum / Chitilei 284, Grand Arena / Metalurgiei 12-18, Ghencea, Popești Vest 37, Otopeni /
Odăii 42, Metalurgiei 132 (n14114803103 "StayFitGym", ~350 m from the geocode, Medium).

OSM entries that match **no** club on stayfit.ro: **Progresului 151-171** (n6782638077, `oh` set) and **n8828063879**
(44.4845, 26.1197, near Fitclub Factory). Both: unsure, maybe closed or another brand; check.

**Missing from OSM** (club, official address; all High confidence that it exists, pins are geocode hints only):

| Club | Address | Geocode hint |
|---|---|---|
| Cocor | Bd. Ion C. Brătianu 29-33 (Cocor mall) | 44.4347, 26.1030 (street) |
| Crângași | Bd. Constructorilor 16A (Auchan Crângași) | 44.4578, 26.0404 |
| Fizicienilor | Str. Fizicienilor 1 | 44.4127, 26.1503 |
| Fundeni | Șos. Fundeni 159 (Sir Fundeni) | 44.4632, 26.1539 (place) |
| Esplanada | Șos. Vergului 20 (former Cora Pantelimon) | 44.4374, 26.1866 |
| Jiului / Marmura | Bd. Bucureștii Noi 25 (Marmura Residence) | 44.4791, 26.0431 |
| Pallady | Bd. Theodor Pallady 51N | 44.4080, 26.1919 (street) |
| Pantelimon | Bd. Chișinău 1 | 44.4451, 26.1585 |
| Petre Ispirescu | Str. Petre Ispirescu 86 (Coral Center) | 44.4152, 26.0617 |
| Prosper | Calea 13 Septembrie 221-225 (Prosper Plaza) | 44.4200, 26.0657 |
| Romana | Calea Dorobanți 5-7 (Sheraton, mezanin) | 44.4466, 26.0989 (~500 m from Sky Fitness Romana, a different gym) |
| Titulescu | Șos. Nicolae Titulescu 171 | 44.4528, 26.0731 (street) |
| Valea Cascadelor | Str. Valea Cascadelor 11B | 44.4301, 26.0039 |
| Vitan | Str. Răcari 57 | 44.4132, 26.1382 |
| Supernova Alexandriei | Șos. Alexandriei 152 | 44.3995, 26.0440 |
| Dorobanți | Str. Loc. Av. Radu Beller 5-7 | 44.4611, 26.0945 |
| Domenii Business Park | Str. Copilului 20 | 44.4646, 26.0615 |
| Pipera Nord | Șos. Pipera 46H, clădirea C6 | 44.4831, 26.1097 |
| Pipera (Iride) | Bd. Dimitrie Pompei 9-9A (Iride Business Park) | not found |
| Voluntari (Gheata) | Bd. Voluntari 72 | 44.4867, 26.1802 (street) |
| Ștefănești, Balotești, Chitila, Crevedia | Ilfov villages | outside the app's city; skip for now |
| Ozana | address not found on its page | needs a look |

## 3. BeFit (6 clubs, 4 in OSM)

In OSM: Olteniței 4A (n13800632736, "Eroii Revoluției"), Ilioara 54 (n13800541348, "MPark Titan"), Pipera Plaza
(n13800621277, București Nord 14), Popești-Leordeni (n13800643176, Olteniței 27B). All four are named just "BeFIT"
(or "BeFIT Gym"); the Timișoara review's naming advice applies.

Missing: **BeFit Giurgiului** (Șos. Giurgiului 59-61) and **BeFit Timișoara** (Bd. Timișoara 14a, 44.4288, 26.0422 as
a building-level geocode, ~215 m from "GymHouse", a different gym). Both High.

## 4. Columbia Fitness (4 clubs on the site: Orhideea, Mihai Bravu, Militari, Bragadiru)

All 4 are in OSM (n6793227019 Orhideea, n13818708340 Mihai Bravu, n11229751137 Militari with no address, n13818703695
Bragadiru). **Keep**, High. Militari's entry has no name beyond "Columbia Fitness".

## 5. Counts so far

| Chain | Official clubs | In OSM | Missing |
|---|---|---|---|
| World Class | 33 | 31 (29 sure) | 2 to add (Sema Park, Planet), 2 unsure |
| Stay Fit | ~33 (about 24 in or next to the city) | 9 sure | **about 19** in the city, plus Ilfov |
| BeFit | 6 | 4 | 2 |
| Columbia | 4 | 4 | 0 |

Stay Fit is the big hole: **OSM has about a third of its Bucharest clubs**.

## 5b. More chains (own sites, 2026-11-25)

| Chain | Official clubs | In OSM | Missing / change |
|---|---|---|---|
| **Level Up** (formerly Sweat, MedLife; level-up.ro) | The Light (Bd. Iuliu Maniu 6Q, et. 11), Promenada (Str. Barbu Văcărescu 313-315), Tineretului (Calea Șerban Vodă 206-208), Arcul de Triumf (Str. Clucerului 78-80), Drumul Taberei (Bd. Drumul Taberei 115), Vitan (new, address not on the page); West Gate and Green Gate are presale; a news item also names Bd. Râmnicu Sărat 41 (Bobocica) | 1: n11508840140 "Sweat" at Iuliu Maniu 6Q | **Rename** that one to Level Up The Light (keeps the id). **Add** Promenada, Tineretului, Arcul de Triumf, Drumul Taberei; Vitan and Râmnicu Sărat once an address is confirmed. Don't add West Gate / Green Gate until open. |
| **Downtown Fitness** (downtownfitness.ro) | 5: Obor, Mihalache, Mihai Bravu, Vitan, Matei Basarab & SPA. The site shows Calea Vitan 13-19, Str. Laborator 14 and Aleea Câmpul Moșilor (Obor); I could not tell which of the other four clubs the first two belong to. | 1 (Obor, n9499379713) | **Add** 4; street addresses for Mihai Bravu, Mihalache and Matei Basarab still to read from the site |
| **KOM Fitness** (komfitness.ro) | 5: Big (Nițu Vasile 9, Piața Sudului), Metalurgiei (Bd. Metalurgiei 78), Dream Town (Amurgului 55), Fitness & Spa (Popești Vest 13), Electromagnetica (Calea Rahovei 266-268) | 4 (n12758226981, n12925628892, n10258067350, w573714246) | **Add** Electromagnetica; the OSM names are inconsistent ("KomFitness II" = Dream Town) |
| **Sas Gym** | site unreachable; not verified | 2 | unsure |
| **Nr1 Fitness, Be Live, Sky Fitness, Max Fitness, Clever Fit** | their sites were unreachable or have no list; web search found nothing usable | several | unsure; need another source or a look |

## 6. Independents and noise (not checked yet)

- **Not gyms** (hide candidates): Pilates and yoga studios (Body Tailors Pilates, Luma Pilates Studio, Pilates House
  Morarilor, Pilates Rocks Studio, Shaya Pilates, Studio 35 Pilates), Zumba Fitness, Magic Dance, Cemtrul Ananda,
  Power Plate, "Sala de antrenament pentru box" (a building, w450195002).
- **Duplicates:** n12109781163 "Fitness Evolution" and the unnamed building w1307423669 are at the same spot
  (44.4410, 26.1567); "Fitology" ×2 (n12856986014 Unirii 18 and n11619043395 at 44.4428, 26.1183, ~4 km apart, so
  not a duplicate, just two); "Infinity Gym" ×2 and "nr1 fitness" ×2 are different places.
- **Other chains seen, not on the first pass:** Sas Gym ×2 (Drumul Taberei 89B, Piața Alba Iulia), KOM Fitness ×4
  (Berceni), Sky Fitness Romana, Downtown Fitness Obor, Be Live Fitness ×2, Max Fitness Sud, Nr1 Fitness. Next: their
  own sites.
- **No name:** n6484902509 (Pache Protopopescu 30), n11735416869, w692336082, w972906687, w1568673178: each needs a
  look.
- **Famous names not seen in OSM at all** (to look up): Gold's Gym, Pure Fitness, 18 Gym is there, Fitness One, Smart
  Gym; and the small gyms with only a Facebook page, which are the weak spot as in Timișoara.

## Questions for the developer

1. Bucharest is ~5 times Timișoara. Do the independents one by one with web search (slow, mostly "unsure"), or only
   fix the chains first (Stay Fit's 19 missing clubs alone would help most users) and leave the independents?
2. The missing clubs need exact pins before the override layer or OSM can take them (section "Getting an exact pin" of
   the Timișoara review). Do you want to do the OSM edits for Stay Fit yourself (`docs/OSM_EDITING_GUIDE.md`), or give
   me the 150 m check as the rule and I place them with the best geocode?
