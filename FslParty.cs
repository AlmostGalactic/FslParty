using System;
using System.Collections.Generic;
using FruktSharedLibrary.Combat;
using FruktSharedLibrary.Controls;
using FruktSharedLibrary.Core;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Gameplay;
using FruktSharedLibrary.UI;
using Il2CppInfrastructure.Project.AssetsHandlers.SFX;
using Il2CppLVA.Creatures;
using Il2CppLVA.NodesHierarchy.Benchmark.Variants;
using MelonLoader;
using MelonLoader.Preferences;
using UnityEngine;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(FslParty.PartyMod), "FSL Party", "1.0.0", "jjlala1313")]
[assembly: MelonGame("tripledose", "FRUKT")]
[assembly: MelonAdditionalDependencies("FruktSharedLibrary")]

namespace FslParty
{
    /// <summary>
    /// A small, silly demo of FruktSharedLibrary: right-click party tricks, raining humans, exploding deaths,
    /// bullet time on kills, a fling hotkey, a settings page and a pause-menu line.
    /// </summary>
    public class PartyMod : MelonMod
    {
        private MelonPreferences_Entry<bool> _explodingDeaths;
        private MelonPreferences_Entry<bool> _bulletTime;
        private MelonPreferences_Entry<float> _blastRadius;
        private MelonPreferences_Entry<string> _flingKey;

        private enum FlingDirection { Up, Away, Random }

        private FlingDirection _flingDirection = FlingDirection.Away;
        private readonly Dictionary<IntPtr, (AbstractCreature Creature, float Until)> _balloons = new();
        private float _nextExplosionAt;
        private float _bulletTimeUntil;
        private float _timeBeforeBulletTime = 1f;
        private int _explosions;
        private int _limbsLost;
        private int _headsLost;

        public override void OnInitializeMelon()
        {
            // Settings: a MelonPreferences category becomes a settings page in the mod menu.
            var prefs = MelonPreferences.CreateCategory("FslParty", "Party settings");
            _explodingDeaths = prefs.CreateEntry("ExplodingDeaths", false, "Exploding deaths",
                "Bodies explode a moment after they die. Chain reactions included.");
            _bulletTime = prefs.CreateEntry("BulletTimeOnKill", true, "Bullet time on kills",
                "Slows time for a moment whenever the kill counter goes up.");
            _blastRadius = prefs.CreateEntry("BlastRadius", 3f, "Blast radius",
                "Size of every party explosion, in metres.", false, false, new ValueRange<float>(1f, 6f));
            _flingKey = prefs.CreateEntry("FlingKey", "V", "Fling key",
                "Flings the creature under your crosshair.");
            ModMenu.AddPreferencesPage(prefs);

            // A page with buttons and live numbers.
            ModMenu.AddPage("FSL Party")
                .Label(() => $"creatures {Creatures.Count}   limbs lost {_limbsLost}   heads lost {_headsLost}   explosions {_explosions}")
                .Header("Chaos")
                .Button("Rain humans", RainHumans).WithTooltip("Six humans fall from the sky in front of you.")
                .Button("Make everyone walk", () => ForEachLiving(c => c.SetWalking(true)))
                .Choice("Fling direction", () => _flingDirection, v => _flingDirection = v)
                .Separator()
                .Button("Calm down", CalmDown).WithTooltip("Normal speed, no balloons, everyone stops walking.");

            // The right-click menu on every body: a drop-down group with a nested group inside.
            var party = ContextMenus.AddCreatureGroup("Party")
                .AddCreatureAction("Balloon", Balloon)
                .AddCreatureAction("Fling", Fling)
                .AddCreatureAction("Detonate", c => Explode(c.GetPosition()))
                .AddToggle("Walking", ctx => ctx.Creature.IsWalking(), (ctx, on) => ctx.Creature.SetWalking(on));
            party.AddGroup("Body parts")
                .AddCreatureAction("Pop head", PopHead, c => c.HasLimb(HumanoidNodeTagValue.Head))
                .AddCreatureAction("Drop arms", c => Detach(c, HumanoidNodeTagValue.LeftArm, HumanoidNodeTagValue.RightArm))
                .AddCreatureAction("Kick out legs", c => Detach(c, HumanoidNodeTagValue.LeftLeg, HumanoidNodeTagValue.RightLeg));

            // A line in the pause menu.
            PauseMenu.AddButton("Party time", () =>
            {
                World.Resume();
                Scheduler.After(0.4f, RainHumans);
            });

            // Game events.
            GameEvents.CreatureDied += OnCreatureDied;
            GameEvents.KillAdded += _ => StartBulletTime();
            GameEvents.LimbDetached += OnLimbDetached;
            GameEvents.FixedUpdate += LiftBalloons;
            GameEvents.SandboxExited += () => _balloons.Clear();
        }

        public override void OnUpdate()
        {
            if (_bulletTimeUntil > 0f && Time.realtimeSinceStartup > _bulletTimeUntil)
            {
                _bulletTimeUntil = 0f;
                if (!World.IsPaused)
                    World.TimeScale = _timeBeforeBulletTime;
            }
            if (!GameState.InSandbox || ModMenu.IsOpen)
                return;
            if (KeyBind.Parse(_flingKey.Value, Key.V).WasPressed())
            {
                var target = Creatures.GetAimedCreature();
                if (target == null)
                    Notifications.Warn("Aim at someone to fling them.");
                else
                    Fling(target);
            }
        }

        // ------------------------------------------------------------ tricks

        private void RainHumans()
        {
            if (!GameState.InSandbox)
                return;
            var center = LocalPlayer.GetPointInFront(6f) + Vector3.up * 7f;
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI * 2f / 6f;
                var position = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.5f;
                Scheduler.After(i * 0.35f, () =>
                {
                    Creatures.SpawnHuman(position, LocalPlayer.RotationFacingPlayer(position));
                    Sounds.Play(ToolsSFXType.SpawnerThrow, position);
                });
            }
            Notifications.Show("It's raining humans.");
        }

        private void Balloon(AbstractCreature creature)
        {
            _balloons[creature.Pointer] = (creature, Time.time + 5f);
            Sounds.Play(UISFXType.ItemSend, 0.8f);
        }

        private void LiftBalloons()
        {
            if (_balloons.Count == 0)
                return;
            foreach (var key in new List<IntPtr>(_balloons.Keys))
            {
                var (creature, until) = _balloons[key];
                if (!creature.IsValid() || Time.time > until)
                {
                    _balloons.Remove(key);
                    continue;
                }
                // A little more than gravity, so they drift upwards.
                creature.AddForce(Vector3.up * 12f, ForceMode.Acceleration);
            }
        }

        private void Fling(AbstractCreature creature)
        {
            var position = creature.GetPosition();
            Vector3 direction = _flingDirection switch
            {
                FlingDirection.Up => Vector3.up,
                FlingDirection.Away => ((position - LocalPlayer.Position).normalized + Vector3.up * 0.5f).normalized,
                _ => new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(0.3f, 1f), UnityEngine.Random.Range(-1f, 1f)).normalized,
            };
            creature.AddForce(direction * 1200f);
            Sounds.Play(WhooshSFXType.WeaponAimWhoosh, position);
        }

        private void PopHead(AbstractCreature creature)
        {
            var head = creature.GetLimb(HumanoidNodeTagValue.Head);
            if (head == null)
                return;
            var center = head.GetRigidbody().worldCenterOfMass;
            Damage.Apply(head, center, radiusVoxels: 8, strength: 1f);
            Sounds.Play(ImpactSFXType.Bullet12gaToBodyOrganic, center);
        }

        private void Detach(AbstractCreature creature, params HumanoidNodeTagValue[] parts)
        {
            foreach (var part in parts)
                creature.GetLimb(part)?.Detach();
        }

        private int Explode(Vector3 position)
        {
            _explosions++;
            float distance = Vector3.Distance(position, LocalPlayer.Position);
            LocalPlayer.ShakeCamera(Mathf.Clamp(6f / Mathf.Max(1f, distance), 0.1f, 1.2f));
            Sounds.Play(ImpactSFXType.Bullet12gaHardSurface, position, 1f);
            return Damage.Explosion(position, _blastRadius.Value, force: 40f, maxRadiusVoxels: 6);
        }

        private void CalmDown()
        {
            _balloons.Clear();
            _bulletTimeUntil = 0f;
            World.TimeScale = 1f;
            ForEachLiving(c => c.SetWalking(false));
            Notifications.Show("Everyone calm down.");
        }

        private static void ForEachLiving(Action<AbstractCreature> action)
        {
            foreach (var creature in Creatures.All)
            {
                if (creature.IsLiving())
                    action(creature);
            }
        }

        // ------------------------------------------------------------ events

        private void OnCreatureDied(AbstractCreature creature)
        {
            // Whole-ish bodies only, so severed fingers don't set off a war.
            if (!_explodingDeaths.Value || creature.GetLimbCount() < 5)
                return;
            var position = creature.GetPosition();
            // A short fuse, and at least a quarter second between explosions so chains go off one by one.
            float now = Time.realtimeSinceStartup;
            _nextExplosionAt = Mathf.Max(now + 0.6f, _nextExplosionAt + 0.25f);
            Scheduler.After(_nextExplosionAt - now, () =>
            {
                if (GameState.InSandbox)
                    Explode(position);
            });
        }

        private void OnLimbDetached(AbstractCreature owner, Il2CppLVA.Limbs.AbstractLimb part)
        {
            _limbsLost++;
            if (part.GetHumanPart() == HumanoidNodeTagValue.Head)
            {
                _headsLost++;
                Notifications.Show(_headsLost == 1 ? "First head off." : $"{_headsLost} heads off.");
            }
        }

        private void StartBulletTime()
        {
            if (!_bulletTime.Value || World.IsPaused)
                return;
            if (_bulletTimeUntil <= 0f)
                _timeBeforeBulletTime = World.TimeScale;
            World.TimeScale = 0.25f;
            _bulletTimeUntil = Time.realtimeSinceStartup + 1.5f;
        }
    }
}
