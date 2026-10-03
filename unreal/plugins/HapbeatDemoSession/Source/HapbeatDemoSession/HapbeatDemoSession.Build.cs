using UnrealBuildTool;

public class HapbeatDemoSession : ModuleRules
{
    public HapbeatDemoSession(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine" });
        PrivateDependencyModuleNames.AddRange(new string[] { "InputCore", "Json", "Sockets", "Networking", "Slate", "SlateCore", "UMG", "HeadMountedDisplay", "XRBase", "OpenXRHMD", "HapbeatSDK" });
        // IOpenXRExtensionPlugin (pause: the hand menu gesture as the left menu button).
        AddEngineThirdPartyPrivateStaticDependencies(Target, "OpenXR");
        // HMAC-SHA256 for Demo Switch authentication (the engine's OpenSSL also links on Android).
        AddEngineThirdPartyPrivateStaticDependencies(Target, "OpenSSL");
        if (Target.Platform == UnrealTargetPlatform.Android)
        {
            PrivateDependencyModuleNames.Add("Launch");
            AdditionalPropertiesForReceipt.Add("AndroidPlugin", System.IO.Path.Combine(ModuleDirectory, "HapbeatDemoSession_APL.xml"));
        }
    }
}
