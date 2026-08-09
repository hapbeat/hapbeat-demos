// Module entry point. Deliberately does nothing at startup: the UDP socket is
// opened lazily the first time a Blueprint fires an event, so simply having the
// plugin installed never touches the network.

#include "HapbeatBlueprintLibrary.h"
#include "HapbeatModLog.h"

// VERIFY-4.16: 4.16 path. Use "ModuleManager.h" if the fork predates the move.
#include "Modules/ModuleManager.h"

DEFINE_LOG_CATEGORY(LogHapbeatMod);

class FHapbeatModModule : public IModuleInterface
{
public:
	virtual void StartupModule() override
	{
		UE_LOG(LogHapbeatMod, Log, TEXT("HapbeatMod loaded. Filter the Output Log by 'LogHapbeatMod'."));
	}

	virtual void ShutdownModule() override
	{
		// Sends CONNECT_STATUS(connected=false) and joins the receiver thread.
		// Note this runs at editor/game exit, not when a PIE session stops — use
		// the Hapbeat Stop All node on EndPlay if you want silence between runs.
		UHapbeatBlueprintLibrary::ShutdownSender();
	}
};

IMPLEMENT_MODULE(FHapbeatModModule, HapbeatMod)
