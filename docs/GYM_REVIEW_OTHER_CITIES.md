# Gym review: the other big cities (0.28 section 0c, chains pass)

Working file, **not committed until reviewed**. Nothing in any database was changed. Same method and caveats as
`docs/GYM_REVIEW_BUCURESTI.md`: official club lists of the chains compared with OSM `leisure=fitness_centre`, read from
Overpass on 2026-11-25. Sources: World Class's own club JSON (`/wp-json/clubs/v1/club`), `stayfit.ro/<city>/<club>`
pages, `befit.ro` club pages. **Matching is mostly by brand and count**: the geocoder puts malls and retail parks on a
street, not a building, so "OSM has it" below means "an OSM gym of that brand is plausibly the same", and any pin for a
missing club is a hint only (150 m check, see the Timișoara review).

| City | OSM gyms | World Class (official / in OSM) | Stay Fit (official / in OSM) | BeFit | Missing from OSM (chains) |
|---|---|---|---|---|---|
| Cluj-Napoca | 51 | 3 / 4 (2 to check) | 3 / 1 | none | 2 (both in Florești, outside the city) |
| Iași | 21 | 2 / 1 | 10 / 2 | none | **9** |
| Constanța | 8 | 1 / 1 | 2 / 0 | none | 2 |
| Brașov | 16 | 1 / 1 | 3 / 2 | 2 / 0 | 3 |
| Ploiești | 9 | 1 / 1 | 4 / 2 | none | 2 |
| Craiova | 2 | none | 2 / 0 | none | 2 (OSM has only 2 gyms in the whole city) |

Overpass boxes: Cluj 46.70,23.50 – 46.85,23.72; Iași 47.09,27.48 – 47.22,27.68; Constanța 44.10,28.50 – 44.28,28.72;
Brașov 45.58,25.50 – 45.72,25.68; Ploiești 44.88,25.95 – 44.98,26.10; Craiova 44.28,23.72 – 44.38,23.88.

## Cluj-Napoca

- **World Class:** Iulius Cluj (Vaida Voievod 53-55) and Belvedere (Călărașilor 1) match n2045802138 and n2950210933,
  High. **Record Cluj** (Str. Scortariilor 10, behind The Office) matches neither; OSM has two more "World Class"
  entries, w800361398 (Str. Onisifor Ghibu 20A) and n6362393601 (no address). One of them may be Record; the other
  may be a closed or private club. Check both on the map.
- **Stay Fit:** official Central (Centrul Comercial Central, Regele Ferdinand 22-26), Florești (Avram Iancu 454) and
  Urbano (Luna de Sus). OSM has one, n11546246498 (no address; probably Central). **Missing: Florești, Urbano**, both in
  Florești commune.
- **18Gym** is a Cluj chain with **9 gyms in OSM** (Tășnad 4, Dunării 22, Gheorghieni, Fabricii 11-13, Iris,
  21 Decembrie 95-97, Dorobanților 105, Pascaly 6, one unnamed). Not verified: 18gym's site wasn't checked.
  Likewise Burn Fitness (4), RevoGym (3), Gold Gym. Next pass.
- **Not gyms:** merryZdance, Alverna Spa, Runa Pilates, Reshape Studio, Bombshell (probable), "Sală fitness angajați
  Digi" (a company gym, not open to the public: hide).

## Iași

- **World Class:** Mall Moldova (Șos. Păcurari 121) and Iași (Str. Anastasie Panu 31). OSM has one, n12778454975 (no
  address, no name beyond the brand). **One missing** (which one depends on where n12778454975 is; check). Note OSM's
  Vivertine Gym Main is at Anastasie Panu 26, close to the second club but a different gym.
- **Stay Fit: 10 official clubs**: Iulius Mall, Miroslava, Păcurari, Palas Campus, Palas Mall, Silk, Tomești, Valea
  Adâncă, Valea Lupului, Vișan (Bârnova). OSM has two: n11079103662 (Iancu Bacalu: Palas Campus, ~25 m, High) and
  n14169913941 (probably Miroslava). **About 8 missing**; the ones inside Iași proper: Iulius Mall (Bd. Tudor
  Vladimirescu), Păcurari (Luca Arbore 2), Palas Mall (Palas 7A), Silk (Calea Chișinăului 22). The rest are in Miroslava,
  Valea Lupului, Tomești, Bârnova communes.
- **Other:** two "Body Line Gym", two "Garage Workout" and two "Metalmorphose" entries are probably branches, not
  duplicates; not checked.

## Constanța

- **World Class City Park** (Bd. Alexandru Lăpușneanu 116C): OSM n7124646879 (no address) is probably it; Medium.
- **Stay Fit: Lake View** (Bd. Mamaia 283C) **and Supernova** (Str. Cumpenei 2): neither found in OSM. **Both missing.**
  OSM has two unnamed gyms (n11155938754, w707588966) 1.6 to 2 km from them; check what they are.
- OSM has only 8 gyms in Constanța, with a yoga studio among them (One Yoga Studio: hide candidate): the city is
  barely mapped, so the independents would add more here than anywhere else.

## Brașov

- **World Class AFI** (Bd. 15 Noiembrie 78): n8227784358 matches by address, High.
- **Stay Fit:** Bartolomeu (Str. Institutului 8B), Craiter (Fundătura Harmanului 2), Tractorul (Ioan Popasu 52). OSM has
  n13069163380 "StayFitGym" (~235 m from the Tractorul geocode, Medium) and n13897763549 (no address). **One missing**
  unless that one is Bartolomeu or Craiter.
- **BeFit:** Saturn (Bd. Saturn 32) and Sânpetru (Str. Sfântu Ștefan 9, Sânpetru commune). **Both missing.**

## Ploiești

- **World Class** (Calomfirescu 2, AFI Palace): n2688264923 matches by address, High.
- **Stay Fit:** official Nord (Găgeni, FunShop Park), SFG43 (Piața 1 Decembrie 1918 nr 1), Vest (Târgoviștei 4) and
  Central Black (address not on the page). OSM has two (n12902096267, n13664508446, no addresses). **About 2 missing**;
  check where the two OSM ones are before adding.

## Craiova

- **Stay Fit Mercur** (Calea Unirii 14) and **Promenada** (Calea Severinului 61): neither in OSM. The only gyms OSM has
  are an unnamed one at Calea Severinului 2E and Fitness Club Relaxsim. **Both missing.** Craiova is a "start from
  nothing" city: independents would be the main work there.

## Not done

- Independents in all six cities (Cluj alone has ~40 unverified entries).
- Chains local to one city (18Gym, Burn Fitness, Revo, Body Line, Metalmorphose, Oxygen, Grand Fitness, Spartan).
- Other big cities: Timișoara done separately; not started: Oradea, Sibiu, Galați, Bacău, Pitești, Arad, Târgu Mureș.
