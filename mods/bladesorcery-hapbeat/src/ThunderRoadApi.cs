using System;
using System.Collections.Generic;
using ThunderRoad;
using UnityEngine;

namespace Hapbeat.BladeSorcery
{
    /// <summary>
    /// Every direct touch of the ThunderRoad API lives in this one file.
    /// <para>
    /// Warpfrog publishes no C# API reference for <c>ThunderRoad.dll</c>, so the exact
    /// delegate signatures below are *inferred* (see the research notes referenced in the
    /// README). They are isolated here so that a signature mismatch is a compile error in
    /// a single file with a known fix procedure, instead of being scattered through the
    /// mod. Every inferred member carries a <c>VERIFY-ILSPY</c> comment; the README
    /// explains how to confirm them against the real DLL.
    /// </para>
    /// <para>
    /// The rest of the mod only sees the stable surface of this class: a handful of
    /// parameterless events plus three query helpers. Nothing outside this file mentions a
    /// ThunderRoad type, so re-fitting to a changed game API never spreads.
    /// </para>
    /// </summary>
    internal sealed class ThunderRoadApi
    {
        /// <summary>Optional log sink (wired to the mod's logger).</summary>
        public Action<string> Log;

        /// <summary>Player prefab finished spawning — safe to touch <c>Player.local</c>.</summary>
        public event Action PlayerSpawned;

        /// <summary>A non-player creature took a hit caused by the player.</summary>
        public event Action EnemyHit;

        /// <summary>A non-player creature was killed by the player.</summary>
        public event Action EnemyKill;

#if HAPBEAT_BS_PARRY
        /// <summary>An attack the player was part of was parried.</summary>
        public event Action Parry;
#endif

#if HAPBEAT_BS_DEFLECT
        /// <summary>A projectile / spell the player was part of was deflected.</summary>
        public event Action Deflect;
#endif

        /// <summary>The player's own creature took damage.</summary>
        public event Action PlayerHit;

        /// <summary>The player's own creature was killed.</summary>
        public event Action PlayerKilled;

        private bool _spawnSubscribed;
        private bool _worldSubscribed;

        // The Creature instance whose per-instance events we subscribed to. Kept so the
        // unsubscribe targets the same object even if Player.local.creature was replaced
        // (respawn / level reload) in the meantime.
        private Creature _playerCreature;

        #region Subscription

        /// <summary>
        /// Subscribe to the player-spawn notification. Called from <c>ScriptLoaded</c>:
        /// this is the only event that is safe to take before a level is running.
        /// </summary>
        public void SubscribeSpawn()
        {
            if (_spawnSubscribed)
                return;

            // VERIFY-ILSPY: EventManager.OnPlayerPrefabSpawned and its delegate type
            // EventManager.PlayerPrefabSpawnedEvent (assumed parameterless).
            EventManager.OnPlayerPrefabSpawned += HandlePlayerPrefabSpawned;
            _spawnSubscribed = true;
        }

        /// <summary>
        /// Subscribe to the gameplay events. Called after the player prefab spawned,
        /// because the per-instance Creature events need <c>Player.local.creature</c> to
        /// exist. Idempotent, and re-runnable after a respawn (it re-binds to the current
        /// player creature).
        /// </summary>
        public void SubscribeWorld()
        {
            Creature current = GetPlayerCreature();

            if (_worldSubscribed && ReferenceEquals(current, _playerCreature))
                return;

            // Player creature swapped (respawn / level change): drop the old binding first.
            if (_playerCreature != null && !ReferenceEquals(current, _playerCreature))
                UnsubscribePlayerCreature();

            if (!_worldSubscribed)
            {
                // VERIFY-ILSPY: EventManager.onCreatureHit — assumed
                //   (Creature creature, CollisionInstance collisionInstance, EventTime eventTime)
                EventManager.onCreatureHit += HandleCreatureHit;

                // VERIFY-ILSPY: EventManager.onCreatureKill — assumed
                //   (Creature creature, Player player, CollisionInstance collisionInstance, EventTime eventTime)
                EventManager.onCreatureKill += HandleCreatureKill;

#if HAPBEAT_BS_PARRY
                // VERIFY-ILSPY: EventManager.onCreatureAttackParry — assumed
                //   (Creature source, Creature target, CollisionInstance collisionInstance, EventTime eventTime)
                // If this signature is wrong, remove HAPBEAT_BS_PARRY from DefineConstants
                // to build without parry support while you fix it.
                EventManager.onCreatureAttackParry += HandleCreatureParry;
#endif

#if HAPBEAT_BS_DEFLECT
                // VERIFY-ILSPY: EventManager.onDeflect — assumed
                //   (Creature source, Item item, Creature target, EventTime eventTime)
                // Same escape hatch as parry: drop HAPBEAT_BS_DEFLECT if it does not match.
                EventManager.onDeflect += HandleDeflect;
#endif
                _worldSubscribed = true;
            }

            if (current != null && _playerCreature == null)
            {
                // VERIFY-ILSPY: Creature.OnDamageEvent / Creature.OnKillEvent — assumed
                //   (CollisionInstance collisionInstance, EventTime eventTime)
                current.OnDamageEvent += HandlePlayerDamage;
                current.OnKillEvent += HandlePlayerKill;
                _playerCreature = current;
            }
        }

        /// <summary>Release every subscription. Safe to call repeatedly.</summary>
        public void UnsubscribeAll()
        {
            if (_spawnSubscribed)
            {
                EventManager.OnPlayerPrefabSpawned -= HandlePlayerPrefabSpawned;
                _spawnSubscribed = false;
            }

            if (_worldSubscribed)
            {
                EventManager.onCreatureHit -= HandleCreatureHit;
                EventManager.onCreatureKill -= HandleCreatureKill;
#if HAPBEAT_BS_PARRY
                EventManager.onCreatureAttackParry -= HandleCreatureParry;
#endif
#if HAPBEAT_BS_DEFLECT
                EventManager.onDeflect -= HandleDeflect;
#endif
                _worldSubscribed = false;
            }

            UnsubscribePlayerCreature();
        }

        private void UnsubscribePlayerCreature()
        {
            if (_playerCreature == null)
                return;

            try
            {
                _playerCreature.OnDamageEvent -= HandlePlayerDamage;
                _playerCreature.OnKillEvent -= HandlePlayerKill;
            }
            catch (Exception ex)
            {
                Write("Failed to unsubscribe player creature events: " + ex.Message);
            }

            _playerCreature = null;
        }

        #endregion

        #region Handlers

        private void HandlePlayerPrefabSpawned()
        {
            Raise(PlayerSpawned, "PlayerSpawned");
        }

        private void HandleCreatureHit(Creature creature, CollisionInstance collisionInstance, EventTime eventTime)
        {
            // Both an OnStart and an OnEnd fire for the same hit; taking only OnStart keeps
            // one haptic pulse per hit and keeps the pulse at the moment of impact.
            if (eventTime != EventTime.OnStart)
                return;

            // The player taking damage arrives here too — that path is handled by the
            // per-instance Creature.OnDamageEvent so it can map to its own logical event.
            if (IsPlayerCreature(creature))
                return;

            if (!IsCausedByPlayer(collisionInstance))
                return;

            Raise(EnemyHit, "EnemyHit");
        }

        private void HandleCreatureKill(Creature creature, Player player, CollisionInstance collisionInstance,
            EventTime eventTime)
        {
            if (eventTime != EventTime.OnStart)
                return;
            if (IsPlayerCreature(creature))
                return; // player death goes through Creature.OnKillEvent

            // "enemy_kill" means *the player* killed something. Without this filter a
            // creature dropped by fall damage, by fire, or by another creature would pulse
            // the player's device in the middle of an arena fight.
            if (!IsKillByPlayer(player, collisionInstance))
                return;

            Raise(EnemyKill, "EnemyKill");
        }

#if HAPBEAT_BS_PARRY
        private void HandleCreatureParry(Creature source, Creature target, CollisionInstance collisionInstance,
            EventTime eventTime)
        {
            if (eventTime != EventTime.OnStart)
                return;

            // Only fire when the player is one of the two creatures. Every NPC-versus-NPC
            // parry in a crowded arena would otherwise buzz the player's device.
            //
            // VERIFY-ILSPY: which of source/target is the parrying side is not confirmed,
            // so the check stays symmetric. Once the roles are known, narrow this to the
            // player's own parry (README ILSpy checklist #3).
            if (!InvolvesPlayer(source, target))
                return;

            Raise(Parry, "Parry");
        }
#endif

#if HAPBEAT_BS_DEFLECT
        private void HandleDeflect(Creature source, Item item, Creature target, EventTime eventTime)
        {
            if (eventTime != EventTime.OnStart)
                return;

            // Same reasoning as parry: a deflect the player was not part of is somebody
            // else's event. A null creature on either side fails the check, which is the
            // safe direction (silence beats a phantom pulse).
            if (!InvolvesPlayer(source, target))
                return;

            Raise(Deflect, "Deflect");
        }
#endif

        private void HandlePlayerDamage(CollisionInstance collisionInstance, EventTime eventTime)
        {
            if (eventTime != EventTime.OnStart)
                return;
            Raise(PlayerHit, "PlayerHit");
        }

        private void HandlePlayerKill(CollisionInstance collisionInstance, EventTime eventTime)
        {
            if (eventTime != EventTime.OnStart)
                return;
            Raise(PlayerKilled, "PlayerKilled");
        }

        #endregion

        #region Queries

        /// <summary>Whether the local player and its creature currently exist.</summary>
        public bool IsPlayerReady
        {
            get { return GetPlayerCreature() != null; }
        }

        /// <summary>
        /// Player health as a 0..1 fraction. False when the player (or a sane
        /// <c>maxHealth</c>) is not available, in which case low-health logic must be
        /// skipped rather than assuming full or empty health.
        /// </summary>
        public bool TryGetPlayerHealthFraction(out float fraction)
        {
            fraction = 1f;
            Creature creature = GetPlayerCreature();
            if (creature == null)
                return false;

            // VERIFY-ILSPY: Creature.currentHealth / Creature.maxHealth (floats).
            float max = creature.maxHealth;
            if (max <= 0f)
                return false;

            fraction = creature.currentHealth / max;
            return true;
        }

        /// <summary>
        /// Number of live non-player creatures within <paramref name="radius"/> metres of
        /// the player. Returns -1 when the player is unavailable (distinct from "nobody
        /// nearby", so the caller does not treat a missing player as a calm moment).
        /// <para>
        /// Faction is deliberately ignored: the faction hostility table is not part of the
        /// documented API, and in the arena scenarios this demo targets everyone nearby is
        /// hostile anyway. Counting a friendly NPC would at worst trigger the "surrounded"
        /// cue slightly early.
        /// </para>
        /// </summary>
        public int CountNearbyCreatures(float radius)
        {
            Creature playerCreature = GetPlayerCreature();
            if (playerCreature == null || playerCreature.transform == null)
                return -1;

            Vector3 origin = playerCreature.transform.position;
            float radiusSq = radius * radius;
            int count = 0;

            // VERIFY-ILSPY: Creature.allActive — the static collection of live creatures.
            List<Creature> all = Creature.allActive;
            if (all == null)
                return 0;

            for (int i = 0; i < all.Count; i++)
            {
                Creature creature = all[i];
                if (creature == null || creature.transform == null)
                    continue;
                if (ReferenceEquals(creature, playerCreature))
                    continue;
                if (IsDead(creature))
                    continue;

                Vector3 delta = creature.transform.position - origin;
                if (delta.sqrMagnitude <= radiusSq)
                    count++;
            }

            return count;
        }

        #endregion

        #region Inferred predicates (single edit points)

        /// <summary>
        /// Whether a collision was caused by the player. Used to keep enemy-hit haptics to
        /// the player's own strikes instead of firing on every NPC-versus-NPC exchange in
        /// a crowded arena.
        /// <para>
        /// If the underlying API turns out to be unavailable, this must keep returning
        /// <c>false</c> for the unknown case: over-firing on every hit in the room would be
        /// far worse for the demo than missing some of the player's own hits.
        /// </para>
        /// </summary>
        private static bool IsCausedByPlayer(CollisionInstance collisionInstance)
        {
            if (collisionInstance == null)
                return false;

            // VERIFY-ILSPY: CollisionInstance.IsDoneByPlayer(). If the real member is a
            // property or is named differently, fix it here — this is the only call site.
            return collisionInstance.IsDoneByPlayer();
        }

        /// <summary>
        /// Whether a kill should count as the player's. The kill event carries a
        /// <c>Player</c> alongside the collision, so that is preferred when present; when it
        /// is null the collision attribution is used, exactly as the hit path does.
        /// <para>
        /// Both branches fail closed: an unknown killer is not the player.
        /// </para>
        /// </summary>
        private static bool IsKillByPlayer(Player player, CollisionInstance collisionInstance)
        {
            // VERIFY-ILSPY: the meaning of the Player parameter on onCreatureKill. It is
            // assumed to be the player credited with the kill (null for kills nobody's
            // player caused). If it turns out to be "the local player, always", drop this
            // branch and rely on IsCausedByPlayer alone — see README ILSpy checklist #2.
            if (player != null)
                return ReferenceEquals(player, Player.local);

            return IsCausedByPlayer(collisionInstance);
        }

        /// <summary>
        /// Whether the player's creature is one of the two creatures in an event. Used by
        /// the parry / deflect paths, where the event is global and would otherwise report
        /// exchanges the player has nothing to do with.
        /// </summary>
        private static bool InvolvesPlayer(Creature a, Creature b)
        {
            Creature playerCreature = GetPlayerCreature();
            if (playerCreature == null)
                return false;

            return ReferenceEquals(a, playerCreature) || ReferenceEquals(b, playerCreature);
        }

        /// <summary>Whether a creature is dead / dying, so it stops counting toward the
        /// "surrounded" cue as soon as it goes down.</summary>
        private static bool IsDead(Creature creature)
        {
            // VERIFY-ILSPY: Creature.isKilled. Blade &amp; Sorcery 1.0 may instead expose a
            // state enum (e.g. Creature.state == CreatureState.Dead) — single edit point.
            return creature.isKilled;
        }

        private static bool IsPlayerCreature(Creature creature)
        {
            if (creature == null)
                return false;
            // Reference comparison instead of a Creature.isPlayer flag, whose existence is
            // not documented.
            return ReferenceEquals(creature, GetPlayerCreature());
        }

        private static Creature GetPlayerCreature()
        {
            // VERIFY-ILSPY: Player.local (static) and Player.creature.
            Player local = Player.local;
            return local == null ? null : local.creature;
        }

        #endregion

        private void Raise(Action handler, string name)
        {
            if (handler == null)
                return;

            try
            {
                handler();
            }
            catch (Exception ex)
            {
                // A throwing handler must never propagate back into the game's event
                // dispatch: ThunderRoad would drop the remaining subscribers of that event.
                Write("Handler for " + name + " threw: " + ex.Message);
            }
        }

        private void Write(string message)
        {
            Action<string> log = Log;
            if (log != null) log(message);
        }
    }
}
