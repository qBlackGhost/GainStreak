# Gym review: 18GYM (national chain, 0.28 section 0c)

Working file, **not committed until reviewed**. Nothing in any database was changed.

- **Source:** 18gym.ro's own store locator (`/wp-admin/admin-ajax.php?action=asl_load_stores`): **48 clubs** with address,
  phone and coordinates. The coordinates are the chain's own, so they are better pins than a geocoder's street hint
  (still, check each on the aerial imagery before it goes into OSM; never copy from Google Maps).
- **Checked against:** the OSM data already pulled for the cities (`docs/GYM_REVIEW_BUCURESTI.md`,
  `docs/GYM_REVIEW_OTHER_CITIES.md`): Cluj-Napoca, București, Brașov, Iași, Constanța. Overpass was down for the rest, so
  the other 18GYM towns (Timișoara, Oradea, Sibiu, Târgu Mureș ×5, Arad, Bacău, Baia Mare, Bistrița, Alba Iulia, Satu Mare,
  Piatra Neamț, Turda, Câmpia Turzii, Mediaș, Iernut, Luduș) are **not checked**; run them when Overpass is back.
- **Matching:** an OSM gym within ~100 m of the chain's own coordinates, with the brand in its name, counts as the same club.

## Cluj-Napoca (18 clubs on the site, the chain's home city)

| Club | OSM | Verdict |
|---|---|---|
| Calea Turzii (Hexagon Office) | n8708751464 "18 Gym" (90 m) | In OSM |
| Gheorgheni Interservisan (Pascaly 2a) | w179900178 (10 m) | In OSM |
| Gheorgheni Mercur (Aleea Detunata 1) | n7076876788 (19 m) | In OSM |
| Iris (Aushopping Iris) | n10248329103 (24 m) | In OSM |
| Mănăștur (Str. Taberei 4) | n1631997565 "18Gym" at Str. Tășnad 4 (58 m) | In OSM; its OSM address says Tășnad, check |
| Mărăști 21 Decembrie (95-97) | n10821229159 (86 m) | In OSM |
| Mărăști Fabricii (13) | n9913899182 (45 m) | In OSM |
| Calea Dorobanților (105) | n11515789505 "18Gym" at Calea Dorobanților 105 | In OSM (the chain's pin is ~220 m away from it) |
| **Grigorescu** (Str. Miraslău 4) | n10638131383 named **"Fortuna"** (7 m) | **Rename** to 18GYM Grigorescu: the chain's site still lists a "Fortuna" club at this place. Medium, check |
| **Park Lake** (Str. Galați 6) | n10035781460 "Reshape Studio" (36 m) | Probably **missing**: Reshape Studio is a different gym in the same mall. Medium |
| **Aurel Vlaicu**, **Bună Ziua**, **Edgar Quinet**, **Era**, **Fabrica de Sport**, **Via**, **Zorilor**, **Florești** | none | **Missing (8)**, pins below |
| OSM "18Gym" at **Str. Dunării 22** (n4124269889) | none of the 18 clubs | **Not on the chain's list**: closed or renamed. Check before hiding |

## București (8 clubs; the biggest finding is a wrong brand in OSM)

- **Calea Rahovei** (the site says Str. Progresului 151-171, Complex Jumbo, etaj 1): OSM n6782638077 sits 79 m
  from the chain's pin but is named **"Stay Fit Gym"**. Stay Fit has no club there (it was the unmatched entry in the
  București review), so this is the **18GYM**: **rename** Stay Fit Gym → 18GYM Calea Rahovei. High.
- **Chiajna** (Sat Dudu, Șos. Ilie Petre 87, Rotar): OSM n12282332079 "18 Gym", 29 m. In OSM.
- **Lujerului** (Șos. Virtuții 1): OSM n12057549622 "Zumba Fitness", 39 m. Probably a mislabelled or different place;
  Medium. Look at it before adding or renaming.
- **Missing:** Mihai Bravu, Monaco Towers, Otopeni, Pantelimon, Pipera Voluntari (pins below). The nearest OSM gym to
  Pipera Voluntari is the unmatched "Stay Fit Gym" n8828063879 (488 m), too far to be it.

## Other cities checked

- **Brașov (4 clubs):** none in OSM. Astra, Coresi, Unirea Mall and Zizinului are **all missing** (no Zizinului address in
  the locator; its page exists).
- **Iași, Hala Centrală** (Str. Anastasie Panu 46): nearest OSM gym is "Club Armony" (224 m), a different gym. **Missing.**
- **Constanța, Tomis Mall** (Ștefan cel Mare 36-40): **missing**; nearest is Oxygen Club (830 m).

## Pins from the chain's own locator, for the missing clubs

| City | Club | Address | Pin | Phone |
|---|---|---|---|---|
| Cluj-Napoca | Aurel Vlaicu | Str. Aurel Vlaicu 25 | 46.78097, 23.62561 | 0771 639 006 |
| Cluj-Napoca | Bună Ziua | Str. Erkel Ferenc 2-4 | 46.74885, 23.59842 | 0740 625 621 |
| Cluj-Napoca | Edgar Quinet | Str. Edgar Quinet 1-3 | 46.74334, 23.56600 | |
| Cluj-Napoca | Era | Str. Fabrica de Chibrituri 9 | 46.78563, 23.58671 | 0774 286 248 |
| Cluj-Napoca | Fabrica de Sport | Str. Tudor Vladimirescu 22 | 46.78282, 23.56173 | 0774 458 093 |
| Cluj-Napoca | Park Lake | Str. Galați 6 | 46.77468, 23.62918 | 0770 344 363 |
| Cluj-Napoca | Via | Str. Colinei 2 | 46.75374, 23.53678 | 0773 521 510 |
| Cluj-Napoca | Zorilor | Str. Observatorului 109 | 46.75382, 23.58541 | 0771 343 570 |
| Cluj-Napoca | Florești | Str. Eroilor 17 (Roka) | 46.73850, 23.48415 | 0774 612 044 |
| București | Lujerului | Șos. Virtuții 1 | 44.43572, 26.03445 | 0774 237 446 |
| București | Mihai Bravu | Str. Răcari 5 (behind Kaufland) | 44.41570, 26.13656 | 0774 587 401 |
| București | Monaco Towers | Șos. Berceni 96, et. 1 | 44.37127, 26.14033 | 0723 047 693 |
| București | Otopeni | Str. 23 August 229 | 44.55333, 26.10641 | |
| București | Pantelimon | Bd. Chișinău 1, et. 1 (Complex Delfinului) | 44.44516, 26.15792 | 0770 181 818 |
| București | Pipera Voluntari | Bd. Pipera 2-XI | 44.48894, 26.11960 | 0773 729 496 |
| Brașov | Astra | Str. Ferecz Szemler 12-14 | 45.64622, 25.62831 | 0757 181 819 |
| Brașov | Coresi | Str. Tudor Arghezi 2 | 45.66471, 25.61915 | 0774 798 341 |
| Brașov | Unirea Mall | Bd. Gării 3A | 45.66131, 25.61055 | 0732 744 453 |
| Iași | Hala Centrală | Str. A. Panu 46 | 47.16146, 27.59044 | 0744 384 287 |
| Constanța | Tomis Mall | Str. Ștefan cel Mare 36-40 | 44.17841, 28.64669 | 0774 550 619 |

**Totals for the checked cities:** 18GYM has 36 clubs in Cluj, București, Brașov, Iași and Constanța; OSM has about 10
of them right, 2 under the wrong name, and about 20 missing. In the review of all the chains so far, 18GYM, Stay Fit
and Downtown Fitness are the biggest gaps.
