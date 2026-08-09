// Compile-check stubs — NOT shipped and NOT part of the mod DLL.
//
// A hand-written mirror of the ThunderRoad members this mod uses, with exactly the
// signatures ThunderRoadApi.cs assumes. Building against these proves the mod is
// internally consistent (no missing usings, no arity mistakes, no wrong delegate shapes);
// it proves nothing about whether the real ThunderRoad.dll agrees. That check is the
// ILSpy pass described in the README.
//
// If you change an assumed signature in ThunderRoadApi.cs, change it here too, or the
// compile-check stops testing the thing you actually build.

#pragma warning disable 67 // stub events are never raised

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ThunderRoad
{
    /// <summary>Assumed: fires at the start and at the end of an occurrence.</summary>
    public enum EventTime
    {
        OnStart,
        OnEnd,
    }

    /// <summary>Base class the game instantiates for every scripted mod.</summary>
    public abstract class ThunderScript
    {
        public virtual void ScriptLoaded(ModManager.ModData modData) { }
        public virtual void ScriptEnable() { }
        public virtual void ScriptDisable() { }
        public virtual void ScriptUnload() { }
        public virtual void ScriptUpdate() { }
        public virtual void ScriptFixedUpdate() { }
        public virtual void ScriptLateUpdate() { }
    }

    public static class ModManager
    {
        public class ModData
        {
            public string Name;
            public string Description;
            public string Author;
            public string ModVersion;
        }
    }

    /// <summary>Damage/collision context passed to hit and kill events (1.0+; replaced the
    /// pre-1.0 CollisionStruct).</summary>
    public class CollisionInstance
    {
        public bool IsDoneByPlayer()
        {
            return false;
        }
    }

    /// <summary>An in-world item (weapon, projectile, ...).</summary>
    public class Item
    {
    }

    public class Creature
    {
        public delegate void DamageEvent(CollisionInstance collisionInstance, EventTime eventTime);

        public delegate void KillEvent(CollisionInstance collisionInstance, EventTime eventTime);

        /// <summary>Every live creature in the scene.</summary>
        public static List<Creature> allActive = new List<Creature>();

        public Transform transform;
        public float currentHealth;
        public float maxHealth;
        public bool isKilled;
        public int factionId;

        public event DamageEvent OnDamageEvent;
        public event KillEvent OnKillEvent;
    }

    public class Player
    {
        public static Player local;

        public Creature creature;
    }

    public static class EventManager
    {
        public delegate void PlayerPrefabSpawnedEvent();

        public delegate void CreatureHitEvent(Creature creature, CollisionInstance collisionInstance,
            EventTime eventTime);

        public delegate void CreatureKillEvent(Creature creature, Player player,
            CollisionInstance collisionInstance, EventTime eventTime);

        public delegate void CreatureParryEvent(Creature source, Creature target,
            CollisionInstance collisionInstance, EventTime eventTime);

        public delegate void DeflectEvent(Creature source, Item item, Creature target, EventTime eventTime);

        public static event PlayerPrefabSpawnedEvent OnPlayerPrefabSpawned;
        public static event CreatureHitEvent onCreatureHit;
        public static event CreatureKillEvent onCreatureKill;
        public static event CreatureParryEvent onCreatureAttackParry;
        public static event DeflectEvent onDeflect;
    }

    #region Mod option attributes (in-game settings menu)

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ModOptionAttribute : Attribute
    {
        public ModOptionAttribute() { }

        public ModOptionAttribute(string name)
        {
            Name = name;
        }

        public string Name;
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ModOptionCategoryAttribute : Attribute
    {
        public ModOptionCategoryAttribute(string category, int order = 0)
        {
            Category = category;
            Order = order;
        }

        public string Category;
        public int Order;
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ModOptionOrderAttribute : Attribute
    {
        public ModOptionOrderAttribute(int order)
        {
            Order = order;
        }

        public int Order;
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ModOptionTooltipAttribute : Attribute
    {
        public ModOptionTooltipAttribute(string tooltip)
        {
            Tooltip = tooltip;
        }

        public string Tooltip;
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ModOptionSliderAttribute : Attribute
    {
    }

    // Only the attributes BasSDK's own documentation names are stubbed here, and only the
    // ones the mod actually applies. Nothing carries a min/max/step: the range-bearing
    // attribute is ModOptionUI, whose parameter list is not documented, and inventing a
    // shape for it here would make the compile-check "pass" against an API that may not
    // exist. See README ILSpy checklist #7.

    #endregion
}
