# FSL Party

A small, silly FRUKT mod that shows off what [FruktSharedLibrary](https://github.com/AlmostGalactic/FruktSharedLibrary)
can do. It's meant as an example to read and copy from, but it's fun to play with too.

## What it does

- Right-click any body and open `+ PARTY` for balloon, fling, detonate and a walking toggle. Inside that,
  `+ BODY PARTS` has pop head, drop arms and kick out legs.
- Press **V** to fling whoever is under your crosshair.
- In the mod menu (F8, or MODS in the pause menu), go to Mods → FSL Party for:
  - Live counts of creatures, limbs and heads lost, and explosions
  - Raining humans from the sky
  - Making everyone walk
  - Choosing the fling direction
  - A "calm down" button
- Under Party settings you can turn on exploding deaths (chain reactions included) and bullet time on kills, and
  change the blast radius and the fling key.
- PARTY TIME in the pause menu unpauses the game and rains humans.

## Installing

1. Install [FruktSharedLibrary](https://github.com/AlmostGalactic/FruktSharedLibrary/releases).
2. Download `FslParty.dll` from the [releases page](https://github.com/AlmostGalactic/FslParty/releases) and put
   it in `FRUKT/Mods`.

## What to look at in the code

Everything is in [FslParty.cs](FslParty.cs). Each feature uses a different part of the library:

| Feature | Library API |
|---------|-------------|
| The `+ PARTY` group in the right-click menu | `ContextMenus.AddCreatureGroup`, `AddCreatureAction`, `AddToggle`, nested `AddGroup` |
| The FSL Party page | `ModMenu.AddPage` with `Label`, `Button`, `Choice`, `WithTooltip` |
| The Party settings page | `ModMenu.AddPreferencesPage` on a MelonPreferences category |
| PARTY TIME | `PauseMenu.AddButton` |
| Raining humans | `Creatures.SpawnHuman`, `LocalPlayer.GetPointInFront`, `Scheduler.After` |
| Balloon, fling | `GameEvents.FixedUpdate`, `creature.AddForce` |
| Detonate, exploding deaths | `Damage.Explosion`, `GameEvents.CreatureDied`, `LocalPlayer.ShakeCamera` |
| Pop head, drop arms | `creature.GetLimb`, `Damage.Apply`, `limb.Detach` |
| Bullet time on kills | `GameEvents.KillAdded`, `World.TimeScale` |
| Fling key | `KeyBind.Parse(...).WasPressed()`, `Creatures.GetAimedCreature` |
| Sounds and messages | `Sounds.Play`, `Notifications.Show` |

## Building

You need the .NET SDK (6 or newer), and FRUKT with MelonLoader and FruktSharedLibrary installed.

```
dotnet build -p:FruktGameDir="C:\path\to\FRUKT"
```

Then copy `bin/Debug/net6.0/FslParty.dll` into `FRUKT/Mods`.

## AI disclosure

This mod was made with AI assistance (Claude), which helped in the writing of its code and documentation. It
has been played and checked in the real game.

## License

[MIT](LICENSE). Made by jjlala1313.
