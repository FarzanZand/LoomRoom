using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Renders a campaign event's picture from a small staged scene in a preview scene (nothing touches the open scene),
// run from an eval: CampaignEventArt.WanderingMerchant(). The scene is described in code so it can be re-rendered after
// a tweak; the result is a PNG beside the event asset, imported as a sprite.
public static class CampaignEventArt
{
    const string Dir = "Assets/Game/Campaign/Encounters/Act 2/Wandering merchant";

    // Rendered small and shown with point filtering, so it reads as pixel art like the rest of the game.
    public static string WanderingMerchant(int width = 640, int height = 360)
    {
        Directory.CreateDirectory(Dir);
        var preview = new PreviewRenderUtility { cameraFieldOfView = 34f };
        var made = new List<GameObject>();
        GameObject Put(string path, Vector3 at, float yaw, float scale = 1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[Event art] missing " + path); return null; }
            var go = Object.Instantiate(prefab);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale *= scale;
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) mb.enabled = false;
            preview.AddSingleGO(go);
            made.Add(go);
            return go;
        }
        try
        {
            // Ground: a dark grassy plain falling away into the dusk.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(20, 1, 20);
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.17f, .2f, .11f) };
            groundMat.SetFloat("_Smoothness", 0f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;
            preview.AddSingleGO(ground); made.Add(ground);
            var road = GameObject.CreatePrimitive(PrimitiveType.Plane);
            road.transform.SetPositionAndRotation(new Vector3(0, .01f, 2f), Quaternion.Euler(0, 12, 0));
            road.transform.localScale = new Vector3(.45f, 1, 6f);
            var roadMat = new Material(groundMat) { color = new Color(.33f, .25f, .16f) };
            road.GetComponent<Renderer>().sharedMaterial = roadMat;
            preview.AddSingleGO(road); made.Add(road);

            // The merchant by his cart, a fire between him and the viewer, pines behind.
            var merchant = Put("Assets/Game/Characters/NPCs/Dungeon merchant.prefab", new Vector3(-.9f, 0, 1.2f), 150f);
            if (merchant != null)
            {
                // Standing at ease: the shared idle clip sampled onto the rig (an Animator doesn't run in a preview).
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Characters/Animations/ThirdPerson/Locomotion/Idle.anim");
                var animator = merchant.GetComponentInChildren<Animator>();
                if (idle != null && animator != null) idle.SampleAnimation(animator.gameObject, .6f);
                // The clip sets the rig's own turn and place: face him toward the viewer, a little to the fire.
                if (animator != null && animator.transform != merchant.transform) { animator.transform.localPosition = Vector3.zero; animator.transform.localRotation = Quaternion.identity; }
                merchant.transform.SetPositionAndRotation(new Vector3(-.9f, 0, 1.2f), Quaternion.Euler(0, 140f, 0));
            }
            Put("Assets/Synty/PolygonFantasyKingdom/Prefabs/Vehicles/SM_Veh_Cart_02.prefab", new Vector3(-3.2f, 0, 3.4f), 70f);
            Put("Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Fire_Pit_01.prefab", new Vector3(1.4f, 0, .2f), 20f, .4f);
            // Embers in the pit: small glowing blocks (a particle fire doesn't draw in a still).
            var emberMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(1f, .5f, .15f) };
            for (int i = 0; i < 5; i++)
            {
                var ember = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ember.transform.position = new Vector3(1.4f + Mathf.Cos(i * 1.3f) * .14f, .06f + i * .025f, .2f + Mathf.Sin(i * 1.3f) * .14f);
                ember.transform.rotation = Quaternion.Euler(i * 30, i * 50, 20);
                ember.transform.localScale = Vector3.one * (.11f - i * .015f);
                ember.GetComponent<Renderer>().sharedMaterial = emberMat;
                preview.AddSingleGO(ember); made.Add(ember);
            }
            Put("Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Sack_01.prefab", new Vector3(-2.1f, 0, 1.7f), 30f);
            Put("Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Sack_02.prefab", new Vector3(-1.9f, 0, 2.3f), 80f);
            Put("Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Lantern_01.prefab", new Vector3(-2.4f, 1.15f, 3.0f), 0f);
            float[] px = { -9, -6, -2.5f, 3, 6.5f, 10, -12, 13, -4, 8, -16, -20, 18, 22, -10, 0, 14, -26, 27, 5 };
            float[] pz = { 10, 13, 15, 14, 11, 9, 7, 6, 20, 19, 22, 14, 21, 13, 27, 26, 28, 24, 25, 33 };
            for (int i = 0; i < px.Length; i++)
                Put($"Assets/Synty/PolygonTown/Prefabs/Environment/SM_Env_Tree_Pine_0{i % 2 + 1}.prefab", new Vector3(px[i], 0, pz[i]), i * 47f, .9f + (i % 3) * .15f);

            // Dusk: a low orange sun from behind and the fire's warm light on the merchant's front.
            var sun = preview.lights[0];
            sun.type = LightType.Directional; sun.color = new Color(1f, .62f, .36f); sun.intensity = 1.3f;
            sun.transform.rotation = Quaternion.Euler(14, 200, 0);
            sun.shadows = LightShadows.Soft;
            var fill = preview.lights[1];
            fill.type = LightType.Directional; fill.color = new Color(.45f, .5f, .75f); fill.intensity = .45f;
            fill.transform.rotation = Quaternion.Euler(40, 20, 0);
            var fireGo = new GameObject("Fire light");
            var fire = fireGo.AddComponent<Light>();
            fire.type = LightType.Point; fire.color = new Color(1f, .55f, .25f); fire.intensity = 9f; fire.range = 8f;
            fireGo.transform.position = new Vector3(1.4f, .9f, .2f);
            preview.AddSingleGO(fireGo); made.Add(fireGo);
            preview.ambientColor = new Color(.32f, .26f, .3f);

            var cam = preview.camera;
            cam.transform.position = new Vector3(1.6f, 1.45f, -5.2f);
            cam.transform.LookAt(new Vector3(-.6f, 1.05f, 1.6f));
            cam.nearClipPlane = .1f; cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.86f, .55f, .38f);

            preview.BeginStaticPreview(new Rect(0, 0, width, height));
            preview.Render(true);
            var tex = preview.EndStaticPreview();
            var path = Dir + "/Wandering merchant.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(groundMat); Object.DestroyImmediate(roadMat); Object.DestroyImmediate(emberMat);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
            return path;
        }
        finally
        {
            foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
            preview.Cleanup();
        }
    }
}
