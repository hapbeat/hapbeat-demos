using UnrealBuildTool;

/// <summary>
/// Module rules for the Hapbeat Robo Recall mod.
///
/// NOTE ON THE CONSTRUCTOR SIGNATURE (the single most version-sensitive line here):
/// the Robo Recall Mod Kit ships a customized engine sitting between UE 4.15 and
/// 4.16 (confirmed by Epic staff on the UE forums). That generation's UnrealBuildTool
/// still expects the OLD `TargetInfo Target` constructor. Real mods shipped for this
/// exact kit (e.g. 3DRudder's `Rudder` plugin, Oct 2017) use it. Every modern
/// UE4.17+/UE5 template instead uses
///
///     public HapbeatMod(ReadOnlyTargetRules Target) : base(Target)
///
/// which will NOT compile here. If, on your install, UBT complains that it cannot
/// find a suitable constructor, swap to the modern form above — but try this one
/// first. See README.md > 実機確認チェックリスト item 3.
/// </summary>
public class HapbeatMod : ModuleRules
{
	public HapbeatMod(TargetInfo Target)
	{
		// Matches the shipped Rudder mod for this Mod Kit. A self-contained mod
		// module should not join the engine's shared PCH chain.
		PCHUsage = PCHUsageMode.NoSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[]
		{
			"Core",
			"CoreUObject",
			"Engine",
			"Sockets",      // FSocket / ISocketSubsystem
			"Networking",   // FUdpSocketBuilder / FUdpSocketReceiver
			"Projects",     // IPluginManager — locates this plugin's Config/ directory
		});

		PrivateIncludePaths.AddRange(new string[]
		{
			"HapbeatMod/Private",
		});
	}
}
