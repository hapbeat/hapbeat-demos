#include "HapbeatModConfig.h"

#include "HapbeatModLog.h"

// VERIFY-4.16: header paths. These are the UE 4.16 (IWYU-era) locations. If the
// Mod Kit's engine fork predates the move, drop the folder prefix
// ("FileHelper.h", "Paths.h") — the types themselves have existed since UE 4.0.
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
// VERIFY-4.16: IPluginManager lives in the "Projects" module (already a
// dependency in HapbeatMod.Build.cs) at this path in 4.16.
#include "Interfaces/IPluginManager.h"

namespace
{
	const TCHAR* kPluginName = TEXT("HapbeatMod");
	const TCHAR* kConfigFileName = TEXT("HapbeatMod.txt");

	/// Trim ASCII whitespace from both ends.
	///
	/// Hand-rolled on purpose: FString's trimming API was renamed across engine
	/// generations (Trim()/TrimTrailing() in the 4.16 era, TrimStartAndEnd() from
	/// 4.22), and this plugin has to compile against a fork of the older one.
	/// Character comparison has never changed.
	FString Trimmed(const FString& In)
	{
		int32 Start = 0;
		int32 End = In.Len();
		while (Start < End && FChar::IsWhitespace(In[Start]))
		{
			++Start;
		}
		while (End > Start && FChar::IsWhitespace(In[End - 1]))
		{
			--End;
		}
		return In.Mid(Start, End - Start);
	}

	/// Splits "key=value" once, trimming whitespace on both sides. Returns false
	/// for blank lines, comments ('#' or ';'), and lines with no '='.
	bool SplitKeyValue(const FString& RawLine, FString& OutKey, FString& OutValue)
	{
		const FString Line = Trimmed(RawLine);
		if (Line.IsEmpty() || Line.StartsWith(TEXT("#")) || Line.StartsWith(TEXT(";")))
		{
			return false;
		}

		int32 Eq = INDEX_NONE;
		if (!Line.FindChar(TEXT('='), Eq))
		{
			return false;
		}

		OutKey = Trimmed(Line.Left(Eq));
		OutValue = Trimmed(Line.Mid(Eq + 1));
		return !OutKey.IsEmpty();
	}

	bool ParseBool(const FString& Value, bool bFallback)
	{
		if (Value.Equals(TEXT("true"), ESearchCase::IgnoreCase) || Value == TEXT("1"))
		{
			return true;
		}
		if (Value.Equals(TEXT("false"), ESearchCase::IgnoreCase) || Value == TEXT("0"))
		{
			return false;
		}
		return bFallback;
	}
}

FHapbeatModConfig::FHapbeatModConfig()
	: bEnabled(true)
	, AppName(TEXT("RoboRecall"))
	, Group(-1)
	, Player(-1)
	, MasterGain(1.0f)
	, MinIntervalMs(60)
	, bCommandUnicast(true)
	, Port(7700)
{
}

FHapbeatModConfig FHapbeatModConfig::CreateDefault()
{
	FHapbeatModConfig C;
	// The three events this mod's Blueprint nodes fire, bound to vr-shooter-kit
	// (kits/vr-shooter-kit/manifest.json is the authority on these ids).
	C.Events.Add(TEXT("fire"), FHapbeatEventBinding(TEXT("vr-shooter-kit.shot_recoil"), 1.0f, true));
	C.Events.Add(TEXT("kill"), FHapbeatEventBinding(TEXT("vr-shooter-kit.kill_confirm"), 0.9f, true));
	C.Events.Add(TEXT("player_hit"), FHapbeatEventBinding(TEXT("vr-shooter-kit.hit_heavy"), 1.0f, true));
	// Extra bindings available to HapbeatSendCustom from Blueprint without an edit here.
	C.Events.Add(TEXT("hit_light"), FHapbeatEventBinding(TEXT("vr-shooter-kit.hit_light"), 0.8f, true));
	C.Events.Add(TEXT("reload"), FHapbeatEventBinding(TEXT("vr-shooter-kit.reload_click"), 0.8f, true));
	C.Events.Add(TEXT("heartbeat"), FHapbeatEventBinding(TEXT("vr-shooter-kit.heartbeat"), 0.9f, true));
	return C;
}

FString FHapbeatModConfig::GetConfigFilePath()
{
	// IPluginManager keeps this correct wherever the Mod Kit is installed, and
	// sidesteps the FPaths::GameDir()/ProjectDir() rename between engine
	// generations entirely.
	TSharedPtr<IPlugin> Plugin = IPluginManager::Get().FindPlugin(kPluginName);
	if (!Plugin.IsValid())
	{
		UE_LOG(LogHapbeatMod, Warning,
			TEXT("Plugin '%s' not found by IPluginManager; using built-in defaults."), kPluginName);
		return FString();
	}
	return FPaths::Combine(*Plugin->GetBaseDir(), TEXT("Config"), kConfigFileName);
}

const FHapbeatEventBinding* FHapbeatModConfig::FindEvent(const FString& LogicalEvent) const
{
	if (LogicalEvent.IsEmpty())
	{
		return nullptr;
	}
	// TMap<FString,...> hashes case-insensitively, so "Fire" finds "fire".
	return Events.Find(LogicalEvent);
}

FHapbeatModConfig FHapbeatModConfig::LoadOrCreate(const FString& Path)
{
	FHapbeatModConfig C = CreateDefault();

	if (Path.IsEmpty())
	{
		return C;
	}

	// Existence is checked BEFORE the read, and the seed-defaults write below only
	// runs when the file is genuinely absent. A file that exists but cannot be read
	// (share lock, antivirus scan, ACLs that allow write but not read) must never be
	// overwritten: that would destroy a tuned config on a transient failure, right
	// before a demo. Same contract as the C# reference
	// (mods/shared/HapbeatModCore/HapbeatModSettings.cs LoadOrCreate, whose tests
	// pin "a bad file is left on disk for the user to fix").
	const bool bFileExists = FPaths::FileExists(Path);

	TArray<FString> Lines;
	if (!FFileHelper::LoadFileToStringArray(Lines, *Path))
	{
		if (bFileExists)
		{
			UE_LOG(LogHapbeatMod, Warning,
				TEXT("%s exists but could not be read; using built-in defaults and LEAVING THE FILE ")
				TEXT("UNTOUCHED. Close anything holding the file open, or fix its permissions."), *Path);
			return C;
		}

		// First run: seed a file so the user has something to edit. A read-only
		// install (e.g. Program Files without elevation) just keeps the defaults.
		if (FFileHelper::SaveStringToFile(C.ToText(), *Path))
		{
			UE_LOG(LogHapbeatMod, Log, TEXT("Wrote default settings to %s"), *Path);
		}
		else
		{
			UE_LOG(LogHapbeatMod, Warning,
				TEXT("Could not create %s; using built-in defaults."), *Path);
		}
		return C;
	}

	// A settings file that defines ANY event replaces the default event map
	// wholesale, so a user can remove a binding rather than only override it.
	bool bClearedEvents = false;

	for (int32 i = 0; i < Lines.Num(); ++i)
	{
		FString Key, Value;
		if (!SplitKeyValue(Lines[i], Key, Value))
		{
			continue;
		}

		if (Key.Equals(TEXT("enabled"), ESearchCase::IgnoreCase))          { C.bEnabled = ParseBool(Value, C.bEnabled); }
		else if (Key.Equals(TEXT("appName"), ESearchCase::IgnoreCase))     { C.AppName = Value; }
		else if (Key.Equals(TEXT("group"), ESearchCase::IgnoreCase))       { C.Group = FCString::Atoi(*Value); }
		else if (Key.Equals(TEXT("player"), ESearchCase::IgnoreCase))      { C.Player = FCString::Atoi(*Value); }
		else if (Key.Equals(TEXT("masterGain"), ESearchCase::IgnoreCase))  { C.MasterGain = FCString::Atof(*Value); }
		else if (Key.Equals(TEXT("minIntervalMs"), ESearchCase::IgnoreCase)) { C.MinIntervalMs = FCString::Atoi(*Value); }
		else if (Key.Equals(TEXT("commandUnicast"), ESearchCase::IgnoreCase)) { C.bCommandUnicast = ParseBool(Value, C.bCommandUnicast); }
		else if (Key.Equals(TEXT("port"), ESearchCase::IgnoreCase))        { C.Port = FCString::Atoi(*Value); }
		else if (Key.Equals(TEXT("broadcastAddress"), ESearchCase::IgnoreCase)) { C.BroadcastAddress = Value; }
		else if (Key.StartsWith(TEXT("event."), ESearchCase::IgnoreCase))
		{
			// event.<logical>.id / .gain / .enabled
			const FString Rest = Key.Mid(6);
			int32 Dot = INDEX_NONE;
			if (!Rest.FindLastChar(TEXT('.'), Dot) || Dot <= 0)
			{
				UE_LOG(LogHapbeatMod, Warning, TEXT("Ignoring malformed settings line %d: '%s'"),
					i + 1, *Lines[i]);
				continue;
			}
			const FString Logical = Rest.Left(Dot);
			const FString Field = Rest.Mid(Dot + 1);

			if (!bClearedEvents)
			{
				C.Events.Empty();
				bClearedEvents = true;
			}

			FHapbeatEventBinding& Binding = C.Events.FindOrAdd(Logical);
			if (Field.Equals(TEXT("id"), ESearchCase::IgnoreCase))            { Binding.EventId = Value; }
			else if (Field.Equals(TEXT("gain"), ESearchCase::IgnoreCase))     { Binding.Gain = FCString::Atof(*Value); }
			else if (Field.Equals(TEXT("enabled"), ESearchCase::IgnoreCase))  { Binding.bEnabled = ParseBool(Value, true); }
			else
			{
				UE_LOG(LogHapbeatMod, Warning, TEXT("Unknown event field '%s' on line %d."), *Field, i + 1);
			}
		}
		else
		{
			UE_LOG(LogHapbeatMod, Warning, TEXT("Unknown settings key '%s' on line %d."), *Key, i + 1);
		}
	}

	// A binding with no event id would send an empty PLAY; drop it loudly instead.
	for (TMap<FString, FHapbeatEventBinding>::TIterator It(C.Events); It; ++It)
	{
		if (It.Value().EventId.IsEmpty())
		{
			UE_LOG(LogHapbeatMod, Warning,
				TEXT("Logical event '%s' has no id= line; removing it."), *It.Key());
			It.RemoveCurrent();
		}
	}

	UE_LOG(LogHapbeatMod, Log, TEXT("Loaded settings from %s (%d event bindings)."),
		*Path, C.Events.Num());
	return C;
}

FString FHapbeatModConfig::ToText() const
{
	FString S;
	S += TEXT("# Hapbeat mod settings (Robo Recall).\n");
	S += TEXT("# Edit and restart the game / PIE session to apply, or call\n");
	S += TEXT("# HapbeatReloadSettings from Blueprint.\n");
	S += TEXT("# Lines starting with # are comments.\n\n");
	S += FString::Printf(TEXT("enabled=%s\n"), bEnabled ? TEXT("true") : TEXT("false"));
	S += FString::Printf(TEXT("appName=%s\n"), *AppName);
	S += TEXT("# player / group: 1-99 to force every target, -1 to disable.\n");
	S += FString::Printf(TEXT("player=%d\n"), Player);
	S += FString::Printf(TEXT("group=%d\n"), Group);
	S += FString::Printf(TEXT("masterGain=%.3f\n"), MasterGain);
	S += TEXT("# Minimum gap between two fires of the SAME logical event (ms). 0 disables.\n");
	S += FString::Printf(TEXT("minIntervalMs=%d\n"), MinIntervalMs);
	S += TEXT("# false = always broadcast instead of unicasting to devices that answered a PING.\n");
	S += FString::Printf(TEXT("commandUnicast=%s\n"), bCommandUnicast ? TEXT("true") : TEXT("false"));
	S += FString::Printf(TEXT("port=%d\n"), Port);
	S += TEXT("\n# Where discovery goes before a device answers. Empty = work it out\n");
	S += TEXT("# (one subnet-directed address per adapter + 255.255.255.255).\n");
	S += TEXT("# Set this by hand if nothing is found: 255.255.255.255 alone only\n");
	S += TEXT("# leaves through the adapter with the lowest metric, which on a PC\n");
	S += TEXT("# running Hyper-V / WSL2 / Docker is often a virtual switch with no\n");
	S += TEXT("# device behind it. Automatic detection assumes /24, so a /16 or /25\n");
	S += TEXT("# network needs the real address here (e.g. 192.168.255.255).\n");
	S += FString::Printf(TEXT("broadcastAddress=%s\n"), *BroadcastAddress);
	S += TEXT("\n# Logical event -> kit event id. Defining ANY event here replaces the\n");
	S += TEXT("# whole default map, so list every event you want.\n");

	for (TMap<FString, FHapbeatEventBinding>::TConstIterator It(Events); It; ++It)
	{
		S += FString::Printf(TEXT("\nevent.%s.id=%s\n"), *It.Key(), *It.Value().EventId);
		S += FString::Printf(TEXT("event.%s.gain=%.3f\n"), *It.Key(), It.Value().Gain);
		S += FString::Printf(TEXT("event.%s.enabled=%s\n"), *It.Key(),
			It.Value().bEnabled ? TEXT("true") : TEXT("false"));
	}

	return S;
}
