#pragma once
#include "CoreMinimal.h"
#include "UObject/Object.h"
#include "HeadMountedDisplayTypes.h"
#include "HapbeatDemoHand.generated.h"

class AActor;
class UMaterialInstanceDynamic;
class UMaterialInterface;
class UPoseableMeshComponent;
class USkeletalMesh;
class UTexture2D;

/** Skin: textured, natural hand. Ghost: Quest-style dark fill with a light outline. */
UENUM(BlueprintType)
enum class EHapbeatHandStyle : uint8 { Skin, Ghost };

/**
 * Reference pose of Meta's OpenXR hand mesh. Its bones are XRHand_<joint> in EHandKeypoint order, so each finger
 * bone is aimed at its tracked child joint; the hand root follows the palm frame (wrist -> middle knuckle,
 * little -> index knuckle) and scales with the palm length.
 */
struct HAPBEATDEMOHANDS_API FHapbeatHandRig
{
    FName HandBone;
    TArray<FName> Bones;
    TArray<int32> From, To, ChainStart; // chains in order: index, middle, ring, little, thumb
    TArray<FQuat> RefRot;
    TArray<FVector> RefLoc, RefDir;
    FVector HandLoc=FVector::ZeroVector, RefForward=FVector::ForwardVector, RefSide=FVector::RightVector;
    float RefLength=1;

    bool Init(const USkeletalMesh* Asset);
    bool IsValid() const { return !Bones.IsEmpty(); }
    /** Poses Mesh from the 26 joints (directions only). False, mesh untouched, when the joints are unusable. */
    bool Pose(UPoseableMeshComponent* Mesh,const FXRHandTrackingState& State,const FVector& Offset=FVector::ZeroVector) const;
    /**
     * The mesh's own reference-pose joints placed at World (origin = wrist), each finger bent towards the palm in
     * its own plane by CurlDegrees per joint, or by ChainCurls[5] per finger (index, middle, ring, little, thumb).
     * Used for illustrations and synthetic review hands.
     */
    bool BuildReferenceSample(const FTransform& World,float CurlDegrees,FXRHandTrackingState& Out,const float* ChainCurls=nullptr) const;
};

/** The shared materials (Scripts/create_hand_materials.py -> /Game/HapbeatDemoHands/Materials). */
USTRUCT()
struct HAPBEATDEMOHANDS_API FHapbeatDemoHandMaterials
{
    GENERATED_BODY()
    UPROPERTY() TObjectPtr<UMaterialInterface> Depth;
    UPROPERTY() TObjectPtr<UMaterialInterface> Skin;
    UPROPERTY() TObjectPtr<UMaterialInterface> Ghost;
    UPROPERTY() TObjectPtr<UMaterialInterface> Outline;
    UPROPERTY() TObjectPtr<UMaterialInterface> Inside;
    /** Loads them from /Game/HapbeatDemoHands/Materials. Call from a constructor (ConstructorHelpers) so they cook. */
    static FHapbeatDemoHandMaterials FindInConstructor();
};

/**
 * One drawn hand: copies of the same posed mesh. The Custom Depth copy is never drawn (it is the stand-in for a
 * depth pre-pass); the outline shell and the visible surface draw only the hand's nearest layer and fade out just
 * past the wrist, where Meta's mesh ends; the inner shell (Skin) shows the inside through that opening.
 * Keep a UPROPERTY reference to it in the owner.
 */
UCLASS()
class HAPBEATDEMOHANDS_API UHapbeatDemoHand : public UObject
{
    GENERATED_BODY()
public:
    static UHapbeatDemoHand* Create(AActor* Owner,USkeletalMesh* Mesh,UTexture2D* SkinTexture,const FHapbeatDemoHandMaterials& Materials,EHapbeatHandStyle Style);
    /** Poses all layers from the joints and shows them; hides the hand when tracking is unusable. */
    bool Update(const FXRHandTrackingState& State,const FVector& Offset=FVector::ZeroVector);
    void SetVisible(bool bVisible);
    bool IsVisible() const;
    void SetStyle(EHapbeatHandStyle InStyle);
    EHapbeatHandStyle GetStyle() const { return Style; }
    /**
     * Skin only. True: an opaque inner shell shows the inside of the hand through the wrist opening. False (default):
     * the opening shows the scene behind (the Safety Mill look of 2026-10-01; the shell was tried in T-Rex on
     * 2026-10-03 and the softer see-through wrist was preferred).
     */
    void SetInsideVisible(bool bVisible);
    /**
     * Translucent sort priority of the drawn layers: outline at Base, surface (and inner shell) at Base+1. Default 1.
     * Demos with the Demo Session panels (drawn without depth test) pass AHapbeatDemoSessionUi::HandSortPriority so a
     * hand in front of a panel stays visible.
     */
    void SetSortPriorityBase(int32 Base);
    /** The Custom Depth copy: query bones here (all layers share its pose). */
    UPoseableMeshComponent* GetPoseMesh() const { return Layers.IsEmpty()?nullptr:Layers[0].Get(); }
    UMaterialInstanceDynamic* GetSurfaceMaterial() const { return SurfaceMaterial; }
    UMaterialInstanceDynamic* GetOutlineMaterial() const { return OutlineMaterial; }
    const FHapbeatHandRig& GetRig() const { return Rig; }
private:
    void ApplyStyle();
    UPROPERTY() TArray<TObjectPtr<UPoseableMeshComponent>> Layers; // depth, outline, surface, inside
    UPROPERTY() TObjectPtr<UMaterialInstanceDynamic> InsideMaterial;
    UPROPERTY() TObjectPtr<UMaterialInstanceDynamic> SurfaceMaterial;
    UPROPERTY() TObjectPtr<UMaterialInstanceDynamic> OutlineMaterial;
    UPROPERTY() FHapbeatDemoHandMaterials Materials;
    UPROPERTY() TObjectPtr<UTexture2D> SkinTexture;
    FHapbeatHandRig Rig;
    EHapbeatHandStyle Style=EHapbeatHandStyle::Skin;
    bool bInsideVisible=false,bShown=false;
    bool IsLayerShown(int32 Layer) const;
};
