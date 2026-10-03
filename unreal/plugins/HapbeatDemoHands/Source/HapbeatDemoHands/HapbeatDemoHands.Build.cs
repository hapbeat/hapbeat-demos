using UnrealBuildTool;

public class HapbeatDemoHands : ModuleRules
{
	public HapbeatDemoHands(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine", "HeadMountedDisplay" });
	}
}
