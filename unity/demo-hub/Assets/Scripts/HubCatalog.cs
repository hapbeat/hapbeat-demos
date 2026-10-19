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

        private static readonly (string json, string package, string activity, string label)[] Dummies =
        {
            (@"{""version"":1,""demo_id"":""volley"",""title"":{""ja"":""バレーボール"",""en"":""Volleyball""},""minutes"":3,""supports"":{""haptics_toggle"":true},
               ""options"":[{""id"":""scene"",""label"":{""ja"":""モード""},""default"":""block"",""values"":[{""value"":""block"",""label"":{""ja"":""スパイク＋ブロック""}},{""value"":""match"",""label"":{""ja"":""6人制の試合""}},{""value"":""receive"",""label"":{""ja"":""レシーブ""}}]},
               {""id"":""tutorial"",""label"":{""ja"":""チュートリアル""},""default"":""on"",""when"":{""scene"":[""block"",""receive""]},""values"":[{""value"":""on"",""label"":{""ja"":""あり""}},{""value"":""off"",""label"":{""ja"":""なし""}}]},
               {""id"":""points"",""label"":{""ja"":""点数""},""default"":""4"",""when"":{""scene"":[""block"",""receive"",""match""]},""values"":[{""value"":""3"",""label"":{""ja"":""3点先取""}},{""value"":""4"",""label"":{""ja"":""4点先取""}},{""value"":""5"",""label"":{""ja"":""5点先取""}},{""value"":""7"",""label"":{""ja"":""7点先取""}}]}]}",
             "jp.hapbeat.volley", UnityActivity, "Volley"),
            (@"{""version"":1,""demo_id"":""boxing"",""title"":{""ja"":""ボクシング"",""en"":""Boxing""},""minutes"":2,""supports"":{""haptics_toggle"":true},
               ""options"":[{""id"":""round"",""label"":{""ja"":""ラウンド""},""default"":""55"",""values"":[{""value"":""30"",""label"":{""ja"":""30秒""}},{""value"":""55"",""label"":{""ja"":""55秒""}}]}]}",
             "com.hapbeat.boxing", UnityActivity, "Boxing"),
            (@"{""version"":1,""demo_id"":""trex-encounter"",""title"":{""ja"":""T-Rex エンカウンター"",""en"":""T-Rex Encounter""},""minutes"":3,""supports"":{""haptics_toggle"":true},""options"":[]}",
             "com.hapbeat.trexencounter", "com.epicgames.unreal.GameActivity", "T-Rex Encounter"),
        };

        /// <summary>Editor preview catalog (no PackageManager in the Editor); the labels stand in for the Quest app names.</summary>
        public static IReadOnlyList<DemoSessionCatalogEntry> EditorDummy()
        {
            var result = new List<DemoSessionCatalogEntry>();
            foreach (var dummy in Dummies)
            {
                if (!DemoSessionDescriptor.TryParse(dummy.json, out var descriptor, out var error))
                    throw new InvalidOperationException("Invalid dummy descriptor: " + error);
                result.Add(new DemoSessionCatalogEntry(descriptor, dummy.package, dummy.activity, dummy.label));
            }
            return result;
        }
    }
}
