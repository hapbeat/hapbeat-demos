#pragma once
#include "CoreMinimal.h"
#include "UObject/Object.h"
#include "HeadMountedDisplayTypes.h"
#include "HapbeatDemoHand.generated.h"

class AActor;
class UInstancedStaticMeshComponent;
class UMaterialInstanceDynamic;
class UMaterialInterface;
class UPoseableMeshComponent;
class USkeletalMesh;
class UStaticMesh;
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
    /** Reference-pose palm normal (unit), on the side of the thumb, so it is right for the left and the right mesh. */
    FVector RefPalm=FVector::UpVector;
    float RefLength=1;

    bool Init(const USkeletalMesh* Asset);
    /** A rough built-in layout of the 26 joints instead of a mesh's reference pose, for the placeholder hand. */
    void InitPlaceholder(bool bRightHand);
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
    /** The placeholder hand (no Meta mesh): its material, null -> the engine shape's own material. */
    UPROPERTY() TObjectPtr<UMaterialInterface> Placeholder;
    UPROPERTY() TObjectPtr<UStaticMesh> PlaceholderJoint;
    UPROPERTY() TObjectPtr<UStaticMesh> PlaceholderBone;
    /**
     * Loads them from /Game/HapbeatDemoHands/Materials and the placeholder shapes from /Engine/BasicShapes. Call from
     * a constructor (ConstructorHelpers) so they cook. A material that is not in the project stays null, quietly.
     */
    static FHapbeatDemoHandMaterials FindInConstructor();
    /**
     * For third-party content that may not be installed (Meta's hand meshes, the skin textures baked from them): the
     * asset, or null without the "CDO Constructor ... Failed to find" error. Call from a constructor, so they cook.
     */
    static USkeletalMesh* FindOptionalMesh(const TCHAR* Path);
    static UTexture2D* FindOptionalTexture(const TCHAR* Path);
};

/**
 * One drawn hand: copies of the same posed mesh. The Custom Depth copy is never drawn (it is the stand-in for a
 * depth pre-pass); the outline shell and the visible surface draw only the hand's nearest layer and fade out just
 * past the wrist, where Meta's mesh ends; the inner shell (Skin) shows the inside through that opening.
 * Without a mesh (Create with Mesh null) it is a placeholder instead: spheres on the joints and cylinders between them,
 * from the same tracked joints, with the same calls. See IsPlaceholder().
 * Keep a UPROPERTY reference to it in the owner.
 */
UCLASS()
class HAPBEATDEMOHANDS_API UHapbeatDemoHand : public UObject
{
    GENERATED_BODY()
public:
    /** Mesh null: the placeholder hand. bRightHand only picks the side of its built-in reference pose (GetRig). */
    static UHapbeatDemoHand* Create(AActor* Owner,USkeletalMesh* Mesh,UTexture2D* SkinTexture,const FHapbeatDemoHandMaterials& Materials,EHapbeatHandStyle Style,bool bRightHand=false);
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
    /** True when drawn from engine shapes because the mesh is missing: GetPoseMesh() and GetOutlineMaterial() are null. */
    bool IsPlaceholder() const { return !PlaceholderParts.IsEmpty(); }
    /** The Custom Depth copy: query bones here (all layers share its pose). */
    UPoseableMeshComponent* GetPoseMesh() const { return Layers.IsEmpty()?nullptr:Layers[0].Get(); }
    UMaterialInstanceDynamic* GetSurfaceMaterial() const { return SurfaceMaterial; }
    UMaterialInstanceDynamic* GetOutlineMaterial() const { return OutlineMaterial; }
    const FHapbeatHandRig& GetRig() const { return Rig; }
private:
    void ApplyStyle();
    bool UpdatePlaceholder(const FXRHandTrackingState& State,const FVector& Offset);
    UPROPERTY() TArray<TObjectPtr<UPoseableMeshComponent>> Layers; // depth, outline, surface, inside
    UPROPERTY() TArray<TObjectPtr<UInstancedStaticMeshComponent>> PlaceholderParts; // joints, bones
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
