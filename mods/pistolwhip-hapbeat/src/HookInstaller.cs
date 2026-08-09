using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Hapbeat.PistolWhip
{
    /// <summary>
    /// Installs the Harmony patches.
    /// <para>
    /// Patches are applied manually instead of through <c>[HarmonyPatch]</c> attributes on
    /// purpose. Attribute patches are auto-applied by MelonLoader in one batch, so a
    /// single missing type takes the whole melon down — and none of these targets is
    /// verified against the current Steam build. Patching by name, one at a time, turns a
    /// renamed method into a warning and a missing feature while the rest keeps working,
    /// and it also sidesteps the question of which namespace MelonLoader's generated
    /// proxies put the game's types in.
    /// </para>
    /// </summary>
    internal static class HookInstaller
    {
        /// <summary>Number of hooks that were applied successfully.</summary>
        public static int AppliedCount { get; private set; }

        /// <summary>Number of hooks whose target could not be found or patched.</summary>
        public static int FailedCount { get; private set; }

        public static void InstallAll(Harmony harmony)
        {
            AppliedCount = 0;
            FailedCount = 0;

            Patch(harmony, "Gun", "Fire", "AfterGunFire");
            Patch(harmony, "GunAmmoDisplay", "Update", "AfterAmmoDisplayUpdate");
            Patch(harmony, "MeleeWeapon", "ProcessHit", "AfterMeleeHit");
            // Gun.Reload(bool triggeredByMelee) and
            // Reloader.SetReloadMethod(Reloader.ReloadMethod) both take one argument; the
            // postfixes read __args[0]. The count pins the overload down so a same-named
            // parameterless sibling (a common UI-hookup shape on Unity components) cannot
            // be patched by accident.
            Patch(harmony, "Gun", "Reload", "AfterGunReload", 1);
            Patch(harmony, "Reloader", "SetReloadMethod", "AfterSetReloadMethod", 1);
            Patch(harmony, "Projectile", "ShowPlayerHitEffects", "AfterPlayerHit");
            Patch(harmony, "PlayerHUD", "OnArmorLost", "AfterArmorLost");
            Patch(harmony, "PlayerHUD", "playArmorGainedEffect", "AfterArmorGained");
            Patch(harmony, "Player", "ProcessKillerHit", "AfterKillerHit");
            Patch(harmony, "PlayerHUD", "OnPlayerDeath", "AfterPlayerDeath");

            PistolWhipHapbeatMod.LogInfo("Game hooks: " + AppliedCount + " applied, " +
                                         FailedCount + " unavailable.");
        }

        /// <param name="expectedArgCount">Parameter count of the intended overload, or -1
        /// when the target is expected to be unique by name.</param>
        private static void Patch(Harmony harmony, string typeName, string methodName,
            string postfixName, int expectedArgCount = -1)
        {
            string label = typeName + "." + methodName;
            try
            {
                Type type = ResolveGameType(typeName);
                if (type == null)
                {
                    Skip(label, "type '" + typeName + "' not found");
                    return;
                }

                string problem;
                MethodInfo target = ResolveTargetMethod(type, methodName, expectedArgCount, out problem);
                if (target == null)
                {
                    Skip(label, problem);
                    return;
                }

                MethodInfo postfix = AccessTools.Method(typeof(GameHooks), postfixName);
                if (postfix == null)
                {
                    Skip(label, "postfix '" + postfixName + "' missing (mod bug)");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                AppliedCount++;
                PistolWhipHapbeatMod.LogInfo("Hooked " + type.FullName + "." +
                                             Describe(target) + ".");
            }
            catch (Exception ex)
            {
                Skip(label, ex.Message);
            }
        }

        private static void Skip(string label, string reason)
        {
            FailedCount++;
            PistolWhipHapbeatMod.LogWarning("Hook unavailable: " + label + " (" + reason +
                                            "). The events it drives will not fire.");
        }

        /// <summary>
        /// Pick the method to patch, by name and (when known) by parameter count.
        /// <para>
        /// Resolution is explicit rather than left to <c>AccessTools.Method</c>'s name-only
        /// lookup: when a type has several methods with the same name that lookup returns
        /// the first declared one, which can patch an overload this mod's postfix was not
        /// written for and do it silently. Here an ambiguity that cannot be resolved by
        /// parameter count becomes a skipped hook and a warning listing the candidates, so
        /// the person building the mod can pin the count down (see README
        /// "実機確認チェックリスト" 1).
        /// </para>
        /// </summary>
        private static MethodInfo ResolveTargetMethod(Type type, string methodName,
            int expectedArgCount, out string problem)
        {
            List<MethodInfo> candidates = FindMethods(type, methodName);
            if (candidates.Count == 0)
            {
                problem = "method not found on " + type.FullName;
                return null;
            }

            if (expectedArgCount >= 0)
            {
                var byCount = new List<MethodInfo>();
                foreach (MethodInfo m in candidates)
                {
                    if (m.GetParameters().Length == expectedArgCount)
                        byCount.Add(m);
                }

                if (byCount.Count == 1)
                {
                    problem = null;
                    return byCount[0];
                }

                if (byCount.Count == 0 && candidates.Count == 1)
                {
                    // Signature changed but the name is still unique: patch it and say so.
                    PistolWhipHapbeatMod.LogWarning(
                        "Hook " + type.FullName + "." + methodName + " takes " +
                        candidates[0].GetParameters().Length + " argument(s), expected " +
                        expectedArgCount + ". Patching it anyway; the argument this hook " +
                        "reads may be wrong.");
                    problem = null;
                    return candidates[0];
                }

                candidates = byCount.Count > 0 ? byCount : candidates;
            }
            else if (candidates.Count == 1)
            {
                problem = null;
                return candidates[0];
            }

            problem = "ambiguous: " + candidates.Count + " overloads (" +
                      DescribeAll(candidates) + "). Pin the intended one down by parameter " +
                      "count in HookInstaller.InstallAll";
            return null;
        }

        /// <summary>All methods with this name on the type or any base type.</summary>
        private static List<MethodInfo> FindMethods(Type type, string methodName)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.DeclaredOnly;

            var found = new List<MethodInfo>();
            var seen = new List<string>();
            for (Type t = type; t != null; t = t.BaseType)
            {
                foreach (MethodInfo method in t.GetMethods(Flags))
                {
                    if (method.Name != methodName || method.IsAbstract)
                        continue;
                    string signature = Describe(method);
                    if (seen.Contains(signature))
                        continue; // an override shadowing the base declaration
                    seen.Add(signature);
                    found.Add(method);
                }
            }
            return found;
        }

        private static string Describe(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            var names = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                names[i] = parameters[i].ParameterType.Name;
            return method.Name + "(" + string.Join(", ", names) + ")";
        }

        private static string DescribeAll(List<MethodInfo> methods)
        {
            var parts = new string[methods.Count];
            for (int i = 0; i < methods.Count; i++)
                parts[i] = Describe(methods[i]);
            return string.Join(" | ", parts);
        }

        /// <summary>
        /// Resolve a game type whose namespace depends on how MelonLoader's assembly
        /// generator laid out the proxies for this build: game scripts live in the global
        /// namespace in the original assembly, and different generator versions surface
        /// them plain, under an <c>Il2Cpp</c> namespace, or with an <c>Il2Cpp</c> prefix.
        /// All three are tried so the mod does not have to be rebuilt per generator.
        /// </summary>
        private static Type ResolveGameType(string typeName)
        {
            foreach (string candidate in Candidates(typeName))
            {
                Type type = AccessTools.TypeByName(candidate);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static IEnumerable<string> Candidates(string typeName)
        {
            yield return typeName;
            yield return "Il2Cpp." + typeName;
            yield return "Il2Cpp" + typeName;
        }
    }
}
