using System.Runtime.CompilerServices;
using UnityEngine.Scripting;

[assembly: InternalsVisibleTo("Hapbeat.DemoSwitch.HapbeatSdk.Tests.Editor")]
// Nothing references this assembly; without this the managed linker drops it, and with it the
// RuntimeInitializeOnLoadMethod bootstrap.
[assembly: AlwaysLinkAssembly]
