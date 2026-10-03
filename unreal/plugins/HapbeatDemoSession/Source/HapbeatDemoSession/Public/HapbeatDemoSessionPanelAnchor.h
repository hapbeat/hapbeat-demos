#pragma once
#include "CoreMinimal.h"
#include "Components/SceneComponent.h"
#include "HapbeatDemoSessionPanelAnchor.generated.h"

/**
 * Where the Demo Session completion panel appears in this demo (instead of in front of the head). Put one on
 * any actor of the level, or create it from code and place it (keep one per world: with several, the first one found
 * is used). The panel takes its location when it appears and then stays there (it does not follow the anchor or the head).
 * The pause panel always appears in front of the head.
 */
UCLASS(ClassGroup=(Hapbeat),meta=(BlueprintSpawnableComponent))
class HAPBEATDEMOSESSION_API UHapbeatDemoSessionPanelAnchor : public USceneComponent
{
    GENERATED_BODY()
public:
    /**
     * True: the panel takes this component's rotation (+X = the panel's face, toward the user; turned round
     * when the eye is behind it). False: only the location is used and the panel turns its yaw toward the eye.
     */
    UPROPERTY(EditAnywhere,BlueprintReadWrite,Category="Demo Session")
    bool bUseRotation=false;
    /** The first registered anchor found in World, or null. */
    static UHapbeatDemoSessionPanelAnchor* Find(const UWorld* World);
};
