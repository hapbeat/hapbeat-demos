using System;
using System.Collections.Generic;
using Hapbeat.DemoSwitch;
using UnityEngine;

namespace Hapbeat.DemoHub
{
    /// <summary>Catalog source: installed demos on Quest, a dummy list in the Editor, or an injected list.</summary>
    public static class HubCatalog
    {
        /// <summary>Editor/test entry point: when set, replaces PackageManager discovery.</summary>
        public static Func<IReadOnlyList<DemoSessionCatalogEntry>> Override;

        public static IReadOnlyList<DemoSessionCatalogEntry> Load()
        {
            if (Override != null) return Override();
            if (Application.isEditor) return EditorDummy();
            return DemoSessionCatalog.LoadInstalled();
        }

        private const string UnityActivity = "com.unity3d.player.UnityPlayerGameActivity";

        private static readonly (string json, string package, string activity)[] Dummies =
        {
            (@"{""version"":1,""demo_id"":""volley"",""title"":{""ja"":""バレーボール"",""en"":""Volleyball""},""minutes"":3,""supports"":{""haptics_toggle"":true},
               ""options"":[{""id"":""scene"",""label"":{""ja"":""モード""},""default"":""block"",""values"":[{""value"":""block"",""label"":{""ja"":""ブロック""}},{""value"":""receive"",""label"":{""ja"":""レシーブ""}}]},
               {""id"":""points"",""label"":{""ja"":""点数""},""default"":""7"",""when"":{""scene"":[""block""]},""values"":[{""value"":""3"",""label"":{""ja"":""3点先取""}},{""value"":""5"",""label"":{""ja"":""5点先取""}},{""value"":""7"",""label"":{""ja"":""7点先取""}}]},
               {""id"":""balls"",""label"":{""ja"":""球数""},""default"":""10"",""when"":{""scene"":[""receive""]},""values"":[{""value"":""10"",""label"":{""ja"":""10球""}},{""value"":""20"",""label"":{""ja"":""20球""}}]}]}",
             "jp.hapbeat.volley", UnityActivity),
            (@"{""version"":1,""demo_id"":""boxing"",""title"":{""ja"":""ボクシング"",""en"":""Boxing""},""minutes"":2,""supports"":{""haptics_toggle"":true},
               ""options"":[{""id"":""round"",""label"":{""ja"":""ラウンド""},""default"":""90"",""values"":[{""value"":""60"",""label"":{""ja"":""60秒""}},{""value"":""90"",""label"":{""ja"":""90秒""}}]}]}",
             "com.hapbeat.boxing", UnityActivity),
            (@"{""version"":1,""demo_id"":""trex-encounter"",""title"":{""ja"":""T-Rex エンカウンター"",""en"":""T-Rex Encounter""},""minutes"":3,""supports"":{""haptics_toggle"":true},""options"":[]}",
             "com.hapbeat.trexencounter", "com.epicgames.unreal.GameActivity"),
        };

        /// <summary>Editor preview catalog (no PackageManager in the Editor).</summary>
        public static IReadOnlyList<DemoSessionCatalogEntry> EditorDummy()
        {
            var result = new List<DemoSessionCatalogEntry>();
            foreach (var dummy in Dummies)
            {
                if (!DemoSessionDescriptor.TryParse(dummy.json, out var descriptor, out var error))
                    throw new InvalidOperationException("Invalid dummy descriptor: " + error);
                result.Add(new DemoSessionCatalogEntry(descriptor, dummy.package, dummy.activity));
            }
            return result;
        }
    }
}
