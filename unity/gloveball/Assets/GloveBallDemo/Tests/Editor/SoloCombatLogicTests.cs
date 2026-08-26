using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GloveBallDemo.Core;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace GloveBallDemo.Tests
{
    public sealed class SoloCombatLogicTests : InputTestFixture
    {
        public override void Setup()
        {
            base.Setup();
            InputSystem.RegisterLayout<XRSimulatedController>();
        }

        [Test]
        public void ThrowCharge_MapsZeroHalfAndFullOverOnePointFiveSeconds()
        {
            var charge = new ThrowCharge();
            Assert.That(ThrowCharge.FullChargeSeconds, Is.EqualTo(1.5f));
            Assert.That(charge.Speed, Is.EqualTo(5f));
            charge.Tick(ThrowCharge.FullChargeSeconds * 0.5f);
            Assert.That(charge.Speed, Is.EqualTo(12.5f).Within(0.001f));
            charge.Tick(ThrowCharge.FullChargeSeconds);
            Assert.That(charge.Value, Is.EqualTo(1f));
            Assert.That(charge.Speed, Is.EqualTo(20f));
        }

        [Test]
        public void TargetLayout_HasFourSpacedFarTargets()
        {
            var player = new Vector3(1.5f, 1.6f, -7f);
            var bounds = new Bounds(new Vector3(0f, 1.95f, 0f), new Vector3(7.4f, 2.5f, 14f));
            var a = new TargetLayoutPlanner(1234).Plan(player, bounds, 2f, 4);
            Assert.That(TargetLayoutPlanner.MaximumTargetCount, Is.EqualTo(4));
            Assert.That(a, Has.Length.EqualTo(TargetLayoutPlanner.MaximumTargetCount));
            for (var i = 0; i < a.Length; i++)
            {
                Assert.That(bounds.Contains(a[i].Position), Is.True);
                var facing = (player - a[i].Position).normalized;
                Assert.That(Vector3.Dot(a[i].Rotation * Vector3.forward, facing), Is.GreaterThan(0.999f));
                for (var j = i + 1; j < a.Length; j++)
                    Assert.That(Vector3.Distance(a[i].Position, a[j].Position), Is.GreaterThanOrEqualTo(2f));
            }

            Assert.That(a.Select(slot => slot.Position.x).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(a.Select(slot => slot.Position.y).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(a.Select(slot => slot.Position.z).Distinct().Count(), Is.GreaterThan(1));
        }

        [Test]
        public void TargetLayout_NarrowValidBoundsStillReturnsRequestedCountDeterministically()
        {
            var bounds = new Bounds(new Vector3(0f, 1.7f, 0f), new Vector3(2.2f, 2f, 5.5f));
            var first = new TargetLayoutPlanner(9).Plan(new Vector3(0f, 1.6f, -4f), bounds, 1.5f, 4);
            var second = new TargetLayoutPlanner(9).Plan(new Vector3(0f, 1.6f, -4f), bounds, 1.5f, 4);
            Assert.That(first, Has.Length.EqualTo(4));
            for (var i = 0; i < first.Length; i++)
            {
                Assert.That(first[i].Position, Is.EqualTo(second[i].Position));
                Assert.That(bounds.Contains(first[i].Position), Is.True);
                for (var j = i + 1; j < first.Length; j++)
                    Assert.That(Vector3.Distance(first[i].Position, first[j].Position), Is.GreaterThanOrEqualTo(1.5f));
            }
        }

        [Test]
        public void MenuNavigate_DeflectionRequiresNeutralBeforeAnotherStep()
        {
            var left = InputSystem.AddDevice<XRSimulatedController>();
            InputSystem.SetDeviceUsage(left, CommonUsages.LeftHand);
            var right = InputSystem.AddDevice<XRSimulatedController>();
            InputSystem.SetDeviceUsage(right, CommonUsages.RightHand);
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = actions.AddActionMap("XR");
            var navigate = map.AddAction("MenuNavigate", InputActionType.Value, expectedControlLayout: "Vector2");
            navigate.AddBinding("<XRController>{LeftHand}/primary2DAxis");
            navigate.AddBinding("<XRController>{RightHand}/primary2DAxis");
            var menuObject = new GameObject("Menu");
            menuObject.SetActive(false);
            var menu = menuObject.AddComponent<QuestMenuController>();
            SetField(menu, "_navigateInput", new InputActionProperty(navigate));
            Invoke(menu, "OnEnable");
            Invoke(menu, "SetOpen", true);
            try
            {
                QueueStick(left, .80f);
                QueueStick(left, .79f);
                QueueStick(left, .81f);
                Assert.That(menu.SelectionIndex, Is.EqualTo(1));
                QueueStick(left, 0f);
                QueueStick(left, .80f);
                Assert.That(menu.SelectionIndex, Is.EqualTo(0));
            }
            finally
            {
                Invoke(menu, "OnDisable");
                GameInputGate.SetBlocked(false);
                Object.DestroyImmediate(menuObject);
                Object.DestroyImmediate(actions);
            }
        }

        [Test]
        public void MenuNavigate_OpeningWithHeldStickRequiresNeutral()
        {
            var left = InputSystem.AddDevice<XRSimulatedController>();
            InputSystem.SetDeviceUsage(left, CommonUsages.LeftHand);
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var navigate = actions.AddActionMap("XR").AddAction("MenuNavigate", InputActionType.Value, expectedControlLayout: "Vector2");
            navigate.AddBinding("<XRController>{LeftHand}/primary2DAxis");
            var menuObject = new GameObject("Menu");
            menuObject.SetActive(false);
            var menu = menuObject.AddComponent<QuestMenuController>();
            SetField(menu, "_navigateInput", new InputActionProperty(navigate));
            Invoke(menu, "OnEnable");
            try
            {
                QueueStick(left, .8f);
                Invoke(menu, "SetOpen", true);
                QueueStick(left, .81f);
                Assert.That(menu.SelectionIndex, Is.Zero);
                QueueStick(left, 0f);
                QueueStick(left, .8f);
                Assert.That(menu.SelectionIndex, Is.EqualTo(1));
            }
            finally
            {
                Invoke(menu, "OnDisable");
                GameInputGate.SetBlocked(false);
                Object.DestroyImmediate(menuObject);
                Object.DestroyImmediate(actions);
            }
        }

        [Test]
        public void QuestMenu_OpenPausesAndCloseRestoresPreviousTimeScaleAcrossDuplicateCalls()
        {
            var menuObject = new GameObject("Menu");
            menuObject.SetActive(false);
            var menu = menuObject.AddComponent<QuestMenuController>();
            var originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0.35f;
                Invoke(menu, "SetOpen", true);
                Assert.That(Time.timeScale, Is.Zero);

                Invoke(menu, "SetOpen", true);
                Invoke(menu, "SetOpen", false);

                Assert.That(Time.timeScale, Is.EqualTo(0.35f));
                Assert.That(GameInputGate.IsBlocked, Is.False);
            }
            finally
            {
                Invoke(menu, "OnDisable");
                GameInputGate.SetBlocked(false);
                Time.timeScale = originalTimeScale;
                Object.DestroyImmediate(menuObject);
            }
        }

        [Test]
        public void QuestMenu_AwakeSynchronizesAnInitiallyClosedMenuPanel()
        {
            var menuObject = new GameObject("Menu");
            menuObject.SetActive(false);
            var panel = new GameObject("Panel");
            var menu = menuObject.AddComponent<QuestMenuController>();
            SetField(menu, "_panel", panel);
            try
            {
                Assert.That(panel.activeSelf, Is.True);

                Invoke(menu, "Awake");

                Assert.That(menu.IsOpen, Is.False);
                Assert.That(panel.activeSelf, Is.False);
                Assert.That(GameInputGate.IsBlocked, Is.False);
            }
            finally
            {
                Invoke(menu, "OnDisable");
                GameInputGate.SetBlocked(false);
                Object.DestroyImmediate(menuObject);
                Object.DestroyImmediate(panel);
            }
        }

        [Test]
        public void QuestMenu_DisableWhileOpenRestoresPreviousTimeScale()
        {
            var menuObject = new GameObject("Menu");
            menuObject.SetActive(false);
            var menu = menuObject.AddComponent<QuestMenuController>();
            var originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0.6f;
                Invoke(menu, "SetOpen", true);

                Invoke(menu, "OnDisable");

                Assert.That(menu.IsOpen, Is.False);
                Assert.That(Time.timeScale, Is.EqualTo(0.6f));
                Assert.That(GameInputGate.IsBlocked, Is.False);
            }
            finally
            {
                GameInputGate.SetBlocked(false);
                Time.timeScale = originalTimeScale;
                Object.DestroyImmediate(menuObject);
            }
        }

        [Test]
        public void QuestMenu_OnDestroyCallbackRestoresPreviousTimeScale()
        {
            var menuObject = new GameObject("Menu");
            var menu = menuObject.AddComponent<QuestMenuController>();
            var originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0.8f;
                Invoke(menu, "SetOpen", true);

                Invoke(menu, "OnDestroy");

                Assert.That(Time.timeScale, Is.EqualTo(0.8f));
                Assert.That(GameInputGate.IsBlocked, Is.False);
            }
            finally
            {
                GameInputGate.SetBlocked(false);
                Time.timeScale = originalTimeScale;
                if (menuObject != null)
                {
                    Object.DestroyImmediate(menuObject);
                }
            }
        }

        [Test]
        public void BallPool_IgnoresBallToBallCollisionsOnly()
        {
            var prefabObject = new GameObject("BallPrefab");
            prefabObject.AddComponent<SphereCollider>();
            prefabObject.AddComponent<Rigidbody>();
            var prefab = prefabObject.AddComponent<Ball>();
            var poolObject = new GameObject("Pool");
            var pool = poolObject.AddComponent<BallPool>();
            SetField(pool, "_prefab", prefab);
            SetField(pool, "_size", 3);
            try
            {
                Invoke(pool, "Awake");
                var balls = GetField<List<Ball>>(pool, "_all");
                Assert.That(balls, Has.Count.EqualTo(3));
                for (var i = 0; i < balls.Count; i++)
                    for (var j = i + 1; j < balls.Count; j++)
                        Assert.That(Physics.GetIgnoreCollision(balls[i].GetComponent<Collider>(), balls[j].GetComponent<Collider>()), Is.True);

                var floor = new GameObject("Floor").AddComponent<BoxCollider>();
                Assert.That(Physics.GetIgnoreCollision(balls[0].GetComponent<Collider>(), floor), Is.False);
                Object.DestroyImmediate(floor.gameObject);
            }
            finally
            {
                Object.DestroyImmediate(poolObject);
                Object.DestroyImmediate(prefabObject);
            }
        }

        [Test]
        public void TargetTriggerHitPassesBallThroughBlocksDuplicatesAndHidesAfterFlash()
        {
            var gameObject = new GameObject("Game");
            var game = gameObject.AddComponent<DemoGameController>();
            Invoke(game, "Awake");
            var root = new GameObject("Targets");
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 1.6f, -7f);
            var targets = new TargetPanel[4];
            for (var i = 0; i < targets.Length; i++)
            {
                var target = new GameObject($"Target_{i}");
                target.transform.SetParent(root.transform, false);
                targets[i] = target.AddComponent<TargetPanel>();
                SetField(targets[i], "_flashDuration", .2f);
                Invoke(targets[i], "Awake");
            }
            var field = root.AddComponent<TargetLayoutField>();
            SetField(field, "_targets", targets);
            SetField(field, "_player", player.transform);
            SetField(field, "_minX", -3.7f);
            SetField(field, "_maxX", 3.7f);
            SetField(field, "_minZ", -2f);
            SetField(field, "_maxZ", 7.8f);
            SetField(field, "_minHeight", .7f);
            SetField(field, "_maxHeight", 3.2f);
            SetField(field, "_minimumSpacing", 2f);
            var ballObject = CreateIncomingBall("ThrownBall", out var ball);
            ball.TryGrab(root.transform);
            ball.Release(Vector3.forward * 10f);
            try
            {
                field.BeginWave(1, 3, 3);
                var firstPositions = targets.Take(3).Select(target => target.transform.position).ToArray();
                Assert.That(field.ActiveTargetCount, Is.EqualTo(3));
                Invoke(targets[0], "OnTriggerEnter", ballObject.GetComponent<Collider>());
                Invoke(targets[0], "OnTriggerEnter", ballObject.GetComponent<Collider>());
                Assert.That(game.TargetHitCount, Is.EqualTo(1), "one trigger overlap scores and reports haptics once while flashing");
                Assert.That(ball.State, Is.EqualTo(BallState.Thrown), "target trigger must let the ball pass through");
                Invoke(targets[0], "TickFlash", .19f);
                Assert.That(targets[0].gameObject.activeSelf, Is.True, "hit color remains visible for the flash duration");
                Invoke(targets[0], "TickFlash", .02f);
                Assert.That(targets[0].gameObject.activeSelf, Is.False);
                Assert.That(field.ActiveTargetCount, Is.EqualTo(2));
                Assert.That(InvokeResult<bool>(targets[1], "TryRegisterHit", ball), Is.True);
                Invoke(targets[1], "TickFlash", .21f);
                Assert.That(InvokeResult<bool>(targets[2], "TryRegisterHit", ball), Is.True);
                Invoke(targets[2], "TickFlash", .21f);
                Assert.That(field.ActiveTargetCount, Is.EqualTo(3), "last hit regenerates without an intermission");
                Assert.That(targets.Take(3).All(target => target.gameObject.activeSelf), Is.True);
                Assert.That(targets.Take(3).Select(target => target.transform.position).SequenceEqual(firstPositions), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(ballObject);
                Invoke(game, "OnDestroy");
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void HeldBallAimRayMatchesChargedThrowAndHidesOnRelease()
        {
            var gloveObject = new GameObject("Glove");
            gloveObject.transform.rotation = Quaternion.Euler(10f, 35f, 0f);
            var catchObject = new GameObject("CatchVolume");
            catchObject.transform.SetParent(gloveObject.transform, false);
            var gripAnchor = new GameObject("GripAnchor");
            gripAnchor.transform.SetParent(gloveObject.transform, false);
            catchObject.AddComponent<SphereCollider>().isTrigger = true;
            var catchVolume = catchObject.AddComponent<CatchVolume>();
            var rayObject = new GameObject("AimRay");
            rayObject.transform.SetParent(gloveObject.transform, false);
            var line = rayObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            var glove = gloveObject.AddComponent<GloveController>();
            SetField(glove, "_handRoot", gloveObject.transform);
            SetField(glove, "_gripAnchor", gripAnchor.transform);
            SetField(glove, "_catchVolume", catchVolume);
            SetField(glove, "_aimRay", line);
            SetField(glove, "_aimRayMinimumLength", .75f);
            SetField(glove, "_aimRayMaximumLength", 2.5f);
            var ballObject = new GameObject("Ball");
            ballObject.AddComponent<SphereCollider>();
            ballObject.AddComponent<Rigidbody>();
            var ball = ballObject.AddComponent<Ball>();
            try
            {
                Invoke(ball, "Awake");
                Invoke(glove, "Awake");
                ball.LaunchIncoming(catchObject.transform.position, Vector3.zero);
                Invoke(catchVolume, "OnTriggerEnter", ballObject.GetComponent<Collider>());
                glove.SetGripOverride(1f);
                Assert.That(line.enabled, Is.True);
                var rayDirection = (line.GetPosition(1) - line.GetPosition(0)).normalized;
                Assert.That(Vector3.Dot(rayDirection, glove.TriggerThrowDirection), Is.GreaterThan(.9999f));
                Assert.That(Vector3.Distance(line.GetPosition(0), line.GetPosition(1)), Is.EqualTo(.75f).Within(.001f));
                GetField<ThrowCharge>(glove, "_charge").Tick(ThrowCharge.FullChargeSeconds);
                Invoke(glove, "RefreshAimRay");
                Assert.That(Vector3.Distance(line.GetPosition(0), line.GetPosition(1)), Is.EqualTo(2.5f).Within(.001f));
                ball.transform.SetParent(rayObject.transform, true);
                Invoke(glove, "RefreshAimRay");
                Assert.That(line.enabled, Is.False, "a stale Held reference outside GripAnchor is not real ownership");
                Invoke(ball, "LateUpdate");
                Invoke(glove, "RefreshAimRay");
                Assert.That(line.enabled, Is.True);
                glove.SetTriggerOverride(1f);
                glove.SetTriggerOverride(0f);
                Assert.That(line.enabled, Is.False);
                Assert.That(Vector3.Dot(ball.Body.linearVelocity.normalized, glove.TriggerThrowDirection), Is.GreaterThan(.9999f));
            }
            finally
            {
                Object.DestroyImmediate(ballObject);
                Object.DestroyImmediate(gloveObject);
            }
        }

        [Test]
        public void GripRisingEdgeOpensOneSecondCatchWindowAndReleaseResetsIt()
        {
            var gloveObject = new GameObject("Glove");
            var catchObject = new GameObject("CatchVolume");
            catchObject.transform.SetParent(gloveObject.transform, false);
            catchObject.AddComponent<SphereCollider>().isTrigger = true;
            var catchVolume = catchObject.AddComponent<CatchVolume>();
            var glove = gloveObject.AddComponent<GloveController>();
            SetField(glove, "_handRoot", gloveObject.transform);
            SetField(glove, "_catchVolume", catchVolume);
            SetField(glove, "_grabWindowDuration", 1f);
            var firstObject = CreateIncomingBall("First", out var first);
            var secondObject = CreateIncomingBall("Second", out var second);
            try
            {
                Invoke(glove, "Awake");
                glove.SetGripOverride(1f);
                Invoke(glove, "TickGrabWindow", .75f);
                Invoke(catchVolume, "OnTriggerEnter", firstObject.GetComponent<Collider>());
                Assert.That(glove.IsHolding, Is.True, "entering before one second is catchable");
                glove.SetGripOverride(0f);

                glove.SetGripOverride(1f);
                Invoke(glove, "TickGrabWindow", 1.01f);
                Invoke(catchVolume, "OnTriggerEnter", secondObject.GetComponent<Collider>());
                Assert.That(glove.IsHolding, Is.False, "held Grip cannot catch after its window expires");
                glove.SetGripOverride(0f);
                glove.SetGripOverride(1f);
                Assert.That(glove.IsHolding, Is.True, "release and a new rising edge reopen the window");
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
                Object.DestroyImmediate(gloveObject);
            }
        }

        [Test]
        public void GripPressClosesHandAndReleaseReturnsItToOpenPose()
        {
            var glovePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GloveBallDemo/Prefabs/Glove.prefab");
            Assert.That(glovePrefab, Is.Not.Null, "Glove.prefab is missing");
            var gloveObject = (GameObject)PrefabUtility.InstantiatePrefab(glovePrefab);
            var ballObject = new GameObject("Ball");
            ballObject.AddComponent<SphereCollider>();
            ballObject.AddComponent<Rigidbody>();
            var ball = ballObject.AddComponent<Ball>();

            try
            {
                var glove = gloveObject.GetComponent<GloveController>();
                var animator = gloveObject.GetComponentInChildren<Animator>();
                var catchVolume = gloveObject.GetComponentInChildren<CatchVolume>();
                Assert.That(animator, Is.Not.Null, "Glove prefab needs a hand Animator");
                Assert.That(catchVolume, Is.Not.Null, "Glove prefab needs a CatchVolume");
                Invoke(glove, "Awake");
                Assert.That(animator.GetBool("Grabbed"), Is.True, "requested resting pose uses the source grab clip as the open hand");
                Invoke(ball, "Awake");
                ball.LaunchIncoming(catchVolume.transform.position, Vector3.zero);
                Invoke(catchVolume, "OnTriggerEnter", ballObject.GetComponent<Collider>());

                glove.SetGripOverride(1f);

                Assert.That(glove.IsHolding, Is.True);
                Assert.That(animator.GetBool("Grabbed"), Is.False, "Grip closes the hand using the source default pose");

                glove.SetGripOverride(0f);
                Assert.That(animator.GetBool("Grabbed"), Is.True, "Grip release reopens the hand");

                glove.SetGripOverride(1f);
                glove.NeutralizeForMenu();
                Assert.That(animator.GetBool("Grabbed"), Is.True, "menu interruption reopens the hand");
            }
            finally
            {
                Object.DestroyImmediate(ballObject);
                Object.DestroyImmediate(gloveObject);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PoolRecoveryFromRestartOrModeChangeClearsHeldChargeStateWithoutThrowing(bool changesToEndless)
        {
            var ballPrefab = new GameObject("BallPrefab");
            ballPrefab.SetActive(false);
            ballPrefab.AddComponent<SphereCollider>();
            ballPrefab.AddComponent<Rigidbody>();
            var prefabBall = ballPrefab.AddComponent<Ball>();
            Invoke(prefabBall, "Awake");

            var poolObject = new GameObject("Pool");
            poolObject.SetActive(false);
            var pool = poolObject.AddComponent<BallPool>();
            SeedPool(pool, prefabBall);

            var controllerObject = new GameObject("Game");
            controllerObject.SetActive(false);
            var game = controllerObject.AddComponent<DemoGameController>();
            SetField(game, "_pool", pool);
            Invoke(game, "Awake");

            var gloveObject = new GameObject("Glove");
            gloveObject.SetActive(false);
            var catchObject = new GameObject("CatchVolume");
            catchObject.transform.SetParent(gloveObject.transform, false);
            catchObject.AddComponent<SphereCollider>().isTrigger = true;
            var catchVolume = catchObject.AddComponent<CatchVolume>();
            var glove = gloveObject.AddComponent<GloveController>();

            try
            {
                var ball = pool.Take();
                Assert.That(ball, Is.Not.Null);
                ball.LaunchIncoming(Vector3.zero, Vector3.zero);
                SetField(glove, "_handRoot", gloveObject.transform);
                SetField(glove, "_catchVolume", catchVolume);
                Invoke(glove, "Awake");
                Invoke(glove, "OnEnable");
                Invoke(catchVolume, "OnTriggerEnter", ball.GetComponent<Collider>());
                glove.SetGripOverride(1f);
                glove.SetTriggerOverride(1f);
                Assert.That(glove.IsHolding, Is.True, "precondition: held ball");
                Assert.That(GetField<bool>(glove, "_isCharging"), Is.True);

                if (changesToEndless)
                {
                    game.SetPlayMode(DemoPlayMode.EndlessRandom);
                }
                else
                {
                    game.RestartRun();
                }

                Assert.That(ball.State, Is.EqualTo(BallState.Idle));
                Assert.That(pool.ActiveCount, Is.Zero);
                Assert.That(glove.IsHolding, Is.False);
                Assert.That(GetField<bool>(glove, "_gripHeld"), Is.False);
                Assert.That(GetField<bool>(glove, "_triggerHeld"), Is.False);
                Assert.That(GetField<bool>(glove, "_isCharging"), Is.False);
                glove.SetGripOverride(1f);
                Assert.That(GetField<bool>(glove, "_gripHeld"), Is.True);
                glove.SetGripOverride(0f);
            }
            finally
            {
                Invoke(glove, "OnDisable");
                Invoke(game, "OnDestroy");
                Object.DestroyImmediate(gloveObject);
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(poolObject);
                Object.DestroyImmediate(ballPrefab);
            }
        }

        [Test]
        public void MenuButtonToggleNeutralizesAChargedBallWithoutTreatingCancelAsAThrow()
        {
            var ballPrefab = new GameObject("BallPrefab");
            ballPrefab.SetActive(false);
            ballPrefab.AddComponent<SphereCollider>();
            ballPrefab.AddComponent<Rigidbody>();
            var prefabBall = ballPrefab.AddComponent<Ball>();
            Invoke(prefabBall, "Awake");

            var poolObject = new GameObject("Pool");
            poolObject.SetActive(false);
            var pool = poolObject.AddComponent<BallPool>();
            SeedPool(pool, prefabBall);

            var gameObject = new GameObject("Game");
            gameObject.SetActive(false);
            var game = gameObject.AddComponent<DemoGameController>();
            SetField(game, "_pool", pool);
            Invoke(game, "Awake");

            var gloveObject = new GameObject("Glove");
            gloveObject.SetActive(false);
            var catchObject = new GameObject("CatchVolume");
            catchObject.transform.SetParent(gloveObject.transform, false);
            catchObject.AddComponent<SphereCollider>().isTrigger = true;
            var catchVolume = catchObject.AddComponent<CatchVolume>();
            var glove = gloveObject.AddComponent<GloveController>();
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var menuAction = actions.AddActionMap("Menu").AddAction("Toggle", InputActionType.Button);
            menuAction.AddBinding("<XRSimulatedController>{LeftHand}/menuButton");
            var panel = new GameObject("Panel");
            var menuObject = new GameObject("QuestMenu");
            menuObject.SetActive(false);
            var menu = menuObject.AddComponent<QuestMenuController>();
            SetField(menu, "_game", game);
            SetField(menu, "_panel", panel);
            SetField(menu, "_leftMenuInput", new InputActionProperty(menuAction));
            Invoke(menu, "Awake");
            Invoke(menu, "OnEnable");
            var controller = InputSystem.AddDevice<XRSimulatedController>();
            InputSystem.SetDeviceUsage(controller, CommonUsages.LeftHand);

            try
            {
                var ball = pool.Take();
                Assert.That(ball, Is.Not.Null);
                ball.LaunchIncoming(Vector3.zero, Vector3.zero);
                SetField(glove, "_handRoot", gloveObject.transform);
                SetField(glove, "_catchVolume", catchVolume);
                Invoke(glove, "Awake");
                Invoke(glove, "OnEnable");
                Invoke(catchVolume, "OnTriggerEnter", ball.GetComponent<Collider>());
                glove.SetGripOverride(1f);
                glove.SetTriggerOverride(1f);

                InputSystem.QueueStateEvent(controller, new XRSimulatedControllerState().WithButton(ControllerButton.MenuButton));
                InputSystem.Update();

                Assert.That(menu.IsOpen, Is.True);
                Assert.That(GameInputGate.IsBlocked, Is.True);
                Assert.That(ball.State, Is.EqualTo(BallState.Idle));
                Assert.That(glove.IsHolding, Is.False);
                Assert.That(GetField<bool>(glove, "_isCharging"), Is.False);

                InputSystem.QueueStateEvent(controller, default(XRSimulatedControllerState));
                InputSystem.Update();
                Assert.That(ball.State, Is.Not.EqualTo(BallState.Thrown));

                InputSystem.QueueStateEvent(controller, new XRSimulatedControllerState().WithButton(ControllerButton.MenuButton));
                InputSystem.Update();
                Assert.That(menu.IsOpen, Is.False);
                Assert.That(GameInputGate.IsBlocked, Is.False);
            }
            finally
            {
                Invoke(menu, "OnDisable");
                Invoke(glove, "OnDisable");
                Invoke(game, "OnDestroy");
                GameInputGate.SetBlocked(false);
                Object.DestroyImmediate(gloveObject);
                Object.DestroyImmediate(menuObject);
                Object.DestroyImmediate(panel);
                Object.DestroyImmediate(actions);
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(poolObject);
                Object.DestroyImmediate(ballPrefab);
            }
        }

        [Test]
        public void GripPressOnOverlappingIncomingBallSnapsToGripAnchorAndFollowsIt()
        {
            var gloveObject = new GameObject("Glove");
            var anchor = new GameObject("HoldAnchor");
            anchor.transform.SetParent(gloveObject.transform, false);
            var catchObject = new GameObject("CatchVolume");
            catchObject.transform.SetParent(gloveObject.transform, false);
            catchObject.AddComponent<SphereCollider>().isTrigger = true;
            var catchVolume = catchObject.AddComponent<CatchVolume>();
            var glove = gloveObject.AddComponent<GloveController>();

            var ballObject = new GameObject("Ball");
            ballObject.AddComponent<SphereCollider>();
            ballObject.AddComponent<Rigidbody>();
            var ball = ballObject.AddComponent<Ball>();
            Invoke(ball, "Awake");
            ball.LaunchIncoming(Vector3.zero, Vector3.zero);

            SetField(glove, "_handRoot", gloveObject.transform);
            SetField(glove, "_gripAnchor", anchor.transform);
            SetField(glove, "_catchVolume", catchVolume);
            Invoke(glove, "Awake");
            Invoke(catchVolume, "OnTriggerEnter", ballObject.GetComponent<Collider>());

            Assert.That(glove.IsHolding, Is.False);
            Assert.That(ball.State, Is.EqualTo(BallState.Incoming));
            glove.SetGripOverride(1f);
            Assert.That(glove.IsHolding, Is.True);
            Assert.That(ball.transform.parent, Is.EqualTo(anchor.transform));
            Assert.That(ball.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(Quaternion.Angle(ball.transform.localRotation, Quaternion.identity), Is.LessThan(0.001f));

            var stalePosition = ball.Body.position;
            var staleRotation = ball.Body.rotation;
            gloveObject.transform.SetPositionAndRotation(
                new Vector3(2f, 3f, 4f),
                Quaternion.Euler(12f, 34f, 56f));

            // Reproduce the observed Air Link frame: the tracked hand has moved but the held
            // kinematic ball still exposes its previous world pose.
            ball.transform.SetPositionAndRotation(stalePosition, staleRotation);
            Assert.That(
                Vector3.Distance(ball.transform.position, anchor.transform.position),
                Is.GreaterThan(0.1f),
                "precondition: the physics pose must be stale before LateUpdate");

            Invoke(ball, "LateUpdate");

            Assert.That(ball.transform.parent, Is.EqualTo(anchor.transform));
            Assert.That(Vector3.Distance(ball.transform.position, anchor.transform.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(ball.transform.rotation, anchor.transform.rotation), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(ball.Body.position, anchor.transform.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(ball.Body.rotation, anchor.transform.rotation), Is.LessThan(0.001f));
            Object.DestroyImmediate(ballObject);
            Object.DestroyImmediate(gloveObject);
        }

        [Test]
        public void TriggerRelease_LeavesReleasedBallThrownUntilItExitsCatchVolume()
        {
            var gloveObject = new GameObject("Glove");
            var anchor = new GameObject("HoldAnchor");
            anchor.transform.SetParent(gloveObject.transform, false);
            var catchObject = new GameObject("CatchVolume");
            catchObject.transform.SetParent(gloveObject.transform, false);
            catchObject.AddComponent<SphereCollider>().isTrigger = true;
            var catchVolume = catchObject.AddComponent<CatchVolume>();
            var glove = gloveObject.AddComponent<GloveController>();

            var ballObject = new GameObject("Ball");
            ballObject.AddComponent<SphereCollider>();
            ballObject.AddComponent<Rigidbody>();
            var ball = ballObject.AddComponent<Ball>();
            Invoke(ball, "Awake");
            ball.LaunchIncoming(Vector3.zero, Vector3.zero);
            SetField(glove, "_handRoot", gloveObject.transform);
            SetField(glove, "_catchVolume", catchVolume);
            Invoke(glove, "Awake");
            Invoke(catchVolume, "OnTriggerEnter", ballObject.GetComponent<Collider>());
            glove.SetGripOverride(1f);
            Assert.That(glove.IsHolding, Is.True, "precondition: grip captures the overlapping incoming ball");

            glove.SetTriggerOverride(1f);
            Invoke(glove, "Update");
            glove.SetTriggerOverride(0f);
            Invoke(glove, "Update");
            Invoke(glove, "Update");

            Assert.That(glove.IsHolding, Is.False);
            Assert.That(ball.State, Is.EqualTo(BallState.Thrown));
            Assert.That(ball.Body.linearVelocity.magnitude, Is.GreaterThan(0f));
            Object.DestroyImmediate(ballObject);
            Object.DestroyImmediate(gloveObject);
        }

        [Test]
        public void GripReleasePhysicallyThrowsTheHeldBall()
        {
            var gloveObject = new GameObject("Glove");
            var catchObject = new GameObject("CatchVolume");
            catchObject.transform.SetParent(gloveObject.transform, false);
            catchObject.AddComponent<SphereCollider>().isTrigger = true;
            var catchVolume = catchObject.AddComponent<CatchVolume>();
            var glove = gloveObject.AddComponent<GloveController>();
            var ballObject = new GameObject("Ball");
            ballObject.AddComponent<SphereCollider>();
            ballObject.AddComponent<Rigidbody>();
            var ball = ballObject.AddComponent<Ball>();
            Invoke(ball, "Awake");
            ball.LaunchIncoming(Vector3.zero, Vector3.zero);
            SetField(glove, "_handRoot", gloveObject.transform);
            SetField(glove, "_catchVolume", catchVolume);
            Invoke(glove, "Awake");
            Invoke(catchVolume, "OnTriggerEnter", ballObject.GetComponent<Collider>());
            glove.SetGripOverride(1f);
            gloveObject.transform.position = Vector3.forward;
            Invoke(glove, "Update");
            glove.SetGripOverride(0f);

            Assert.That(ball.State, Is.EqualTo(BallState.Thrown));
            Assert.That(ball.Body.linearVelocity.magnitude, Is.GreaterThan(0f));
            Object.DestroyImmediate(ballObject);
            Object.DestroyImmediate(gloveObject);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ChargeReleaseWinsWhenGripAndTriggerCancelInEitherOrder(bool triggerCancelsFirst)
        {
            var gloveObject = new GameObject("Glove");
            var catchObject = new GameObject("CatchVolume");
            catchObject.transform.SetParent(gloveObject.transform, false);
            catchObject.AddComponent<SphereCollider>().isTrigger = true;
            var catchVolume = catchObject.AddComponent<CatchVolume>();
            var glove = gloveObject.AddComponent<GloveController>();
            var ballObject = new GameObject("Ball");
            ballObject.AddComponent<SphereCollider>();
            ballObject.AddComponent<Rigidbody>();
            var ball = ballObject.AddComponent<Ball>();
            Invoke(ball, "Awake");
            ball.LaunchIncoming(Vector3.zero, Vector3.zero);
            SetField(glove, "_handRoot", gloveObject.transform);
            SetField(glove, "_catchVolume", catchVolume);
            Invoke(glove, "Awake");
            Invoke(catchVolume, "OnTriggerEnter", ballObject.GetComponent<Collider>());
            glove.SetGripOverride(1f);
            glove.SetTriggerOverride(1f);
            Assert.That(GetField<bool>(glove, "_isCharging"), Is.True);

            if (triggerCancelsFirst)
            {
                glove.SetTriggerOverride(0f);
                glove.SetGripOverride(0f);
            }
            else
            {
                glove.SetGripOverride(0f);
                glove.SetTriggerOverride(0f);
            }

            Assert.That(GetField<bool>(glove, "_isCharging"), Is.False);
            Assert.That(glove.IsHolding, Is.False);
            Assert.That(ball.State, Is.EqualTo(BallState.Thrown));
            Assert.That(ball.Body.linearVelocity, Is.EqualTo(Vector3.forward * ThrowCharge.MinimumSpeed));
            Object.DestroyImmediate(ballObject);
            Object.DestroyImmediate(gloveObject);
        }

        [Test]
        public void LeftAndRightGlovesUseDistinctGrabReleaseAndChargeEvents()
        {
            var leftObject = new GameObject("Left");
            var rightObject = new GameObject("Right");
            var left = leftObject.AddComponent<GloveController>();
            var right = rightObject.AddComponent<GloveController>();
            SetField(left, "_side", GloveSide.Left);
            SetField(right, "_side", GloveSide.Right);
            Assert.That(GetProperty<DemoHapticEvent>(left, "GrabEvent"), Is.EqualTo(DemoHapticEvent.LeftGrab));
            Assert.That(GetProperty<DemoHapticEvent>(right, "GrabEvent"), Is.EqualTo(DemoHapticEvent.RightGrab));
            Assert.That(GetProperty<DemoHapticEvent>(left, "ReleaseEvent"), Is.EqualTo(DemoHapticEvent.LeftRelease));
            Assert.That(GetProperty<DemoHapticEvent>(right, "ChargeEvent"), Is.EqualTo(DemoHapticEvent.RightChargeLoop));
            Object.DestroyImmediate(leftObject);
            Object.DestroyImmediate(rightObject);
        }

        [Test]
        public void ChargeLoopsRemainIndependentWhenTheLeftLoopEnds()
        {
            var relayObject = new GameObject("Relay");
            var relay = relayObject.AddComponent<HapticEventRelay>();
            Invoke(relay, "Awake");
            HapticEventRelay.BeginLoop(DemoHapticEvent.LeftChargeLoop, Vector3.zero, 1f);
            HapticEventRelay.BeginLoop(DemoHapticEvent.RightChargeLoop, Vector3.zero, 1f);
            HapticEventRelay.EndLoop(DemoHapticEvent.LeftChargeLoop);

            var active = GetField<HashSet<DemoHapticEvent>>(relay, "_activeLoops");
            Assert.That(active.Contains(DemoHapticEvent.LeftChargeLoop), Is.False);
            Assert.That(active.Contains(DemoHapticEvent.RightChargeLoop), Is.True);
            Object.DestroyImmediate(relayObject);
        }

        [Test]
        public void LauncherWarningIsHapticOnlyAndLaunchAudioRequiresSuccessfulEmission()
        {
            var relayObject = new GameObject("Relay");
            var relay = relayObject.AddComponent<HapticEventRelay>();
            Invoke(relay, "Awake");
            var haptics = new List<DemoHapticEvent>();
            var audio = new List<DemoHapticEvent>();
            relay.EventReported += (evt, _, __) => haptics.Add(evt);
            var audioEvent = typeof(HapticEventRelay).GetEvent("AudioRequested");
            Assert.That(audioEvent, Is.Not.Null, "diagnostics need an audio-only request seam that does not play a clip");
            audioEvent.AddEventHandler(relay, new System.Action<DemoHapticEvent, Vector3, float>((evt, _, __) => audio.Add(evt)));

            var poolObject = new GameObject("Pool");
            var pool = poolObject.AddComponent<BallPool>();
            var ballObject = new GameObject("Ball");
            ballObject.AddComponent<SphereCollider>();
            ballObject.AddComponent<Rigidbody>();
            var ball = ballObject.AddComponent<Ball>();
            Invoke(ball, "Awake");
            SeedPool(pool, ball);

            var launcherObject = new GameObject("Launcher");
            var launcher = launcherObject.AddComponent<BallLauncher>();
            SetField(launcher, "_muzzle", launcherObject.transform);
            Invoke(launcher, "Awake");
            launcher.Bind(pool);
            try
            {
                Assert.That(launcher.RequestFire(Vector3.forward * 5f, 10f, AimKind.Direct), Is.True);
                Assert.That(haptics, Is.EqualTo(new[] { DemoHapticEvent.BallIncomingWarning }));
                Assert.That(audio, Is.Empty, "telegraph must not request audible feedback");
                Invoke(launcher, "FireNow");
                Assert.That(ball.State, Is.EqualTo(BallState.Incoming));
                Assert.That(audio, Is.EqualTo(new[] { DemoHapticEvent.BallIncomingWarning }));

                Assert.That(launcher.RequestFire(Vector3.forward * 5f, 10f, AimKind.Direct), Is.True);
                Invoke(launcher, "FireNow");
                Assert.That(audio, Has.Count.EqualTo(1), "exhausted pool emits no launch audio");

                SetField(launcher, "_lobElevation", 0f);
                Assert.That(launcher.RequestFire(Vector3.forward * 5f, 0f, AimKind.Lob), Is.True);
                Invoke(launcher, "FireNow");
                Assert.That(audio, Has.Count.EqualTo(1), "unsolved trajectory emits no launch audio");
                Assert.That(launcher.ShotsFired, Is.EqualTo(1));
            }
            finally
            {
                Invoke(relay, "OnDestroy");
                Object.DestroyImmediate(launcherObject);
                Object.DestroyImmediate(ballObject);
                Object.DestroyImmediate(poolObject);
                Object.DestroyImmediate(relayObject);
            }
        }

        [Test]
        public void LauncherReturnHitStillStunsOnlyOnConfiguredThreshold()
        {
            var launcherObject = new GameObject("Launcher");
            var launcher = launcherObject.AddComponent<BallLauncher>();
            SetField(launcher, "_hitsToStun", 3);
            Invoke(launcher, "Awake");
            Assert.That(launcher.RegisterReturnHit(), Is.False);
            Assert.That(launcher.RegisterReturnHit(), Is.False);
            Assert.That(launcher.RegisterReturnHit(), Is.True);
            Assert.That(launcher.IsStunned, Is.True);
            Assert.That(launcher.RegisterReturnHit(), Is.False, "hits while stunned do not retrigger stun");
            Object.DestroyImmediate(launcherObject);
        }

        [Test]
        public void SimulatedTriggerButton_PerformedAndCanceledDriveTheInjectedInputReference()
        {
            var controller = InputSystem.AddDevice<XRSimulatedController>();
            InputSystem.SetDeviceUsage(controller, CommonUsages.RightHand);

            var gloveObject = new GameObject("Glove");
            var anchor = new GameObject("HoldAnchor");
            anchor.transform.SetParent(gloveObject.transform, false);
            var catchObject = new GameObject("CatchVolume");
            catchObject.transform.SetParent(gloveObject.transform, false);
            catchObject.AddComponent<SphereCollider>().isTrigger = true;
            var catchVolume = catchObject.AddComponent<CatchVolume>();
            var glove = gloveObject.AddComponent<GloveController>();
            var ballObject = new GameObject("Ball");
            ballObject.AddComponent<SphereCollider>();
            ballObject.AddComponent<Rigidbody>();
            var ball = ballObject.AddComponent<Ball>();
            Invoke(ball, "Awake");
            ball.LaunchIncoming(Vector3.zero, Vector3.zero);

            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var action = actions.AddActionMap("XR").AddAction("TriggerRight", InputActionType.Button);
            action.AddBinding("<XRSimulatedController>{RightHand}/triggerButton");
            var performed = 0;
            var canceled = 0;
            action.performed += _ => performed++;
            action.canceled += _ => canceled++;
            var reference = InputActionReference.Create(action);
            action.Enable();
            Assert.That(action.controls.Count, Is.EqualTo(1));
            SetField(glove, "_triggerInput", new InputActionProperty(reference));
            SetField(glove, "_handRoot", gloveObject.transform);
            SetField(glove, "_catchVolume", catchVolume);
            Invoke(glove, "Awake");
            Invoke(glove, "OnEnable");
            Invoke(catchVolume, "OnTriggerEnter", ballObject.GetComponent<Collider>());
            glove.SetGripOverride(1f);

            InputSystem.QueueStateEvent(controller, new XRSimulatedControllerState().WithButton(ControllerButton.TriggerButton));
            InputSystem.Update();
            Assert.That(performed, Is.EqualTo(1));
            Assert.That(GetField<bool>(glove, "_triggerHeld"), Is.True);
            Assert.That(glove.Charge01, Is.Zero, "performed starts the charge at zero");

            InputSystem.QueueStateEvent(controller, default(XRSimulatedControllerState));
            InputSystem.Update();

            Assert.That(canceled, Is.EqualTo(1));
            Assert.That(GetField<bool>(glove, "_triggerHeld"), Is.False);
            Assert.That(glove.IsHolding, Is.False);
            Assert.That(ball.State, Is.EqualTo(BallState.Thrown));
            Assert.That(ball.Body.linearVelocity.magnitude, Is.GreaterThan(0f));
            Object.DestroyImmediate(ballObject);
            Object.DestroyImmediate(gloveObject);
            Object.DestroyImmediate(actions);
        }

        private static void QueueStick(XRSimulatedController controller, float y)
        {
            InputSystem.QueueStateEvent(controller, new XRSimulatedControllerState { primary2DAxis = new Vector2(0f, y) });
            InputSystem.Update();
        }

        private static GameObject CreateIncomingBall(string name, out Ball ball)
        {
            var gameObject = new GameObject(name);
            gameObject.AddComponent<SphereCollider>();
            gameObject.AddComponent<Rigidbody>();
            ball = gameObject.AddComponent<Ball>();
            Invoke(ball, "Awake");
            ball.LaunchIncoming(Vector3.zero, Vector3.zero);
            return gameObject;
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static T GetProperty<T>(object target, string name) =>
            (T)target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static T InvokeResult<T>(object target, string name, object argument) =>
            (T)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, new[] { argument });

        private static void SeedPool(BallPool pool, Ball ball)
        {
            ball.BindPool(pool);
            ball.ResetToIdle();
            GetField<List<Ball>>(pool, "_all").Add(ball);
            GetField<Queue<Ball>>(pool, "_free").Enqueue(ball);
        }

        private static void Invoke(object target, string name, object argument = null) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, argument == null ? null : new[] { argument });
    }
}
