# Food catalog tool

Builds `foods.db`, the USDA catalog GainStreak bundles (Phase 26), from the FoodData Central CSV downloads.

1. Download **Foundation Foods** and **SR Legacy** (both **CSV**) from https://fdc.nal.usda.gov/download-datasets and unzip
   each into its own folder. Keep them outside the repo.
2. Run:

```bash
dotnet run --project tools/food-catalog -- <foundation-folder> <sr-legacy-folder> [output foods.db]
```

What it keeps: foods of type `foundation_food` and `sr_legacy_food` that have energy (kcal; else Atwater kcal; else
kJ / 4.184), protein, carbs and fat per 100 g, and the household portions (name and grams). It writes an FTS5 index on
the name (`food_fts`, prefix search) and records `catalog_version` and `schema_version` in `meta`. It also writes
`foods.version` (the catalog version as text) next to it. Put both in `src/GymApp.Services/Catalog/`: they are embedded
in `GymApp.Services`, and the app extracts the catalog again when the version changes.

Current build (Foundation 2026-10-29 + SR Legacy 2018-04): 8,223 foods, 2.3 MB.
