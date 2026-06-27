# MapLightBaker

An **offline** lightmap baker for Valve-220 `.map` files (the format TrenchBroom
emits). It reads brush geometry and `light_omni` entities, bakes direct lighting
with hard or soft shadows via CPU raytracing, and writes:

- `<map>_lightmap.png` — the lightmap atlas
- `<map>_lightmap.json` — per-face atlas UV rects + per-vertex lightmap UVs

It runs as a content-pipeline step: edit your map, run the tool, ship the two
output files next to the map. Nothing native, nothing at runtime — pure C#.

## Build & run

Requires the .NET 8 SDK.

```bash
cd MapLightBaker
dotnet run -c Release -- path/to/zs-bunker-nowalls.map
```

Output lands next to the map by default (override with `--out`).

## Options

| Flag | Default | Meaning |
|------|---------|---------|
| `--out <dir>` | map's folder | output directory |
| `--texels <f>` | 0.25 | texels per world unit (0.25 = 4 units/texel, coarse/HL2-ish). Higher = sharper + bigger atlas + slower |
| `--atlas <int>` | 1024 | atlas width in px (height grows automatically) |
| `--range-scale <f>` | 200 | world reach per `light_radius` unit |
| `--energy-scale <f>` | 4 | brightness per `light_energy` unit |
| `--ambient <f>` | 0.08 | flat ambient floor so shadows aren't pure black |
| `--shadow-samples <n>` | 1 | 1 = hard shadows; >1 = soft (e.g. 16) |
| `--light-size <f>` | 8 | soft-shadow light radius (only used when samples > 1) |

### Tuning for your map

Your `light_omni` entities use Godot values (`light_radius` 3, `light_energy` .8)
that are tiny next to Quake-unit geometry, so the tool scales them. If the bake
comes out too dark or too bright, adjust `--energy-scale`; if lights don't reach
far enough or bleed too far, adjust `--range-scale`. Start with the defaults,
look at the PNG, iterate. These two knobs are the main thing you'll touch.

For a first look, hard shadows (`--shadow-samples 1`) bake fastest. For the final
asset, `--shadow-samples 16 --light-size 8` gives soft penumbrae.

## What it skips

Faces with textures containing `glass`, `trigger`, `clip`, `skip`, `nodraw`,
`sky`, or `origin` don't get lightmap texels and don't cast shadows. Edit
`IsSkipped` in `Program.cs` to match your texture conventions.

## Loader-side integration (Raylib / C#)

The JSON gives you everything to apply the atlas:

```jsonc
{
  "atlasWidth": 1024,
  "atlasHeight": 768,
  "faces": [
    {
      "faceId": 42,
      "texture": "horror/stone_brick_02",
      "atlasRect": [128, 64, 40, 24],
      "verts": [
        { "pos": [x,y,z], "uv": [u,v] },   // uv is the lightmap UV (0..1, atlas space)
        ...
      ]
    }
  ]
}
```

In your existing loader, when you build the mesh for a face, attach the matching
`uv` values as a **second UV set** (Raylib's `texcoords2` / `MATERIAL_MAP_*`
secondary channel). Then in your world shader:

```glsl
vec3 albedo   = texture(albedoMap,  fragTexCoord ).rgb;  // your existing UVs
vec3 lighting = texture(lightmap,    fragTexCoord2).rgb;  // baked UVs from JSON
fragColor = vec4(albedo * lighting, 1.0);
```

Match faces by `faceId` — emit faces from your loader in the same order the baker
does (it walks brushes in file order, all entities, skipping `IsSkipped`
textures), or match on `pos` if your ordering differs. The safest cross-check is
position: the `verts[].pos` are exact world coordinates of the face winding.

### Important: face ordering

The baker assigns `faceId` by walking every brush in file order and incrementing
per face (including skipped faces, which are then dropped). If your loader's face
order matches, `faceId` lines up directly. If not, match faces by their winding
positions instead — they're exact. This is the one integration seam to verify
first; a quick way is to confirm a known face (e.g. a floor at a known height)
has the UVs you expect.

## How it works (short version)

1. **Parse** brushes (planes + texture) and `light_omni` lights from every entity.
2. **Winding**: intersect each brush's planes to recover face polygons (validated
   on your map: 748/748 faces produce valid windings).
3. **Layout**: project each face to its plane, allocate an atlas rect sized by
   texel density, shelf-pack into the atlas.
4. **Bake**: for each texel, accumulate `N·L · attenuation · energy` from each
   light, multiplied by a shadow-ray visibility test through a uniform grid.
5. **Dilate** atlas gutters (prevents seam bleed), tonemap + gamma, write PNG +
   JSON.

## Known limitations / next steps

- **Direct lighting + ambient only.** No indirect bounces (the HL2 "radiosity"
  look). Adding one or two bounces is the natural next step: after the first
  bake, treat lit texels as emitters and gather again. Hooks are isolated in
  `Baker.cs`.
- **Uniform-grid occlusion.** Fine for this map size. For much larger maps a BVH
  would be faster.
- **No normal-mapped lightmap (RNM).** Single lightmap per face; pairs fine with
  standard normal mapping in your shader.
- **Self-shadow bias** is a fixed nudge (0.1 units off-surface). If you see acne
  or peter-panning at a different scale, tune the offset in `Baker.Bake`.
