using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    public class BallImpactKitTests
    {
        [Test]
        public void AllFifteenImpactEntriesReferenceFiveManifestRegisteredKitClips()
        {
            const string kit="Assets/GloveBallDemo/Kits/gloveball-kit";
            var manifest=File.ReadAllText(kit+"/gloveball-kit-manifest.json");
            var map=AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/GloveBallDemo/Haptics/BallImpactEventMap.asset");
            var entries=new SerializedObject(map).FindProperty("entries");
            foreach(var kind in new[]{"Bowling","Volleyball","Foam","Basketball","Perforated"})
            {
                var path=kit+"/stream-clips/"+kind.ToLowerInvariant()+"_impact.wav";
                var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assert.That(clip,Is.Not.Null,path);
                Assert.That(File.Exists("Assets/GloveBallDemo/Art/Balls/"+kind+"Impact.wav"),Is.False);
                foreach(var side in new[]{"l_arm_collide","r_arm_collide","body_collide"})
                {
                    var name=kind.ToLowerInvariant()+"_"+side;
                    Assert.That(manifest,Does.Contain("\"gloveball-kit."+name+"\""));
                    bool found=false;
                    for(int i=0;i<entries.arraySize;i++)
                    {
                        var entry=entries.GetArrayElementAtIndex(i);
                        if(entry.FindPropertyRelative("eventName").stringValue!=name) continue;
                        Assert.That(entry.FindPropertyRelative("streamClip").objectReferenceValue,Is.EqualTo(clip));
                        found=true;
                    }
                    Assert.That(found,Is.True,name);
                }
            }
        }
    }
}
