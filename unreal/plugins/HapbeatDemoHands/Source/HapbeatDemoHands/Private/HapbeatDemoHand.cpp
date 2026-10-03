#include "HapbeatDemoHand.h"
#include "Components/PoseableMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/Texture2D.h"
#include "GameFramework/Actor.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Modules/ModuleManager.h"
#include "UObject/ConstructorHelpers.h"

IMPLEMENT_MODULE(FDefaultModuleImpl, HapbeatDemoHands)

namespace
{
    // Each chain lists its bones; the joint after the last one (the tip) gives the distal aim.
    struct FFingerChain { int32 Base; int32 Joints; };
    constexpr FFingerChain FingerChains[]={{6,4},{11,4},{16,4},{21,4},{2,3}}; // index, middle, ring, little, thumb
    FName JointName(int32 Joint) { return *(TEXT("XRHand_")+StaticEnum<EHandKeypoint>()->GetNameStringByValue(Joint)); }
    constexpr int32 WristJoint=int32(EHandKeypoint::Wrist),PalmJoint=int32(EHandKeypoint::Palm),
        MiddleProximal=int32(EHandKeypoint::MiddleProximal),IndexProximal=int32(EHandKeypoint::IndexProximal),
        LittleProximal=int32(EHandKeypoint::LittleProximal);
}

bool FHapbeatHandRig::Init(const USkeletalMesh* Asset)
{
    *this=FHapbeatHandRig();
    if(!Asset)return false;
    const FReferenceSkeleton& Ref=Asset->GetRefSkeleton();
    TArray<FTransform> CS;CS.SetNum(Ref.GetNum());
    for(int32 I=0;I<Ref.GetNum();++I){const int32 P=Ref.GetParentIndex(I);CS[I]=P==INDEX_NONE?Ref.GetRefBonePose()[I]:Ref.GetRefBonePose()[I]*CS[P];}
    bool bOk=true;
    auto Loc=[&](int32 Joint){const int32 I=Ref.FindBoneIndex(JointName(Joint));bOk&=I!=INDEX_NONE;return I==INDEX_NONE?FVector::ZeroVector:CS[I].GetLocation();};
    for(const FFingerChain& C:FingerChains) {
        ChainStart.Add(Bones.Num());
        for(int32 J=0;J<C.Joints;++J) {
            const int32 I=Ref.FindBoneIndex(JointName(C.Base+J));
            if(I==INDEX_NONE){bOk=false;break;}
            Bones.Add(JointName(C.Base+J));From.Add(C.Base+J);To.Add(C.Base+J+1);
            RefRot.Add(CS[I].GetRotation());RefLoc.Add(CS[I].GetLocation());
            RefDir.Add((Loc(C.Base+J+1)-CS[I].GetLocation()).GetSafeNormal());
        }
    }
    HandBone=JointName(WristJoint);HandLoc=Loc(WristJoint);
    RefForward=Loc(MiddleProximal)-HandLoc;RefSide=Loc(IndexProximal)-Loc(LittleProximal);
    RefLength=FMath::Max(1.f,RefForward.Size());
    if(!bOk)Bones.Reset();
    return bOk;
}

bool FHapbeatHandRig::Pose(UPoseableMeshComponent* Mesh,const FXRHandTrackingState& S,const FVector& Offset) const
{
    if(!Mesh||!IsValid()||S.HandKeyLocations.Num()!=EHandKeypointCount)return false;
    const TArray<FVector>& K=S.HandKeyLocations;
    const FVector Forward=K[MiddleProximal]-K[WristJoint],Sideways=K[IndexProximal]-K[LittleProximal];
    if(Forward.Size()<1||Sideways.Size()<1||FVector::Parallel(Forward.GetSafeNormal(),Sideways.GetSafeNormal()))return false;
    const FQuat Root=FRotationMatrix::MakeFromXY(Forward,Sideways).ToQuat()*FRotationMatrix::MakeFromXY(RefForward,RefSide).ToQuat().Inverse();
    const float Scale=FMath::Clamp(Forward.Size()/RefLength,.8f,1.3f);
    Mesh->SetWorldTransform(FTransform(Root,K[WristJoint]+Offset-Root.RotateVector(HandLoc*Scale),FVector(Scale)));
    for(int32 C=0;C<ChainStart.Num();++C) {
        const int32 End=C+1<ChainStart.Num()?ChainStart[C+1]:Bones.Num();
        FQuat Parent=FQuat::Identity; // metacarpals hang off the wrist bone, which keeps its reference pose
        for(int32 B=ChainStart[C];B<End;++B) {
            const FVector Want=Root.UnrotateVector(K[To[B]]-K[From[B]]).GetSafeNormal();
            if(Want.IsNearlyZero())break;
            const FQuat Delta=FQuat::FindBetweenNormals(Parent.RotateVector(RefDir[B]),Want)*Parent;
            FTransform T=Mesh->GetBoneTransformByName(Bones[B],EBoneSpaces::ComponentSpace);
            T.SetRotation(Delta*RefRot[B]);
            Mesh->SetBoneTransformByName(Bones[B],T,EBoneSpaces::ComponentSpace);
            Parent=Delta;
        }
    }
    return true;
}

bool FHapbeatHandRig::BuildReferenceSample(const FTransform& World,float CurlDegrees,FXRHandTrackingState& Out,const float* ChainCurls) const
{
    if(!IsValid())return false;
    Out=FXRHandTrackingState();Out.bValid=true;Out.TrackingStatus=ETrackingStatus::Tracked;
    TArray<FVector> K;K.Init(HandLoc,EHandKeypointCount);
    K[PalmJoint]=HandLoc+RefForward*.5f;
    for(int32 C=0;C<ChainStart.Num();++C) {
        // Each finger bends in its own plane, like a hinge joint: the hand's side axis made perpendicular to the
        // finger's proximal bone (one hand-wide axis twisted the splayed fingers sideways). For this rig
        // forward x index-side is the palm normal, so rotating about -side curls towards the palm.
        const int32 Start=ChainStart[C],End=C+1<ChainStart.Num()?ChainStart[C+1]:Bones.Num();
        const FVector Bone=RefDir[FMath::Min(Start+1,End-1)];
        const FVector Axis=-(RefSide-Bone*FVector::DotProduct(RefSide,Bone)).GetSafeNormal();
        const FQuat Curl(Axis.IsNearlyZero()?-RefSide.GetSafeNormal():Axis,FMath::DegreesToRadians(ChainCurls?ChainCurls[C]:CurlDegrees));
        FVector P=RefLoc[Start];FQuat Acc=FQuat::Identity;K[From[Start]]=P;
        for(int32 B=Start;B<End;++B) {
            if(B>Start)Acc=Curl*Acc;
            const float Length=B+1<End?(RefLoc[B+1]-RefLoc[B]).Size():(RefLoc[B]-RefLoc[B-1]).Size()*.8f;
            P+=Acc.RotateVector(RefDir[B])*Length;K[To[B]]=P;
        }
    }
    for(FVector& P:K)P=World.TransformPosition(P-HandLoc); // world origin = wrist
    Out.HandKeyLocations=K;Out.HandKeyRotations.Init(World.GetRotation(),EHandKeypointCount);Out.HandKeyRadii.Init(.8f,EHandKeypointCount);
    return true;
}

FHapbeatDemoHandMaterials FHapbeatDemoHandMaterials::FindInConstructor()
{
    FHapbeatDemoHandMaterials M;
    M.Depth=ConstructorHelpers::FObjectFinder<UMaterialInterface>(TEXT("/Game/HapbeatDemoHands/Materials/M_HandDepth.M_HandDepth")).Object;
    M.Skin=ConstructorHelpers::FObjectFinder<UMaterialInterface>(TEXT("/Game/HapbeatDemoHands/Materials/M_SkinHand.M_SkinHand")).Object;
    M.Ghost=ConstructorHelpers::FObjectFinder<UMaterialInterface>(TEXT("/Game/HapbeatDemoHands/Materials/M_GhostHand.M_GhostHand")).Object;
    M.Outline=ConstructorHelpers::FObjectFinder<UMaterialInterface>(TEXT("/Game/HapbeatDemoHands/Materials/M_GhostOutline.M_GhostOutline")).Object;
    M.Inside=ConstructorHelpers::FObjectFinder<UMaterialInterface>(TEXT("/Game/HapbeatDemoHands/Materials/M_HandInside.M_HandInside")).Object;
    return M;
}

UHapbeatDemoHand* UHapbeatDemoHand::Create(AActor* Owner,USkeletalMesh* Mesh,UTexture2D* InSkinTexture,const FHapbeatDemoHandMaterials& InMaterials,EHapbeatHandStyle InStyle)
{
    if(!Owner||!Mesh)return nullptr;
    UHapbeatDemoHand* Hand=NewObject<UHapbeatDemoHand>(Owner);
    Hand->Materials=InMaterials;Hand->SkinTexture=InSkinTexture;Hand->Style=InStyle;
    if(!Hand->Rig.Init(Mesh))UE_LOG(LogTemp,Error,TEXT("HAPBEAT_DEMO_HAND missing XRHand_* bones in %s"),*Mesh->GetName());
    for(int32 Layer=0;Layer<4;++Layer) {
        auto* M=NewObject<UPoseableMeshComponent>(Owner);
        M->SetMobility(EComponentMobility::Movable); // moved every frame to the tracked wrist
        M->SetSkinnedAsset(Mesh);M->SetCollisionEnabled(ECollisionEnabled::NoCollision);M->SetCastShadow(false);
        if(Layer==0) {
            // Custom Depth only: kept out of the main pass and of the scene depth pre-pass (a depth-only copy in the
            // pre-pass hides whatever lies behind the wrist opening and leaves a black hole there on desktop).
            for(int32 I=0;I<M->GetNumMaterials();++I)M->SetMaterial(I,InMaterials.Depth);
            M->SetRenderInMainPass(false);M->SetRenderInDepthPass(false);M->SetRenderCustomDepth(true);
        }
        M->SetTranslucentSortPriority(FMath::Min(Layer,2)); // outline (1) before the surface (2); the inner shell is opaque
        M->SetVisibility(false);M->RegisterComponent();
        Hand->Layers.Add(M);
    }
    Hand->ApplyStyle();
    return Hand;
}

void UHapbeatDemoHand::ApplyStyle()
{
    if(Layers.Num()<4)return;
    const bool bGhost=Style==EHapbeatHandStyle::Ghost;
    // The materials fade out along the reference forearm axis (pre-skinning component space).
    auto Make=[this](UMaterialInterface* Base) {
        UMaterialInstanceDynamic* Mid=Base?UMaterialInstanceDynamic::Create(Base,this):nullptr;
        if(Mid){Mid->SetVectorParameterValue(TEXT("WristRef"),FLinearColor(Rig.HandLoc));Mid->SetVectorParameterValue(TEXT("ArmDir"),FLinearColor(-Rig.RefForward.GetSafeNormal()));}
        return Mid;
    };
    SurfaceMaterial=Make(bGhost?Materials.Ghost:Materials.Skin);
    OutlineMaterial=Make(Materials.Outline);
    if(SurfaceMaterial&&!bGhost&&SkinTexture)SurfaceMaterial->SetTextureParameterValue(TEXT("SkinTex"),SkinTexture);
    InsideMaterial=Materials.Inside?UMaterialInstanceDynamic::Create(Materials.Inside,this):nullptr;
    if(InsideMaterial&&SkinTexture)InsideMaterial->SetTextureParameterValue(TEXT("SkinTex"),SkinTexture);
    for(int32 I=0;I<Layers[3]->GetNumMaterials();++I)Layers[3]->SetMaterial(I,InsideMaterial);
    if(OutlineMaterial&&!bGhost) {
        // Skin: the same shell as a thin, solid, darker skin-tone contour that fades with the skin at the wrist.
        OutlineMaterial->SetVectorParameterValue(TEXT("OutlineColor"),FLinearColor(.30f,.16f,.11f));
        OutlineMaterial->SetScalarParameterValue(TEXT("OutlineOpacity"),1.f);
        OutlineMaterial->SetScalarParameterValue(TEXT("OutlineWidth"),.07f);
        OutlineMaterial->SetScalarParameterValue(TEXT("FadeStart"),-1.8f);
        OutlineMaterial->SetScalarParameterValue(TEXT("FadeLength"),3.f);
    }
    for(int32 Layer=1;Layer<3;++Layer)
        for(int32 I=0;I<Layers[Layer]->GetNumMaterials();++I)Layers[Layer]->SetMaterial(I,Layer==1?OutlineMaterial:SurfaceMaterial);
}

void UHapbeatDemoHand::SetStyle(EHapbeatHandStyle InStyle)
{
    if(Style==InStyle)return;
    Style=InStyle;ApplyStyle();SetVisible(bShown);
}

void UHapbeatDemoHand::SetInsideVisible(bool bVisible)
{
    bInsideVisible=bVisible;SetVisible(bShown);
}

bool UHapbeatDemoHand::IsLayerShown(int32 Layer) const
{
    // The inner shell belongs to the Skin look only (the Ghost look is see-through by design).
    return Layer<3||(bInsideVisible&&Style==EHapbeatHandStyle::Skin&&InsideMaterial);
}

bool UHapbeatDemoHand::Update(const FXRHandTrackingState& S,const FVector& Offset)
{
    const bool bValid=S.bValid&&S.TrackingStatus==ETrackingStatus::Tracked;
    bool bPosed=bValid;
    for(UPoseableMeshComponent* M:Layers)bPosed=bPosed&&Rig.Pose(M,S,Offset);
    SetVisible(bPosed);
    return bPosed;
}

void UHapbeatDemoHand::SetVisible(bool bVisible)
{
    bShown=bVisible;
    for(int32 Layer=0;Layer<Layers.Num();++Layer)Layers[Layer]->SetVisibility(bVisible&&IsLayerShown(Layer));
}

bool UHapbeatDemoHand::IsVisible() const
{
    return !Layers.IsEmpty()&&Layers[0]->IsVisible();
}
