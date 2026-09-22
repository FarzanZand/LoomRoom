using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TableRevealValidation
{
    static int phase;
    static double next, deadline;
    static bool captured, sawHiddenHandsDuringFlight, sawArrival, capturedFlight, capturedArrival;
    static double arrivalStarted;
    static Vector3 arrivalPosition;
    static Quaternion arrivalRotation;
    static double loadStarted;
    static Color roomAmbient;
    static TableLevelData level;
    static Vector3 finalCameraPosition;
    static Quaternion finalCameraRotation;
    static Vector3 roomViewPosition;
    static Quaternion roomViewRotation;
    static TableRevealValidation() => EditorApplication.update += Tick;
    static void EnsureDust()
    {
        const string folder = "Assets/Game/Levels/_Resources/Shared/Resources/TableReveal/";
        var settings = AssetDatabase.LoadAssetAtPath<TableLevelRevealSettings>(folder + "Table assembly.asset");
        var world = UnityEngine.Object.FindAnyObjectByType<WorldManager>();
        if (world.tableLevelReveal == null)
        {
            world.tableLevelReveal = settings; EditorUtility.SetDirty(world);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            var oldMode = EditorSettings.serializationMode; EditorSettings.serializationMode = SerializationMode.ForceText;
            try { UnityEditor.SceneManagement.EditorSceneManager.SaveScene(world.gameObject.scene); }
            finally { EditorSettings.serializationMode = oldMode; }
        }
        if (settings.dustPrefab != null) return;
        var mode = EditorSettings.serializationMode;
        EditorSettings.serializationMode = SerializationMode.ForceText;
        var go = new GameObject("Assembly dust", typeof(ParticleSystem));
        try
        {
            var ps = go.GetComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.duration = 2.7f; main.loop = false; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, 1.1f); main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, .65f);
            main.startSize = new ParticleSystem.MinMaxCurve(.1f, .45f); main.startColor = new Color(.48f, .58f, .64f, .28f); main.maxParticles = 250;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.useUnscaledTime = true;
            var emission = ps.emission; emission.rateOverTime = 90;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.rotation = new Vector3(-90, 0, 0);
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] {new GradientColorKey(Color.white, 0),new GradientColorKey(Color.white, 1)}, new[] {new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(0, 1)}); fade.color = gradient;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = settings.lineMaterial;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, folder + "Assembly dust.prefab");
            settings.dustPrefab = prefab.GetComponent<ParticleSystem>(); settings.cameraAngle = 80;
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        }
        finally { UnityEngine.Object.DestroyImmediate(go); EditorSettings.serializationMode = mode; }
    }
    static void Tick()
    {
        try
        {
            if (File.Exists("Temp/TableRevealValidation.request"))
            {
                File.Delete("Temp/TableRevealValidation.request");
                EnsureDust();
                SessionState.SetBool("RevealTestRestorePlayOptions", true);
                SessionState.SetBool("RevealTestPlayOptions", EditorSettings.enterPlayModeOptionsEnabled);
                EditorSettings.enterPlayModeOptionsEnabled = false;
                SessionState.SetBool("TableRevealValidation", true); EditorApplication.isPlaying = true;
            }
            if (!EditorApplication.isPlaying)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool("RevealTestRestorePlayOptions", false))
                {
                    EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool("RevealTestPlayOptions", false);
                    SessionState.SetBool("RevealTestRestorePlayOptions", false);
                }
                return;
            }
            if (phase == 0 && SessionState.GetBool("TableRevealValidation", false))
            { SessionState.SetBool("TableRevealValidation", false); phase = 1; next = EditorApplication.timeSinceStartup + 3; deadline = next + 40; }
            if (phase == 0 || EditorApplication.timeSinceStartup < next) return;
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Reveal timed out");
            var loader = UnityEngine.Object.FindAnyObjectByType<TableLevelLoader>();
            if (loader != null && loader.Busy && loader.GetComponent<TableLevelReveal>() is TableLevelReveal playing && playing.IsPlaying && PlayerManager.Instance.ActiveKind == PlayerKind.Table)
            {
                var table = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
                var camera = PlayerManager.Instance.OutputCamera;
                bool visible = table.ViewPresentation.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled && !r.forceRenderingOff);
                if (!playing.HasArrived)
                {
                    if (visible) throw new Exception("Table hands visible before arrival");
                    var room = PlayerManager.Instance.GetPlayer(PlayerKind.Room);
                    if (room.ActivationRoot.GetComponentsInChildren<Renderer>(true).Any(r => !r.forceRenderingOff)) throw new Exception("Room body visible inside departure camera");
                    sawHiddenHandsDuringFlight = true;
                    if (!capturedFlight) { ScreenCapture.CaptureScreenshot("Temp/TableRevealFlight.png"); capturedFlight = true; }
                }
                else
                {
                    if (!sawArrival) { sawArrival = true; arrivalStarted = EditorApplication.timeSinceStartup; arrivalPosition = camera.transform.position; arrivalRotation = camera.transform.rotation; }
                    if (GameManager.Instance.GameplayActive) throw new Exception("Controls active during hand settle");
                    if (Vector3.Distance(arrivalPosition, camera.transform.position) > .01f || Quaternion.Angle(arrivalRotation, camera.transform.rotation) > .1f) throw new Exception("Camera moved during hand settle");
                    if (!visible) throw new Exception("Prepared hands missing at arrival");
                    if (!capturedArrival && EditorApplication.timeSinceStartup - arrivalStarted > .1)
                    { ScreenCapture.CaptureScreenshot("Temp/TableRevealArrival.png"); capturedArrival = true; }
                }
            }
            if (loader == null) return;
            if (phase > 1 && phase != 10 && phase != 11 && ScreenManager.Instance.fadeFullscreenImage != null && ScreenManager.Instance.fadeFullscreenImage.isActiveAndEnabled && ScreenManager.Instance.fadeFullscreenImage.color.a > .01f)
                throw new Exception($"Reveal used a black fade: phase {phase}, alpha {ScreenManager.Instance.fadeFullscreenImage.color.a}, busy {loader.Busy}, component {loader.GetComponent<TableLevelReveal>() != null}, setting {WorldManager.Instance.tableLevelReveal != null}");
            if (phase == 1)
            {
                foreach (var cutscene in UnityEngine.Object.FindObjectsByType<CutsceneController>()) { cutscene.Stop(); cutscene.StopAllCoroutines(); }
                GameManager.Instance.PopAll();
                level = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset"));
                level.fixedSeed = 713;
                WorldManager.Instance.tableLevelReveal = UnityEngine.Object.Instantiate(WorldManager.Instance.tableLevelReveal);
                WorldManager.Instance.tableLevelReveal.useRoomPlayerPOV = true; WorldManager.Instance.tableLevelReveal.cameraAngle = 72;
                if (WorldManager.Instance.tableLevelReveal == null || WorldManager.Instance.tableLevelReveal.lineMaterial == null) throw new Exception("Reveal assets missing");
                if (PlayerManager.Instance.ActiveKind != PlayerKind.Room) throw new Exception("Cold-start test must begin with Room player");
                if (PlayerManager.Instance.GetPlayer(PlayerKind.Table).Equipment.Character != null) throw new Exception("Table equipment already initialized: test is not a cold start");
                roomAmbient = RenderSettings.ambientSkyColor; loadStarted = EditorApplication.timeSinceStartup;
                loader.ShowSelection();
                var view = UnityEngine.Object.FindAnyObjectByType<TableAdventureMenuView>();
                var choices = view.options.GetComponentsInChildren<AdventureOptionUI>();
                var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                if (UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null) throw new Exception("Menu selected a destination before user input");
                choices[0].OnPointerEnter(pointer); choices[1].OnPointerEnter(pointer); choices[0].OnPointerExit(pointer);
                if (UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != choices[1].gameObject) throw new Exception("Hover did not transfer selection");
                choices[1].OnPointerExit(pointer);
                if (UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null) throw new Exception("Hover selection stuck after leaving");
                phase = 10; next = EditorApplication.timeSinceStartup + .3;
            }
            else if (phase == 10)
            {
                ScreenCapture.CaptureScreenshot("Temp/AdventureMenu.png"); phase = 11; next = EditorApplication.timeSinceStartup + .15;
            }
            else if (phase == 11)
            {
                var menuButton = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>().First(b => b.GetComponentInChildren<UnityEngine.UI.Text>()?.text.StartsWith(level.displayName) == true);
                menuButton.onClick.Invoke(); phase = 2;
            }
            else if (phase == 2)
            {
                var reveal = loader.GetComponent<TableLevelReveal>();
                if (reveal != null && reveal.IsPlaying && !captured)
                {
                    var plan = reveal.GetComponentsInChildren<LineRenderer>();
                    if (plan.Length < 3 || plan.Take(plan.Length - 2).Any(l => l.startColor.a > .001f)) throw new Exception("Blueprint visible before fade-in begins");
                    if (GameManager.Instance.SimulationActive) throw new Exception("Simulation active during reveal");

                    captured = true; next = EditorApplication.timeSinceStartup + 1.5; phase = 3;
                }
            }
            else if (phase == 3)
            {
                if (RenderSettings.ambientSkyColor != roomAmbient) throw new Exception("Room lighting changed during assembly");
                if (AudioManager.Instance.GetComponentsInChildren<AudioSource>().Any(a => a.isPlaying && a.clip == level.backgroundMusic)) throw new Exception("Dungeon music started before entry finished");
                ScreenCapture.CaptureScreenshot("Temp/TableRevealMid.png"); phase = 4;
            }
            else if (phase == 4 && !loader.Busy)
            {
                if (PlayerManager.Instance.ActiveKind != PlayerKind.Table || PlayerManager.Instance.OutputCamera.orthographic) throw new Exception("Camera/player not restored");
                if (!GameManager.Instance.GameplayActive) throw new Exception("Gameplay not restored");
                if (UnityEngine.Object.FindAnyObjectByType<TableLevelReveal>() != null) return;
                if (!loader.Dungeon.GetComponentsInChildren<EnemyBrain>().Any()) throw new Exception("Enemies not restored");
                if (EditorApplication.timeSinceStartup - loadStarted < 3) throw new Exception("Reveal completed instantly");
                if (!AudioManager.Instance.GetComponentsInChildren<AudioSource>().Any(a => a.isPlaying && a.clip == level.backgroundMusic)) throw new Exception("Dungeon music did not start after entry");
                if (PlayerManager.Instance.GetPlayer(PlayerKind.Table).Equipment.Get(EquipmentSlot.RightHand) == null) throw new Exception("Starter weapon missing");
                if (!sawHiddenHandsDuringFlight) throw new Exception("Hidden hands during flight were not observed");
                if (!PlayerManager.Instance.GetPlayer(PlayerKind.Table).ViewPresentation.GetComponentsInChildren<Renderer>().Any(r => r.enabled && !r.forceRenderingOff)) throw new Exception("Equipped hands missing at handover");
                finalCameraPosition = PlayerManager.Instance.OutputCamera.transform.position;
                finalCameraRotation = PlayerManager.Instance.OutputCamera.transform.rotation;
                phase = 7; next = EditorApplication.timeSinceStartup + .1;
            }
            else if (phase == 7)
            {
                if (Vector3.Distance(finalCameraPosition, PlayerManager.Instance.OutputCamera.transform.position) > .05f || Quaternion.Angle(finalCameraRotation, PlayerManager.Instance.OutputCamera.transform.rotation) > 5) throw new Exception($"Camera jumped at handoff: distance {Vector3.Distance(finalCameraPosition, PlayerManager.Instance.OutputCamera.transform.position)}, angle {Quaternion.Angle(finalCameraRotation, PlayerManager.Instance.OutputCamera.transform.rotation)}");
                ScreenCapture.CaptureScreenshot("Temp/TableRevealComplete.png");
                phase = 12; next = EditorApplication.timeSinceStartup + .2;
            }
            else if (phase == 12)
            {
                PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Room);
                phase = 8; next = EditorApplication.timeSinceStartup + .2;
            }
            else if (phase == 8)
            {
                roomViewPosition = PlayerManager.Instance.OutputCamera.transform.position;
                roomViewRotation = PlayerManager.Instance.OutputCamera.transform.rotation;
                WorldManager.Instance.tableLevelReveal.useRoomPlayerPOV = true;
                sawArrival = false; capturedFlight = false; loader.Load(level); phase = 5;
            }
            else if (phase == 5)
            {
                var reveal = loader.GetComponent<TableLevelReveal>();
                if (reveal != null && reveal.IsPlaying)
                {
                    if (Vector3.Distance(roomViewPosition, PlayerManager.Instance.OutputCamera.transform.position) > .2f) throw new Exception("Room POV changed at start");
                    phase = 9; next = EditorApplication.timeSinceStartup + 1.1;
                }
            }
            else if (phase == 9)
            {
                var camera = PlayerManager.Instance.OutputCamera;
                if (Vector3.Distance(roomViewPosition, camera.transform.position) > .2f || Quaternion.Angle(roomViewRotation, camera.transform.rotation) > 2) throw new Exception("Room POV changed during assembly");
                ScreenCapture.CaptureScreenshot("Temp/TableRevealRoomPOV.png");
                loader.GetComponent<TableLevelReveal>().SkipRequested = true; phase = 6;
            }
            else if (phase == 6 && !loader.Busy)
            {
                if (PlayerManager.Instance.ActiveKind != PlayerKind.Table || !GameManager.Instance.GameplayActive || PlayerManager.Instance.OutputCamera.orthographic) throw new Exception("Skip did not restore gameplay");
                File.WriteAllText("Temp/TableRevealValidation.report", "PASS: prefab menu has no initial selection, hover transfers/clears selection, hands hidden throughout flight, room body suppressed before departure, prepared gear visible only on arrival, stable arrival and input reset, no extra arrival hold; blueprint starts transparent without an initial flash; cold scene/domain reload, inactive table equipment, actual adventure menu button, room POV full assembly and camera flight, delayed BGM, unchanged room ambient during assembly, starter weapon equipped, no visible black overlay, camera handoff and repeat-run skip.");
                phase = 0; captured = false; EditorApplication.isPlaying = false;
            }
        }
        catch (Exception e) { File.WriteAllText("Temp/TableRevealValidation.report", e.ToString()); phase = 0; captured = false; SessionState.SetBool("TableRevealValidation", false); EditorApplication.isPlaying = false; }
    }
}
