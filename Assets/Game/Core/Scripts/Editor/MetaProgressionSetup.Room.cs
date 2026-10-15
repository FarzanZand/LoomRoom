using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The room's side of the meta progression: the south wall right of the table is cleared (the campaign library, the
// flamingo, the framed picture, the reading lamp and the library's collision box are switched off, not deleted), the
// trophy wall goes up there (a plaque per skill milestone, a brass name plate per skill) and the hobby table stands in
// front of it with your character sheet, books, paints and the class miniatures.
public static partial class MetaProgressionSetup
{
    const string Furnishings = "World/Apartment/Main Bedroom/Furnishings";
    const string ApartmentArt = "Assets/Game/Art/Synty Pixel/Apartment/";
    const string RoomArt = "Assets/Game/Art/Synty Pixel/Room/";
    // The south wall's face (z) and the floor (y), measured in the room; the wall runs between the two plants.
    const float WallZ = 238.53f, FloorY = .5f, WallCentreX = -372.6f;

    [MenuItem("Tools/LoomRoom/Meta/Build Trophy Wall and Hobby Table", priority = 314)]
    public static void BuildRoom()
    {
        var furnishings = GameObject.Find(Furnishings);
        if (furnishings == null) throw new System.Exception("No " + Furnishings + " in the open scene.");
        ClearWall(furnishings.transform);
        BuildWall(furnishings.transform);
        BuildHobbyTable(furnishings.transform);
        EditorSceneManager.MarkSceneDirty(furnishings.scene);
        Debug.Log("Meta: trophy wall and hobby table built");
    }

    static void ClearWall(Transform furnishings)
    {
        // The open book that lay on the bookcase's lowest shelf.
        foreach (Transform child in furnishings)
            if (child.name == "SM_Prop_BookOpen_01" && Mathf.Abs(child.position.z - WallZ) < 15f && child.gameObject.activeSelf)
            { Undo.RecordObject(child.gameObject, "Clear the south wall"); child.gameObject.SetActive(false); }
        var corner = furnishings.Find("Reading Corner and Lighting");
        if (corner == null) return;
        foreach (Transform child in corner)
        {
            bool clear = child.name == "Campaign library" || child.name == "w" || child.name == "Hobby reading lamp (1)"
                || child.name == "Furniture collision" && Mathf.Abs(child.position.z - 250f) < 6f;
            if (!clear || !child.gameObject.activeSelf) continue;
            Undo.RecordObject(child.gameObject, "Clear the south wall");
            child.gameObject.SetActive(false);
        }
    }

    static Material Mat(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);

    static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos; go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go;
    }

    static GameObject MeshObject(string name, Transform parent, string meshPath, string matPath, Vector3 localPos, Vector3 euler, float scale)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath); var mat = Mat(matPath);
        if (mesh == null || mat == null) { Debug.LogWarning("Meta: missing " + meshPath); return null; }
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos; go.transform.localEulerAngles = euler; go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static void BuildWall(Transform furnishings)
    {
        var old = furnishings.Find("Trophy wall");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var root = new GameObject("Trophy wall");
        Undo.RegisterCreatedObjectUndo(root, "Trophy wall");
        root.transform.SetParent(furnishings, false);
        root.transform.position = new Vector3(WallCentreX, FloorY, WallZ);
        var wall = root.AddComponent<TrophyWall>();
        wall.outlineMaterial = OutlineMaterial();
        var wood = PlaqueMaterial();
        var brass = Mat(ApartmentArt + "Decor - Brass.mat");
        AdventureSkill[] order = { AdventureSkill.Blocking, AdventureSkill.Swords, AdventureSkill.Maces, AdventureSkill.Sorcery, AdventureSkill.Thaumaturgy, AdventureSkill.Athletics, AdventureSkill.Stealth, AdventureSkill.Trading };
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        for (int c = 0; c < order.Length; c++)
        {
            // Seen from the room the wall's +x is on the left: Blocking first, reading left to right.
            float x = 56f - c * 16f;
            string id = order[c].ToString().ToLowerInvariant();
            int first = order[c] == AdventureSkill.Blocking ? 15 : 25;
            int[] values = { first, 50 };
            for (int r = 0; r < 2; r++)
            {
                float y = 30f + r * 15f;
                var mount = new GameObject($"{order[c]} {values[r]}");
                mount.transform.SetParent(root.transform, false);
                mount.transform.localPosition = new Vector3(x, y, 0);
                Box("Plaque", mount.transform, new Vector3(0, 0, .35f), new Vector3(12f, 12f, .7f), wood);
                Box("Peg", mount.transform, new Vector3(0, 4.2f, 1f), new Vector3(.8f, .8f, .7f), brass);
                var anchor = new GameObject("Anchor").transform;
                anchor.SetParent(mount.transform, false);
                anchor.localPosition = new Vector3(0, 0, 2.6f);
                // The anchor faces out of the wall (+z), so a trophy's front shows to the room.
                anchor.localRotation = Quaternion.Euler(0, 180, 0);
                wall.mounts.Add(new TrophyWall.Mount { trackId = id, value = values[r], anchor = anchor });
            }
            // The skill's brass name plate under its column.
            var plate = Box($"{order[c]} name plate", root.transform, new Vector3(x, 21.6f, .3f), new Vector3(13f, 3f, .6f), brass);
            if (font != null)
            {
                var label = new GameObject("Name", typeof(RectTransform));
                label.transform.SetParent(root.transform, false);
                label.transform.localPosition = new Vector3(x, 21.6f, .65f);
                label.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var t = label.AddComponent<TextMeshPro>();
                t.text = order[c].ToString().ToUpperInvariant(); t.font = font; t.fontSize = 15; t.color = new Color(.96f, .86f, .62f);
                t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap;
                ((RectTransform)label.transform).sizeDelta = new Vector2(13, 3f);
                label.name = $"{order[c]} name";
            }
        }
        // A warm spot from the room onto the wall (the reading lamp that lit this corner is gone with the bookcase); a spot,
        // so the ceiling above it doesn't get a hot patch.
        var lamp = new GameObject("Trophy wall light").AddComponent<Light>();
        lamp.transform.SetParent(root.transform, false);
        lamp.transform.localPosition = new Vector3(0, 44f, 46f);
        lamp.transform.LookAt(root.transform.TransformPoint(new Vector3(0, 36f, 0)));
        lamp.type = LightType.Spot; lamp.spotAngle = 78f; lamp.innerSpotAngle = 40f; lamp.range = 120f; lamp.intensity = 900f;
        lamp.color = new Color(1f, .72f, .43f); lamp.shadows = LightShadows.Soft;
        wall.trophySize = 9.5f;
        // Using the wall: a trigger box over the plaques.
        var interaction = new GameObject("Interaction");
        interaction.transform.SetParent(root.transform, false);
        interaction.transform.localPosition = new Vector3(0, 36f, 3f);
        var box = interaction.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(135f, 36f, 6f);
        interaction.AddComponent<InteractableTrigger>();
        EditorUtility.SetDirty(wall);
    }

    // The plaques: plain stained wood.
    static Material PlaqueMaterial()
    {
        const string path = "Assets/Game/Progression/Room/Trophy plaque.mat";
        var mat = Mat(path);
        if (mat != null) return mat;
        // Smooth stained wood: the trim's plain grain in a warm brown, at about the room's texel size.
        var trim = Mat(ApartmentArt + "Architectural trim.mat");
        mat = new Material(trim) { name = "Trophy plaque" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(.72f, .53f, .36f));
        if (mat.HasProperty("_BaseMap")) mat.SetTextureScale("_BaseMap", new Vector2(.34f, .34f));
        mat.mainTextureScale = new Vector2(.34f, .34f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Material OutlineMaterial()
    {
        const string path = "Assets/Game/Progression/Room/Trophy outline.mat";
        var mat = Mat(path);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetColor("_BaseColor", new Color(.12f, .09f, .07f, .38f));
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void BuildHobbyTable(Transform furnishings)
    {
        var old = furnishings.Find("Hobby table");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var root = new GameObject("Hobby table");
        Undo.RegisterCreatedObjectUndo(root, "Hobby table");
        root.transform.SetParent(furnishings, false);
        // Against the wall, under the trophies; the desk's front faces the room.
        root.transform.position = new Vector3(WallCentreX, FloorY, WallZ + 10.5f);
        var table = root.AddComponent<CharacterSheetTable>();
        var desk = MeshObject("Desk", root.transform, RoomArt + "SM_Prop_Desk_01/SM_Prop_Desk_01.asset", RoomArt + "SM_Prop_Desk_01/SM_Prop_Desk_01.mat", Vector3.zero, new Vector3(0, 180, 0), 20f);
        float top = 0.877f * 20f;   // the desk mesh's height, scaled
        var collision = root.AddComponent<BoxCollider>();
        collision.center = new Vector3(0, top / 2, 0); collision.size = new Vector3(44f, top, 18f);
        // Books at the left end.
        MeshObject("Books", root.transform, RoomArt + "SM_Prop_Book_Group_01/SM_Prop_Book_Group_01 painted.asset", RoomArt + "SM_Prop_Book_Group_01/SM_Prop_Book_Group_01 painted.mat", new Vector3(-15f, top, -2f), new Vector3(0, 175, 0), 14f);
        // The sheet in the middle.
        var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
        sheet.name = "Character sheet";
        Object.DestroyImmediate(sheet.GetComponent<Collider>());
        sheet.transform.SetParent(root.transform, false);
        sheet.transform.localPosition = new Vector3(-1f, top + .06f, 1.5f);
        sheet.transform.localEulerAngles = new Vector3(90, 0, -6);
        sheet.transform.localScale = new Vector3(13.5f, 9f, 1f);
        sheet.GetComponent<MeshRenderer>().sharedMaterial = SheetMaterial();
        var trigger = sheet.AddComponent<BoxCollider>(); trigger.isTrigger = true; trigger.size = new Vector3(1.3f, 1.3f, 4f);
        sheet.AddComponent<InteractableTrigger>();
        // Paint pots and a cup of brushes to the right.
        string[] paints = { "Decor - Terracotta", "Decor - Forest", "Decor - Ink", "Decor - Brass" };
        for (int i = 0; i < paints.Length; i++)
        {
            var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pot.name = "Paint pot " + (i + 1);
            Object.DestroyImmediate(pot.GetComponent<Collider>());
            pot.transform.SetParent(root.transform, false);
            pot.transform.localPosition = new Vector3(8.2f + i * 1.9f, top + .55f, -3.2f + (i % 2) * .8f);
            pot.transform.localScale = new Vector3(1.5f, .55f, 1.5f);
            pot.GetComponent<MeshRenderer>().sharedMaterial = Mat(ApartmentArt + paints[i] + ".mat");
        }
        var cup = MeshObject("Brush cup", root.transform, RoomArt + "SM_Item_Cup_01/SM_Item_Cup_01.asset", RoomArt + "SM_Item_Cup_01/SM_Item_Cup_01.mat", new Vector3(17f, top, -4.5f), Vector3.zero, 20f);
        for (int i = 0; i < 3; i++)
        {
            var brush = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            brush.name = "Brush " + (i + 1);
            Object.DestroyImmediate(brush.GetComponent<Collider>());
            brush.transform.SetParent(root.transform, false);
            brush.transform.localPosition = new Vector3(16.6f + i * .45f, top + 3.4f, -4.4f + (i - 1) * .3f);
            brush.transform.localEulerAngles = new Vector3((i - 1) * 9f, 0, (i - 1) * -11f);
            brush.transform.localScale = new Vector3(.22f, 2.6f, .22f);
            brush.GetComponent<MeshRenderer>().sharedMaterial = Mat(ApartmentArt + (i == 1 ? "Decor - Ink.mat" : "Decor - Walnut.mat"));
        }
        // The class miniatures, half painted.
        string[] minis = { "Warrior", "Wizard", "Cleric" };
        for (int i = 0; i < minis.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Progression/Room/Miniatures/{minis[i]} Miniature.prefab");
            if (prefab == null) continue;
            var mini = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            mini.name = minis[i] + " miniature";
            mini.transform.localPosition = new Vector3(9f + i * 3.6f, top, 2.4f + (i % 2) * 1.4f);
            mini.transform.localEulerAngles = new Vector3(0, 180 + (i - 1) * 25, 0);
            mini.transform.localScale = Vector3.one * 2.2f;
        }
        // A soft light over the sheet while the DM waits for you to look at it.
        var hint = new GameObject("Sheet hint light").AddComponent<Light>();
        hint.transform.SetParent(root.transform, false);
        hint.transform.localPosition = new Vector3(-1f, top + 8f, 1.5f);
        hint.type = LightType.Point; hint.range = 26f; hint.intensity = 140f; hint.color = new Color(1f, .86f, .62f); hint.shadows = LightShadows.None; hint.enabled = false;
        table.hint = hint;
        // Everything renders like the room around it.
        var reference = furnishings.GetComponentInChildren<MeshRenderer>(true);
        if (reference != null)
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = reference.renderingLayerMask;
        EditorUtility.SetDirty(table);
    }

    static Material SheetMaterial()
    {
        const string path = "Assets/Game/Progression/Room/Character sheet on table.mat";
        var mat = Mat(path);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(MetaSprites + "Sheet on table.png");
        var importer = AssetImporter.GetAtPath(MetaSprites + "Sheet on table.png") as TextureImporter;
        if (importer != null && (importer.filterMode != FilterMode.Point || importer.textureType != TextureImporterType.Default))
        {
            importer.textureType = TextureImporterType.Default; importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(MetaSprites + "Sheet on table.png");
        }
        if (mat == null)
        {
            var linen = Mat(ApartmentArt + "Decor - Linen.mat");
            mat = linen != null ? new Material(linen) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = "Character sheet on table";
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.mainTexture = tex;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_BaseMap")) mat.SetTextureScale("_BaseMap", Vector2.one);
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
