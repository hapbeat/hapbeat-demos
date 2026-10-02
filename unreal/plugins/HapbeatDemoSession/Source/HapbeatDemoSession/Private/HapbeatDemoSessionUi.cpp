#include "HapbeatDemoSessionUi.h"
#include "Components/StaticMeshComponent.h"
#include "Components/WidgetComponent.h"
#include "Engine/StaticMesh.h"
#include "Materials/MaterialInterface.h"
#include "Styling/CoreStyle.h"
#include "UObject/ConstructorHelpers.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SBox.h"
#include "Widgets/SCanvas.h"
#include "Widgets/Text/STextBlock.h"

namespace
{
    // Completion panel: 960 x 420 px at 0.045 cm/px (43 x 19 cm), 55 cm ahead of the eye and 12 cm below it.
    const FVector2D CompletionPixels(960,420);
    constexpr float CompletionCmPerPixel=.045f;
    constexpr float CompletionDistance=55.f, CompletionDrop=12.f;
    // Haptics button: 280 x 96 px at 0.032 cm/px (9 x 3 cm), 45 cm from the eye, 30 deg left and 35 deg down.
    const FVector2D HapticsPixels(280,96);
    constexpr float HapticsCmPerPixel=.032f;
    constexpr float HapticsDistance=45.f, HapticsYawOffset=-30.f, HapticsPitch=-35.f;
    // The button's yaw only follows the head once it has turned more than this away (a glance at the button keeps it in place).
    constexpr float HapticsYawDeadZone=25.f;
    constexpr float RayLength=150.f;
    // Poke depth window in cm in front of the face (FSafetyMillPoke): arm 2.5..12, press at <= 0.6, cancel beyond 18 or behind -3.
    constexpr float ArmNear=2.5f, ArmFar=12.f, PressDepth=.6f, CancelFar=18.f, CancelBehind=-3.f;
    // Widget colours are linear and pass through the scene's tonemapper / exposure, which lifts them a lot
    // (checked in a desktop capture of the jungle): keep them dark so white text stays readable.
    const FLinearColor PanelColor(.012f,.022f,.026f);
    const FLinearColor PrimaryColor(.02f,.19f,.15f), PrimaryHover(.05f,.34f,.27f);
    const FLinearColor SecondaryColor(.11f,.13f,.14f), SecondaryHover(.22f,.26f,.28f), DisabledColor(.05f,.06f,.065f);
    // Fingertip/ray tolerance around a button in pixels.
    constexpr float ButtonMargin=12.f;
    FSlateFontInfo Font(int32 Size,const char* Style="Regular") {return FCoreStyle::GetDefaultFontStyle(Style,Size);}
    void SetupWidget(UWidgetComponent* W,const FVector2D& Pixels,float CmPerPixel)
    {
        // Two-sided opaque: the widget material the demos' own world-space panels already use (and cook).
        W->SetWidgetSpace(EWidgetSpace::World);W->SetDrawSize(Pixels);W->SetTwoSided(true);
        W->SetCollisionEnabled(ECollisionEnabled::NoCollision);W->SetCastShadow(false);
        W->SetBlendMode(EWidgetBlendMode::Opaque);W->SetBackgroundColor(PanelColor);
        W->SetUsingAbsoluteLocation(true);W->SetUsingAbsoluteRotation(true);W->SetUsingAbsoluteScale(true);
        W->SetWorldScale3D(FVector(CmPerPixel));W->SetVisibility(false);
    }
    int32 HitButton(TConstArrayView<FBox2D> Buttons,const FVector2D& Pixel)
    {
        for(int32 I=0;I<Buttons.Num();++I) if(Buttons[I].ExpandBy(ButtonMargin).IsInside(Pixel)) return I;
        return INDEX_NONE;
    }
}

int32 FHapbeatSessionPressTracker::Update(const FTransform& Plane,float CmPerPixel,const FVector2D& Size,TConstArrayView<FBox2D> Buttons,
    const FHapbeatSessionPointerInput& In,bool bAccepting,int32* OutHover,float* InOutRayHit)
{
    // Widget pixels from panel-local cm: +Y is the viewer's left, +Z up (UWidgetComponent::GetLocalHitLocation).
    auto Pixel=[&](const FVector& L){return FVector2D(Size.X*.5f-L.Y/CmPerPixel,Size.Y*.5f-L.Z/CmPerPixel);};
    const FBox2D Panel(FVector2D::ZeroVector,Size);
    int32 Pressed=INDEX_NONE,Hover=INDEX_NONE;
    for(int32 H=0;H<2;++H) {
        if(In.bFinger[H]) {
            const FVector L=Plane.InverseTransformPositionNoScale(In.Finger[H]);
            const int32 B=HitButton(Buttons,Pixel(L));
            if(B==INDEX_NONE||L.X>CancelFar||L.X<CancelBehind||!bAccepting) Armed[H]=INDEX_NONE;
            else {
                Hover=B;
                if(L.X>ArmNear&&L.X<ArmFar) Armed[H]=B;
                else if(Armed[H]!=B) Armed[H]=INDEX_NONE; // slid onto another button below the arming depth
                if(Armed[H]==B&&L.X<=PressDepth) {Armed[H]=INDEX_NONE;Pressed=B;}
            }
        } else Armed[H]=INDEX_NONE;
        const bool Trigger=In.bRay[H]&&In.bTrigger[H];
        if(In.bRay[H]) {
            const FVector Origin=Plane.InverseTransformPositionNoScale(In.RayOrigin[H]);
            const FVector Dir=Plane.InverseTransformVectorNoScale(In.RayDirection[H].GetSafeNormal());
            // Only from the front, toward the face.
            if(Origin.X>0&&Dir.X<-KINDA_SMALL_NUMBER) {
                const float T=-Origin.X/Dir.X;
                const FVector2D P=Pixel(Origin+Dir*T);
                if(T<RayLength&&Panel.ExpandBy(ButtonMargin).IsInside(P)) {
                    if(InOutRayHit) InOutRayHit[H]=FMath::Min(InOutRayHit[H],T);
                    const int32 B=HitButton(Buttons,P);
                    if(B!=INDEX_NONE) {
                        Hover=B;
                        if(Trigger&&!bTriggerDown[H]&&bAccepting) Pressed=B;
                    }
                }
            }
        }
        bTriggerDown[H]=Trigger;
    }
    if(OutHover) *OutHover=Hover;
    return Pressed;
}

void FHapbeatSessionPressTracker::Reset()
{
    Armed[0]=Armed[1]=INDEX_NONE;
}

AHapbeatDemoSessionUi::AHapbeatDemoSessionUi()
{
    PrimaryActorTick.bCanEverTick=false; // stepped by UHapbeatDemoSessionSubsystem
    SetRootComponent(CreateDefaultSubobject<USceneComponent>(TEXT("Root")));
    Completion=CreateDefaultSubobject<UWidgetComponent>(TEXT("CompletionPanel"));Completion->SetupAttachment(RootComponent);
    SetupWidget(Completion,CompletionPixels,CompletionCmPerPixel);
    HapticsButton=CreateDefaultSubobject<UWidgetComponent>(TEXT("HapticsButton"));HapticsButton->SetupAttachment(RootComponent);
    SetupWidget(HapticsButton,HapticsPixels,HapticsCmPerPixel);
    UStaticMesh* Cylinder=ConstructorHelpers::FObjectFinder<UStaticMesh>(TEXT("/Engine/BasicShapes/Cylinder.Cylinder")).Object;
    UMaterialInterface* Material=ConstructorHelpers::FObjectFinder<UMaterialInterface>(TEXT("/Engine/BasicShapes/BasicShapeMaterial.BasicShapeMaterial")).Object;
    for(int32 H=0;H<2;++H) {
        Rays[H]=CreateDefaultSubobject<UStaticMeshComponent>(H==0?TEXT("LeftRay"):TEXT("RightRay"));
        Rays[H]->SetupAttachment(RootComponent);Rays[H]->SetStaticMesh(Cylinder);Rays[H]->SetMaterial(0,Material);
        Rays[H]->SetCollisionEnabled(ECollisionEnabled::NoCollision);Rays[H]->SetCastShadow(false);
        Rays[H]->SetUsingAbsoluteLocation(true);Rays[H]->SetUsingAbsoluteRotation(true);Rays[H]->SetUsingAbsoluteScale(true);
        Rays[H]->SetVisibility(false);
    }
}

FLinearColor AHapbeatDemoSessionUi::ButtonColor(int32 Button,bool bPrimary) const
{
    if(!IsCompletionAccepting()) return DisabledColor;
    const bool Hover=CompletionHover==Button;
    return bPrimary?(Hover?PrimaryHover:PrimaryColor):(Hover?SecondaryHover:SecondaryColor);
}

void AHapbeatDemoSessionUi::BuildCompletionWidget()
{
    // Fixed layout: buttons sit at known pixel rectangles (also the poke / ray targets), and the error line
    // has its own reserved height, so nothing moves when it appears.
    const bool Retry=CompletionView.bRetry;
    CompletionButtons.Reset();RetryButton=NextButton=INDEX_NONE;
    if(Retry) {RetryButton=CompletionButtons.Add(FBox2D(FVector2D(40,280),FVector2D(360,392)));}
    NextButton=CompletionButtons.Add(Retry?FBox2D(FVector2D(400,280),FVector2D(920,392)):FBox2D(FVector2D(220,280),FVector2D(740,392)));
    auto Button=[this](int32 Index,bool bPrimary,TFunction<FText()> Label) {
        const FBox2D& R=CompletionButtons[Index];
        return SNew(SBox).WidthOverride(R.GetSize().X).HeightOverride(R.GetSize().Y)
            [SNew(SBorder).BorderImage(FCoreStyle::Get().GetBrush("WhiteBrush")).Padding(FMargin(16,6)).HAlign(HAlign_Center).VAlign(VAlign_Center)
             .BorderBackgroundColor_Lambda([this,Index,bPrimary](){return FSlateColor(ButtonColor(Index,bPrimary));})
             [SNew(STextBlock).Text_Lambda(MoveTemp(Label)).Font(Font(28,"Bold")).Justification(ETextJustify::Center)
              .WrapTextAt(R.GetSize().X-40).ColorAndOpacity(FLinearColor::White)]];
    };
    TSharedRef<SCanvas> Canvas=SNew(SCanvas)
        +SCanvas::Slot().Position(FVector2D(0,22)).Size(FVector2D(CompletionPixels.X,74))
        [SNew(STextBlock).Text(FText::FromString(TEXT("体験完了"))).Font(Font(52,"Bold")).Justification(ETextJustify::Center).ColorAndOpacity(FLinearColor::White)]
        +SCanvas::Slot().Position(FVector2D(0,104)).Size(FVector2D(CompletionPixels.X,44))
        [SNew(STextBlock).Text_Lambda([this](){return FText::FromString(FString::Printf(TEXT("%d / %d"),CompletionView.StepNumber,CompletionView.StepCount));})
         .Font(Font(30)).Justification(ETextJustify::Center).ColorAndOpacity(FLinearColor(.55f,.70f,.68f))]
        +SCanvas::Slot().Position(FVector2D(40,166)).Size(FVector2D(CompletionPixels.X-80,96))
        [SNew(STextBlock).Text_Lambda([this](){return FText::FromString(CompletionError);}).Font(Font(26)).Justification(ETextJustify::Center)
         .AutoWrapText(true).ColorAndOpacity(FLinearColor(1.f,.45f,.38f))];
    if(RetryButton!=INDEX_NONE) {
        const FBox2D R=CompletionButtons[RetryButton];
        Canvas->AddSlot().Position(R.Min).Size(R.GetSize())[Button(RetryButton,false,[](){return FText::FromString(TEXT("もう一度"));})];
    }
    const FBox2D R=CompletionButtons[NextButton];
    Canvas->AddSlot().Position(R.Min).Size(R.GetSize())[Button(NextButton,true,[this](){return FText::FromString(CompletionView.NextLabel);})];
    Completion->SetSlateWidget(SNew(SBorder).BorderImage(FCoreStyle::Get().GetBrush("WhiteBrush")).BorderBackgroundColor(PanelColor).Padding(0)[Canvas]);
}

void AHapbeatDemoSessionUi::BuildHapticsWidget()
{
    bHapticsBuilt=true;
    // One fixed-width face for both texts ("触覚 ON" / "触覚 OFF"): switching never changes its size.
    HapticsButton->SetSlateWidget(SNew(SBorder).BorderImage(FCoreStyle::Get().GetBrush("WhiteBrush")).Padding(0).HAlign(HAlign_Center).VAlign(VAlign_Center)
        .BorderBackgroundColor_Lambda([this](){
            return FSlateColor(bHapticsOn?(bHapticsHover?PrimaryHover:PrimaryColor):(bHapticsHover?SecondaryHover:SecondaryColor));})
        [SNew(SBox).WidthOverride(HapticsPixels.X).HeightOverride(HapticsPixels.Y).HAlign(HAlign_Center).VAlign(VAlign_Center)
         [SNew(STextBlock).Text_Lambda([this](){return FText::FromString(bHapticsOn?TEXT("触覚 ON"):TEXT("触覚 OFF"));})
          .Font(Font(36,"Bold")).Justification(ETextJustify::Center).ColorAndOpacity(FLinearColor::White)]]);
}

void AHapbeatDemoSessionUi::ShowCompletion(const FHapbeatSessionCompletionView& View,const FVector& Eye,const FRotator& ViewRotation)
{
    CompletionView=View;CompletionError.Empty();CompletionSeconds=0;CompletionHover=INDEX_NONE;
    BuildCompletionWidget();CompletionPress.Reset();
    // In front of the user (head yaw only), a little below eye level, facing the eye.
    const FVector At=Eye+FRotator(0,ViewRotation.Yaw,0).Vector()*CompletionDistance-FVector(0,0,CompletionDrop);
    Completion->SetWorldLocationAndRotation(At,(Eye-At).Rotation());
    Completion->SetVisibility(true);bCompletionShown=true;
}

void AHapbeatDemoSessionUi::HideCompletion()
{
    Completion->SetVisibility(false);bCompletionShown=false;CompletionPress.Reset();
}

void AHapbeatDemoSessionUi::SetHapticsButton(bool bVisible,bool bOn)
{
    bHapticsOn=bOn;
    if(bVisible&&!bHapticsBuilt) BuildHapticsWidget();
    if(bVisible!=HapticsButton->IsVisible()) {HapticsButton->SetVisibility(bVisible);HapticsPress.Reset();bHapticsPlaced=false;}
}

void AHapbeatDemoSessionUi::PlaceHapticsButton(const FVector& Eye,const FRotator& View,float Dt)
{
    // Lower left of the view, following the head slowly: the yaw catches up only beyond the dead zone and
    // the pitch is fixed below the horizon, so looking down at the button does not push it away.
    if(!bHapticsPlaced) HapticsYaw=View.Yaw;
    const float Off=FRotator::NormalizeAxis(View.Yaw-HapticsYaw);
    if(FMath::Abs(Off)>HapticsYawDeadZone) HapticsYaw+=(Off-FMath::Sign(Off)*HapticsYawDeadZone)*FMath::Min(1.f,Dt*3.f);
    const FVector Goal=Eye+FRotator(HapticsPitch,HapticsYaw+HapticsYawOffset,0).Vector()*HapticsDistance;
    HapticsLocation=bHapticsPlaced?FMath::VInterpTo(HapticsLocation,Goal,Dt,4.f):Goal;bHapticsPlaced=true;
    HapticsButton->SetWorldLocationAndRotation(HapticsLocation,(Eye-HapticsLocation).Rotation());
}

AHapbeatDemoSessionUi::FEvents AHapbeatDemoSessionUi::Step(const FHapbeatSessionPointerInput& In,const FVector& Eye,const FRotator& View,float Dt)
{
    FEvents E;
    float RayHit[2]={RayLength,RayLength};
    if(HapticsButton->IsVisible()) {
        PlaceHapticsButton(Eye,View,Dt);
        const FBox2D Face(FVector2D::ZeroVector,HapticsPixels);
        int32 Hover=INDEX_NONE;
        const FTransform Plane(HapticsButton->GetComponentQuat(),HapticsButton->GetComponentLocation());
        E.bToggleHaptics=HapticsPress.Update(Plane,HapticsCmPerPixel,HapticsPixels,MakeArrayView(&Face,1),In,true,&Hover,RayHit)==0;
        bHapticsHover=Hover==0;
    }
    if(bCompletionShown) {
        CompletionSeconds+=Dt;
        const FTransform Plane(Completion->GetComponentQuat(),Completion->GetComponentLocation());
        const int32 Pressed=CompletionPress.Update(Plane,CompletionCmPerPixel,CompletionPixels,CompletionButtons,In,IsCompletionAccepting(),&CompletionHover,RayHit);
        E.bRetry=Pressed!=INDEX_NONE&&Pressed==RetryButton;
        E.bNext=Pressed!=INDEX_NONE&&Pressed==NextButton;
    }
    const bool UiVisible=bCompletionShown||HapticsButton->IsVisible();
    for(int32 H=0;H<2;++H) {
        const bool Show=UiVisible&&In.bRay[H];
        Rays[H]->SetVisibility(Show);
        if(!Show) continue;
        const FVector Dir=In.RayDirection[H].GetSafeNormal();
        Rays[H]->SetWorldLocationAndRotation(In.RayOrigin[H]+Dir*RayHit[H]*.5f,FRotationMatrix::MakeFromZ(Dir).Rotator());
        Rays[H]->SetWorldScale3D(FVector(.004f,.004f,RayHit[H]/100.f));
    }
    return E;
}
