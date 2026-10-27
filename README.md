# Desert Serpent Coaster: Unity 6 Roller Coaster Simulation

<!-- Replace with your own screenshot or GIF once re-captured: docs/images/hero.png -->
<!-- ![Desert Serpent Coaster](docs/images/hero.png) -->

A physics-driven roller coaster simulation built in **Unity 6** with **C#** and the **Unity Splines** package. The cart rides a spline track with a lift hill, drops, banked turns, a barrel-roll twist and a vertical loop through a themed desert world, with water splashes, fire, explosions, physics-based destruction and a fireworks finale.

▶️ **[Watch the full ride on YouTube](YOUTUBE_LINK_HERE)**

**~10,000 lines of C#** across 46 scripts: runtime gameplay systems plus custom Unity Editor tooling.

---

## Highlights

### Track & physics
- **Energy-based cart physics** (`CoasterCart.cs`): a chain lift pulls the train up the first hill, then **only gravity** drives it, with rolling friction and quadratic air drag removing energy like a real coaster. Brakes stop it in the station and the ride loops automatically.
- **Spline track system** (`CoasterTrack.cs`): converts a `SplineContainer` into a measured track, computes the cart's "up" vector through **twists, banked turns and vertical loops**, and marks lift and brake sections by knot.
- **Live G-force calculation**: vertical G felt by the riders is computed every frame and shown on the HUD.
- **Ride simulator** (`CoasterRideSim.cs`): runs the same physics instantly in the editor to size the track so the ride lasts 30–60 seconds and never stalls.
- **Procedural track mesh** (`TrackBuilder.cs`): generates rails, spine tube, cross ties and support pillars down to the terrain.

### Editor tooling
- **One-click coaster builder** (`CoasterAutoBuilder.cs`, ~1,250 lines): a `Tools > Coaster Kit` menu that builds the full scene: track, 3-car train with riders, station, ponds, effect zones, cameras, HUD and audio. It finds open terrain, lowers sand where the track needs room, and supports Undo.
- **Custom Inspectors** (`CoasterEditors.cs`): buttons to add and remove vertical loops and run a ride check from the Inspector.
- **Render pipeline helper** (`PipelineUtil.cs`): picks the right shaders for URP or the Built-in pipeline automatically.

### Effects & set pieces
| Script | What happens |
|---|---|
| `SplashZone.cs`, `SplashZoneExtras.cs`, `WaterFountain.cs` | Layered splashes, spray and ripples as the cart hits the ponds |
| `OasisWaterfall.cs`, `LensDrops.cs` | A waterfall the cart rides through, with refractive water drops on the camera lens |
| `FireRing.cs`, `FireGauntlet.cs`, `FireJet.cs` | Spinning rings of fire and flame jets arching over the track |
| `ExplosionZone.cs` | Explosions with light flash and physics impulse on nearby objects |
| `RuinsCollapse.cs` | Stone columns topple like dominoes with real Rigidbody physics |
| `FireDragon.cs`, `DragonBattle.cs` | Procedural serpent dragons that circle, swoop alongside the cart and battle fire vs. water |
| `MeteorShower.cs`, `SandTornado.cs` | Sky turns to sunset as meteors fall; a wandering sand tornado |
| `FireworksFinale.cs` | Fireworks show (peony, ring, willow bursts) when the train returns |
| `FairylandZone.cs`, `WorldTransition.cs` | A magic portal that transforms part of the desert ride into a fairyland |
| `FxFactory.cs` | Builds fire, smoke, splash, spark and explosion particle systems from code |

### Camera & player experience
- **Cinematic ride director** (`RideDirector.cs`): cuts automatically between rider, chase and drone cameras by track section, with slow motion at key moments and a live speed, height and G-force graph showing potential energy turning into kinetic energy.
- **Manual cameras** (`RideCamera.cs`): `C` cycles cameras, `1` rider, `2` chase, `3` trackside, `4` overview.
- **On-ride photos** (`RidePhoto.cs`): theme-park-style hidden cameras flash and capture up to 4 photos per ride.
- **Living riders** (`RiderBoarding.cs`, `RiderAnimator.cs`, `RiderScreams.cs`): riders walk from the platform, board, raise their arms on drops, scream during airtime, then climb out and re-board.
- **HUD** (`RideHUD.cs`): ride time, speed, height and G-force (`H` to hide).
- **Speed-based audio** (`CartAudio.cs`): rolling and wind sounds scale with cart speed.

---

## Tech

| | |
|---|---|
| Engine | Unity 6 (6000.5.9f1), URP |
| Language | C# |
| Unity packages | Splines, Input System, Mathematics |
| Concepts | Rigidbody physics, trigger zones, particle systems, procedural meshes, custom Editor tooling, cinematic cameras |

## Repository contents

```
Assets/
├── CoasterKit/
│   ├── Scripts/            # runtime gameplay, physics, effects, cameras, riders
│   │   └── Editor/         # one-click builder, custom Inspectors, import helpers
│   └── Setup/Editor/       # package setup helper
└── MagicPortal/            # fairyland zone + world transition
```

This repository contains **my source code only**. The scene also uses third-party art packs from the Unity Asset Store (desert environment, skybox, fire VFX, dragon models). Their licenses don't allow redistribution, so they aren't included here.

## How to run

1. Create a new **Unity 6 (6000.5.9f1)** project using the URP template
2. Install **Splines** and **Input System** from `Window > Package Manager`
3. Copy this repo's `Assets/` folder into your project
4. Open a scene with a terrain, then run **Tools > Coaster Kit > 3. Build Coaster Centred On My Scene View** (or **2. Build Complete Coaster** with the desert pack installed)
5. Press **Play**

## About

Built by **Yashvanthini Uppala**, MS Computer Science, University of Dayton (Fall 2026).

[LinkedIn](https://linkedin.com/in/yashvanthiniuppala-a926203b5) · yashvanthiniuppala@gmail.com
