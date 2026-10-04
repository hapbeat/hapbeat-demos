#include "HapbeatDemoSessionUi.h"
#include "HapbeatDemoSessionLog.h"
#include "HapbeatDemoSessionPanelAnchor.h"
#include "Components/StaticMeshComponent.h"
#include "Components/WidgetComponent.h"
#include "Engine/StaticMesh.h"
#include "Engine/World.h"
#include "Fonts/CompositeFont.h"
#include "HAL/FileManager.h"
#include "Interfaces/IPluginManager.h"
#include "Kismet/GameplayStatics.h"
#include "Materials/MaterialInterface.h"
#include "Misc/Paths.h"
#include "Sound/SoundBase.h"
#include "Styling/CoreStyle.h"
#include "UObject/ConstructorHelpers.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SBox.h"
#include "Widgets/Layout/SScaleBox.h"
#include "Widgets/SCanvas.h"
#include "Widgets/Text/STextBlock.h"

namespace
{
    // The Unity panels are laid out in millimetres (DemoSessionUi.cs, DemoPause.cs); here 2 widget px per mm at
    // 0.05 cm/px, so the same layout numbers give the same physical size.
    constexpr float Px=2.f;
    constexpr float PanelWidth=440, ButtonHeight=68, ButtonGap=14;
    // Completion and pause panels: by default 55 cm ahead of the eye and 12 cm below it.
    constexpr float PanelDistance=55.f, PanelDrop=12.f;
    // In-view buttons (Unity DemoSessionCornerButtons): two 88 x 64 mm plates (an 80 x 56 mm button, two-line English
    // label), 45 cm from the eye and 30 deg below the horizon, Reset / View 19.5 deg and Haptics / ON|OFF 5 deg left of
    // the heading (about 10 mm apart). Each keeps its place when the other is hidden.
    const FVector2D InViewPlate(88,64);
    constexpr float PlateMargin=4;
    constexpr float InViewDistance=45.f, InViewPitch=-30.f, RecenterYawOffset=-19.5f, HapticsYawOffset=-5.f;
    // How quickly an in-view button catches up with the head's heading, 1/s (Unity DemoHeadingPlacement.FollowRate).
    constexpr float InViewFollowRate=2.5f;
    constexpr float RayLength=150.f;
    // Poke depth window in cm in front of the face (FSafetyMillPoke): arm 2.5..12, press at <= 0.6, cancel beyond 18 or behind -3.
    constexpr float ArmNear=2.5f, ArmFar=12.f, PressDepth=.6f, CancelFar=18.f, CancelBehind=-3.f;
    // Unity font sizes are the glyph em in mm; Slate sizes are points at 96 DPI.
    constexpr float TitleMm=34, SubLineMm=22, ButtonMm=24, ErrorMm=17, InViewMm=18;
    // Unity DemoSessionButton.Press: the pressed colour shows for 0.15 s; DemoSessionClickSound.Volume.
    constexpr float FlashSeconds=.15f, ClickVolume=.5f;
    // Fingertip/ray tolerance around a button in pixels.
    constexpr float ButtonMargin=12.f;
    // Unity UI colours are sRGB; Slate colours are linear.
    FLinearColor Srgb(float R,float G,float B) {return FLinearColor::FromSRGBColor(FLinearColor(R,G,B).ToFColor(false));}
    const FLinearColor PanelColor=Srgb(.025f,.04f,.065f);
    const FLinearColor NormalColor=Srgb(.12f,.2f,.27f), HoverColor=Srgb(.16f,.42f,.52f), SelectedColor=Srgb(.08f,.52f,.58f), FlashColor=Srgb(.55f,.9f,1.f);
    const FLinearColor SubLineColor=Srgb(.7f,.82f,.92f), ErrorColor=Srgb(1.f,.55f,.45f);
    const TCHAR* const PanelMaterialPath=TEXT("/HapbeatDemoSession/Materials/M_HapbeatDemoSessionPanel.M_HapbeatDemoSessionPanel");
    const TCHAR* const RayMaterialPath=TEXT("/HapbeatDemoSession/Materials/M_HapbeatDemoSessionRay.M_HapbeatDemoSessionRay");
    const TCHAR* const ClickPath=TEXT("/HapbeatDemoSession/Audio/S_HapbeatDemoSessionClick.S_HapbeatDemoSessionClick");

    /** Noto Sans CJK JP Regular (Resources/Fonts, staged with the plugin), the Unity panels' font. */
    FSlateFontInfo Font(float Mm)
    {
        const float Size=Mm*Px*72.f/96.f;
        static const TSharedPtr<const FCompositeFont> Noto=[]()->TSharedPtr<const FCompositeFont>
        {
            const TSharedPtr<IPlugin> Plugin=IPluginManager::Get().FindPlugin(TEXT("HapbeatDemoSession"));
            const FString Path=Plugin.IsValid()?FPaths::Combine(Plugin->GetBaseDir(),TEXT("Resources/Fonts/NotoSansCJKjp-Regular.otf")):FString();
            if(Path.IsEmpty()||!IFileManager::Get().FileExists(*Path)) {
                UE_LOG(LogHapbeatDemoSession,Warning,TEXT("DEMO_SESSION_FONT_MISSING %s: panels use the engine font"),*Path);
                return nullptr;
            }
            return MakeShared<const FCompositeFont>(TEXT("NotoSansCJKjp-Regular"),Path,EFontHinting::Default,EFontLoadingPolicy::LazyLoad);
        }();
        return Noto.IsValid()?FSlateFontInfo(Noto,Size):FCoreStyle::GetDefaultFontStyle("Regular",Size);
    }
    FBox2D Mm(float X0,float Y0,float X1,float Y1) {return FBox2D(FVector2D(X0,Y0)*Px,FVector2D(X1,Y1)*Px);}
    /**
     * The Unity stack: heading 46 mm at 28 mm from the top, an optional sub line, N buttons (380 x 68 mm, 14 mm
     * apart) from FirstTop, then the reserved error line ErrorHeight high, 14 mm under the last button.
     */
    FHapbeatSessionPanelLayout Stack(int32 Count,float Height,float SubLineHeight,float FirstTop,float ErrorHeight)
    {
        FHapbeatSessionPanelLayout L;
        L.Size=FVector2D(PanelWidth,Height)*Px;
        L.Title=Mm(20,28,PanelWidth-20,74);
        L.SubLine=Mm(20,74,PanelWidth-20,74+SubLineHeight);
        float Top=FirstTop;
        for(int32 I=0;I<Count;++I,Top+=ButtonHeight+ButtonGap) L.Buttons.Add(Mm(30,Top,PanelWidth-30,Top+ButtonHeight));
        // Top is now 14 mm under the last button.
        L.Error=Mm(20,Top,PanelWidth-20,Top+ErrorHeight);
        return L;
    }
    void SetupWidget(UWidgetComponent* W)
    {
        // Drawn with the plugin's widget material (PostInitializeComponents): translucent without a depth test.
        W->SetWidgetSpace(EWidgetSpace::World);W->SetTwoSided(true);
        W->SetCollisionEnabled(ECollisionEnabled::NoCollision);W->SetCastShadow(false);
        W->SetBlendMode(EWidgetBlendMode::Transparent);W->SetBackgroundColor(PanelColor);
        W->SetTranslucentSortPriority(AHapbeatDemoSessionUi::PanelSortPriority);
        W->SetUsingAbsoluteLocation(true);W->SetUsingAbsoluteRotation(true);W->SetUsingAbsoluteScale(true);
        W->SetWorldScale3D(FVector(AHapbeatDemoSessionUi::PanelCmPerPixel));W->SetVisibility(false);
        // Keeps redrawing while a demo pauses its world under the pause panel.
        W->SetTickableWhenPaused(true);
    }
    int32 HitButton(TConstArrayView<FBox2D> Buttons,const FVector2D& Pixel)
    {
        for(int32 I=0;I<Buttons.Num();++I) if(Buttons[I].ExpandBy(ButtonMargin).IsInside(Pixel)) return I;
        return INDEX_NONE;
    }
    TSharedRef<SWidget> CentredText(TFunction<FText()> Text,float Mm,const FLinearColor& Color)
    {
        return SNew(SBox).VAlign(VAlign_Center)
            [SNew(STextBlock).Text_Lambda(MoveTemp(Text)).Font(Font(Mm)).Justification(ETextJustify::Center).AutoWrapText(true).ColorAndOpacity(Color)];
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
    CompletionPanel=CreateDefaultSubobject<UWidgetComponent>(TEXT("CompletionPanel"));CompletionPanel->SetupAttachment(RootComponent);
    PausePanel=CreateDefaultSubobject<UWidgetComponent>(TEXT("PausePanel"));PausePanel->SetupAttachment(RootComponent);
    HapticsButton=CreateDefaultSubobject<UWidgetComponent>(TEXT("HapticsButton"));HapticsButton->SetupAttachment(RootComponent);
    RecenterButton=CreateDefaultSubobject<UWidgetComponent>(TEXT("RecenterButton"));RecenterButton->SetupAttachment(RootComponent);
    for(UWidgetComponent* W:{CompletionPanel.Get(),PausePanel.Get(),HapticsButton.Get(),RecenterButton.Get()}) SetupWidget(W);
    PanelMaterial=ConstructorHelpers::FObjectFinder<UMaterialInterface>(PanelMaterialPath).Object;
    ClickSound=ConstructorHelpers::FObjectFinder<USoundBase>(ClickPath).Object;
    UStaticMesh* Cylinder=ConstructorHelpers::FObjectFinder<UStaticMesh>(TEXT("/Engine/BasicShapes/Cylinder.Cylinder")).Object;
    // Translucent, after the panels (HandSortPriority): a ray stays visible in front of the panel it points at.
    UMaterialInterface* RayMaterial=ConstructorHelpers::FObjectFinder<UMaterialInterface>(RayMaterialPath).Object;
    for(int32 H=0;H<2;++H) {
        Rays[H]=CreateDefaultSubobject<UStaticMeshComponent>(H==0?TEXT("LeftRay"):TEXT("RightRay"));
        Rays[H]->SetupAttachment(RootComponent);Rays[H]->SetStaticMesh(Cylinder);Rays[H]->SetMaterial(0,RayMaterial);
        Rays[H]->SetTranslucentSortPriority(HandSortPriority);
        Rays[H]->SetCollisionEnabled(ECollisionEnabled::NoCollision);Rays[H]->SetCastShadow(false);
        Rays[H]->SetUsingAbsoluteLocation(true);Rays[H]->SetUsingAbsoluteRotation(true);Rays[H]->SetUsingAbsoluteScale(true);
        Rays[H]->SetVisibility(false);
    }
}

void AHapbeatDemoSessionUi::PostInitializeComponents()
{
    Super::PostInitializeComponents();
    // Set here, not in the constructor: UWidgetComponent::SetMaterial creates the widget's material instance.
    if(!PanelMaterial) {UE_LOG(LogHapbeatDemoSession,Warning,TEXT("DEMO_SESSION_PANEL_MATERIAL_MISSING %s: panels can be hidden by the scene"),PanelMaterialPath);return;}
    for(UWidgetComponent* W:{CompletionPanel.Get(),PausePanel.Get(),HapticsButton.Get(),RecenterButton.Get()}) W->SetMaterial(0,PanelMaterial);
}

FHapbeatSessionPanelLayout AHapbeatDemoSessionUi::CompletionLayout(int32 Count)
{
    // DemoSessionCompletionPanel: heading, progress (34 mm), 18 mm, buttons, error line 52 mm, 16 mm.
    return Stack(Count,28+46+34+18+Count*(ButtonHeight+ButtonGap)+52+16,34,28+46+34+18,52);
}

FHapbeatSessionPanelLayout AHapbeatDemoSessionUi::PauseLayout(int32 Count)
{
    // DemoPausePanel: heading, 18 mm, buttons, error line 40 mm, 16 mm.
    return Stack(Count,28+46+18+Count*(ButtonHeight+ButtonGap)+40+16,0,28+46+18,40);
}

FTransform AHapbeatDemoSessionUi::PlacePanel(const FVector& Eye,const FRotator& View,const UHapbeatDemoSessionPanelAnchor* Anchor)
{
    if(!Anchor) {
        // In front of the user (head yaw only), a little below eye level, facing the eye.
        const FVector At=Eye+FRotator(0,View.Yaw,0).Vector()*PanelDistance-FVector(0,0,PanelDrop);
        return FTransform((Eye-At).Rotation(),At);
    }
    const FVector At=Anchor->GetComponentLocation();
    if(!Anchor->bUseRotation) return FTransform(FRotator(0,(Eye-At).Rotation().Yaw,0),At);
    // The anchor's own facing, but never its back to the user: turned round when the eye is behind it.
    FQuat Q=Anchor->GetComponentQuat();
    if(FVector::DotProduct(Q.GetAxisX(),Eye-At)<0) Q=FQuat(FVector::UpVector,PI)*Q;
    return FTransform(Q,At);
}

FLinearColor AHapbeatDemoSessionUi::ButtonColor(const FPanel& Panel,int32 Button,bool bHighlighted) const
{
    // DemoSessionButton.Refresh: pressed flash, hover, selected (an ON toggle), normal.
    if(Panel.Flash==Button&&Panel.FlashSeconds>0) return FlashColor;
    if(Panel.Hover==Button) return HoverColor;
    return bHighlighted?SelectedColor:NormalColor;
}

TSharedRef<SCanvas> AHapbeatDemoSessionUi::MakePanelCanvas(const FPanel& Panel,const FHapbeatSessionPanelLayout& L,const FString& Title,TFunction<FText()> SubLine)
{
    // Fixed layout: buttons sit at known pixel rectangles (also the poke / ray targets), and the error line
    // has its own reserved height, so nothing moves when it appears.
    TSharedRef<SCanvas> Canvas=SNew(SCanvas)
        +SCanvas::Slot().Position(FVector2D::ZeroVector).Size(L.Size)
        [SNew(SBorder).BorderImage(FCoreStyle::Get().GetBrush("WhiteBrush")).BorderBackgroundColor(PanelColor)]
        +SCanvas::Slot().Position(L.Title.Min).Size(L.Title.GetSize())
        [CentredText([Title](){return FText::FromString(Title);},TitleMm,FLinearColor::White)]
        +SCanvas::Slot().Position(L.Error.Min).Size(L.Error.GetSize())
        [CentredText([&Panel](){return FText::FromString(Panel.Error);},ErrorMm,ErrorColor)];
    if(L.SubLine.GetSize().Y>0) Canvas->AddSlot().Position(L.SubLine.Min).Size(L.SubLine.GetSize())[CentredText(MoveTemp(SubLine),SubLineMm,SubLineColor)];
    return Canvas;
}

void AHapbeatDemoSessionUi::AddPanelButton(SCanvas& Canvas,const FPanel& Panel,int32 Index,TFunction<FText()> Label,TFunction<bool()> Highlighted)
{
    // DemoSessionPanel.AddButton: the label wraps inside the button (10 x 4 mm inset) and shrinks when it still does not fit.
    const FBox2D R=Panel.Buttons[Index];
    Canvas.AddSlot().Position(R.Min).Size(R.GetSize())
        [SNew(SBorder).BorderImage(FCoreStyle::Get().GetBrush("WhiteBrush")).Padding(FMargin(5*Px,2*Px)).HAlign(HAlign_Center).VAlign(VAlign_Center)
         .BorderBackgroundColor_Lambda([this,&Panel,Index,Highlighted=MoveTemp(Highlighted)](){return FSlateColor(ButtonColor(Panel,Index,Highlighted&&Highlighted()));})
         [SNew(SScaleBox).Stretch(EStretch::ScaleToFit).StretchDirection(EStretchDirection::DownOnly)
          [SNew(STextBlock).Text_Lambda(MoveTemp(Label)).Font(Font(ButtonMm)).Justification(ETextJustify::Center)
           .WrapTextAt(R.GetSize().X-10*Px).ColorAndOpacity(FLinearColor::White)]]];
}

void AHapbeatDemoSessionUi::BuildCompletionWidget()
{
    const FHapbeatSessionPanelLayout L=CompletionLayout(CompletionView.bRetry?2:1);
    Completion.Size=L.Size;Completion.Buttons=L.Buttons;
    RetryButton=CompletionView.bRetry?0:INDEX_NONE;
    NextButton=CompletionView.bRetry?1:0;
    TSharedRef<SCanvas> Canvas=MakePanelCanvas(Completion,L,TEXT("体験完了"),
        [this](){return FText::FromString(FString::Printf(TEXT("%d / %d"),CompletionView.StepNumber,CompletionView.StepCount));});
    if(RetryButton!=INDEX_NONE) AddPanelButton(*Canvas,Completion,RetryButton,[](){return FText::FromString(TEXT("もう一度"));});
    AddPanelButton(*Canvas,Completion,NextButton,[this](){return FText::FromString(CompletionView.NextLabel);});
    CompletionPanel->SetDrawSize(L.Size);
    CompletionPanel->SetSlateWidget(Canvas);
}

void AHapbeatDemoSessionUi::BuildPauseWidget(bool bHub,const FString& NextLabel)
{
    // 再開 / 最初からやり直す / 次へ：<title> or デモを終了 (session only) / Hub に戻る (only when the Hub is
    // installed), stacked like Unity's DemoPausePanel.
    PauseNextLabel=NextLabel;
    const bool bNext=!NextLabel.IsEmpty();
    int32 Count=0;
    ResumeButton=Count++;RestartButton=Count++;
    PauseNextButton=bNext?Count++:INDEX_NONE;
    HubButton=bHub?Count++:INDEX_NONE;
    const FHapbeatSessionPanelLayout L=PauseLayout(Count);
    Pause.Size=L.Size;Pause.Buttons=L.Buttons;
    TSharedRef<SCanvas> Canvas=MakePanelCanvas(Pause,L,TEXT("一時停止"),[](){return FText::GetEmpty();});
    AddPanelButton(*Canvas,Pause,ResumeButton,[](){return FText::FromString(TEXT("再開"));});
    AddPanelButton(*Canvas,Pause,RestartButton,[](){return FText::FromString(TEXT("最初からやり直す"));});
    if(PauseNextButton!=INDEX_NONE) AddPanelButton(*Canvas,Pause,PauseNextButton,[this](){return FText::FromString(PauseNextLabel);});
    if(HubButton!=INDEX_NONE) AddPanelButton(*Canvas,Pause,HubButton,[](){return FText::FromString(TEXT("Hub に戻る"));});
    PausePanel->SetDrawSize(L.Size);
    PausePanel->SetSlateWidget(Canvas);
}

void AHapbeatDemoSessionUi::BuildInViewWidget(UWidgetComponent* Widget,FInViewButton& Button,TFunction<FText()> Label,TFunction<bool()> Highlighted)
{
    // A plate one button wide; its width fits the longest label, so the text never resizes it.
    Button.Face.Size=InViewPlate*Px;
    Button.Face.Buttons={Mm(PlateMargin,PlateMargin,InViewPlate.X-PlateMargin,InViewPlate.Y-PlateMargin)};
    const FBox2D R=Button.Face.Buttons[0];
    TSharedRef<SCanvas> Canvas=SNew(SCanvas)
        +SCanvas::Slot().Position(FVector2D::ZeroVector).Size(Button.Face.Size)
        [SNew(SBorder).BorderImage(FCoreStyle::Get().GetBrush("WhiteBrush")).BorderBackgroundColor(PanelColor)];
    Canvas->AddSlot().Position(R.Min).Size(R.GetSize())
        [SNew(SBorder).BorderImage(FCoreStyle::Get().GetBrush("WhiteBrush")).HAlign(HAlign_Center).VAlign(VAlign_Center)
         .BorderBackgroundColor_Lambda([this,&Button,Highlighted=MoveTemp(Highlighted)](){return FSlateColor(ButtonColor(Button.Face,0,Highlighted()));})
         [SNew(STextBlock).Text_Lambda(MoveTemp(Label)).Font(Font(InViewMm)).Justification(ETextJustify::Center).ColorAndOpacity(FLinearColor::White)]];
    Widget->SetDrawSize(Button.Face.Size);
    Widget->SetSlateWidget(Canvas);
}

void AHapbeatDemoSessionUi::ShowPanel(UWidgetComponent* Widget,FPanel& Panel,const FTransform& At)
{
    Panel.Error.Empty();Panel.Seconds=0;Panel.Hover=Panel.Flash=INDEX_NONE;Panel.Press.Reset();
    // Placed once: the panel stays where it appeared.
    Widget->SetWorldLocationAndRotation(At.GetLocation(),At.GetRotation());
    Widget->SetVisibility(true);Panel.bShown=true;
}

void AHapbeatDemoSessionUi::HidePanel(UWidgetComponent* Widget,FPanel& Panel)
{
    Widget->SetVisibility(false);Panel.bShown=false;Panel.Press.Reset();
}

void AHapbeatDemoSessionUi::ShowCompletion(const FHapbeatSessionCompletionView& View,const FTransform& At)
{
    CompletionView=View;
    BuildCompletionWidget();
    ShowPanel(CompletionPanel,Completion,At);
}

void AHapbeatDemoSessionUi::HideCompletion()
{
    HidePanel(CompletionPanel,Completion);
}

void AHapbeatDemoSessionUi::ShowPause(bool bHub,const FString& NextLabel,const FTransform& At)
{
    BuildPauseWidget(bHub,NextLabel);
    ShowPanel(PausePanel,Pause,At);
}

void AHapbeatDemoSessionUi::HidePause()
{
    HidePanel(PausePanel,Pause);
}

void AHapbeatDemoSessionUi::Reposition(const FTransform& At)
{
    // Both panels go to the same place: they are never open at the same time.
    auto Move=[&At](UWidgetComponent* Widget,FPanel& Panel)
    {
        if(!Panel.bShown) return;
        Widget->SetWorldLocationAndRotation(At.GetLocation(),At.GetRotation());
        Panel.Press.Reset();
    };
    Move(CompletionPanel,Completion);Move(PausePanel,Pause);
    Haptics.bPlaced=Recenter.bPlaced=false;
}

void AHapbeatDemoSessionUi::SetInViewVisible(UWidgetComponent* Widget,FInViewButton& Button,bool bVisible)
{
    if(bVisible==Widget->IsVisible()) return;
    Widget->SetVisibility(bVisible);Button.Face.bShown=bVisible;Button.Face.Press.Reset();Button.Face.Hover=INDEX_NONE;Button.bPlaced=false;
}

void AHapbeatDemoSessionUi::SetHapticsButton(bool bVisible,bool bOn)
{
    bHapticsOn=bOn;
    if(bVisible&&Haptics.Face.Buttons.IsEmpty())
        BuildInViewWidget(HapticsButton,Haptics,[this](){return FText::FromString(bHapticsOn?TEXT("Haptics\nON"):TEXT("Haptics\nOFF"));},[this](){return bHapticsOn;});
    SetInViewVisible(HapticsButton,Haptics,bVisible);
}

void AHapbeatDemoSessionUi::SetRecenterButton(bool bVisible)
{
    if(bVisible&&Recenter.Face.Buttons.IsEmpty())
        BuildInViewWidget(RecenterButton,Recenter,[](){return FText::FromString(TEXT("Reset\nView"));},[](){return false;});
    SetInViewVisible(RecenterButton,Recenter,bVisible);
}

FVector AHapbeatDemoSessionUi::InViewButtonLocation(const FVector& Eye,float HeadingYaw,bool bRecenter)
{
    // Turned from the heading (negative = left), then lowered below the horizon.
    return Eye+FRotator(InViewPitch,HeadingYaw+(bRecenter?RecenterYawOffset:HapticsYawOffset),0).Vector()*InViewDistance;
}

void AHapbeatDemoSessionUi::PlaceInViewButtons(const FVector& Eye,const FRotator& View,float Dt)
{
    // Lower left of the view, slowly following the head's heading (Unity DemoHeadingPlacement): the head's pitch is
    // ignored, so looking down at a button keeps it still.
    const float Alpha=1.f-FMath::Exp(-InViewFollowRate*Dt);
    auto Place=[&](UWidgetComponent* Widget,FInViewButton& Button,bool bRecenter)
    {
        if(!Button.Face.bShown) return;
        const FVector Goal=InViewButtonLocation(Eye,View.Yaw,bRecenter);
        const FQuat GoalRotation=(Eye-Goal).Rotation().Quaternion();
        if(Button.bPlaced) {Button.Location=FMath::Lerp(Button.Location,Goal,Alpha);Button.Rotation=FQuat::Slerp(Button.Rotation,GoalRotation,Alpha);}
        else {Button.Location=Goal;Button.Rotation=GoalRotation;}
        Button.bPlaced=true;
        Widget->SetWorldLocationAndRotation(Button.Location,Button.Rotation);
    };
    Place(HapticsButton,Haptics,false);Place(RecenterButton,Recenter,true);
}

void AHapbeatDemoSessionUi::PlayClick()
{
    // Never in editor preview worlds (automation tests). A UI sound: it plays while the game is paused.
    const UWorld* World=GetWorld();
    if(!ClickSound||!World||!World->IsGameWorld()) return;
    UGameplayStatics::PlaySound2D(this,ClickSound,ClickVolume,1.f,0.f,nullptr,nullptr,true);
}

int32 AHapbeatDemoSessionUi::StepPanel(UWidgetComponent* Widget,FPanel& Panel,const FHapbeatSessionPointerInput& In,float InputDelay,float Dt,float* RayHit)
{
    if(!Panel.bShown) return INDEX_NONE;
    Panel.Seconds+=Dt;Panel.FlashSeconds-=Dt;
    const bool bAccepting=Panel.Seconds>=InputDelay;
    const FTransform Plane(Widget->GetComponentQuat(),Widget->GetComponentLocation());
    const int32 Pressed=Panel.Press.Update(Plane,PanelCmPerPixel,Panel.Size,Panel.Buttons,In,bAccepting,&Panel.Hover,RayHit);
    if(Pressed!=INDEX_NONE) {Panel.Flash=Pressed;Panel.FlashSeconds=FlashSeconds;PlayClick();}
    return Pressed;
}

AHapbeatDemoSessionUi::FEvents AHapbeatDemoSessionUi::Step(const FHapbeatSessionPointerInput& In,const FVector& Eye,const FRotator& View,float Dt)
{
    FEvents E;
    float RayHit[2]={RayLength,RayLength};
    PlaceInViewButtons(Eye,View,Dt);
    // The in-view buttons take input at once (as in Unity); the panels after CompletionInputDelay.
    E.bToggleHaptics=StepPanel(HapticsButton,Haptics.Face,In,0.f,Dt,RayHit)==0;
    E.bRecenter=StepPanel(RecenterButton,Recenter.Face,In,0.f,Dt,RayHit)==0;
    const int32 Done=StepPanel(CompletionPanel,Completion,In,CompletionInputDelay,Dt,RayHit);
    E.bRetry=Done!=INDEX_NONE&&Done==RetryButton;
    E.bNext=Done!=INDEX_NONE&&Done==NextButton;
    const int32 Chosen=StepPanel(PausePanel,Pause,In,CompletionInputDelay,Dt,RayHit);
    E.bResume=Chosen!=INDEX_NONE&&Chosen==ResumeButton;
    E.bRestart=Chosen!=INDEX_NONE&&Chosen==RestartButton;
    E.bPauseNext=Chosen!=INDEX_NONE&&Chosen==PauseNextButton;
    E.bHub=Chosen!=INDEX_NONE&&Chosen==HubButton;
    const bool UiVisible=Completion.bShown||Pause.bShown||Haptics.Face.bShown||Recenter.Face.bShown;
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
