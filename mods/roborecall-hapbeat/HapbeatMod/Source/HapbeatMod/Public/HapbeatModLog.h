// Shared log category. Filter the editor's Output Log by "LogHapbeatMod" to see
// only this plugin's messages while wiring Blueprints.

#pragma once

#include "CoreMinimal.h"
// VERIFY-4.16: 4.16 path for DECLARE_LOG_CATEGORY_EXTERN. Drop the folder prefix
// ("LogMacros.h") if the fork predates the IWYU header move.
#include "Logging/LogMacros.h"

DECLARE_LOG_CATEGORY_EXTERN(LogHapbeatMod, Log, All);
