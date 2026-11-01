using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Where a portrait dialogue's speaker is filmed (PortraitDialogueUI): far under the world, a copy of the speaker's model
// (only its Animator and meshes, so it idles and nothing else of it runs) stands in its own key, fill and rim light,
// and a small camera frames its head and shoulders into a low-resolution texture with a clear background, drawn
// point-filtered so it keeps the game's pixels. One copy per speaker, kept while the dialogue lasts.
public class PortraitStage : MonoBehaviour
{
    [Tooltip("Pixels across the portrait texture (shown larger, point-filtered).")] public int resolution = 256;
    [Tooltip("The camera's field of view.")] public float fieldOfView = 24f;
    [Tooltip("Degrees the model is turned from facing the camera (a three-quarter view).")] public float turn = 22f;
    [Tooltip("How much of the body shows below the head, in head-to-chest lengths.")] public float bodyBelow = 1.9f;
    [Tooltip("Room above the head, in head-to-chest lengths.")] public float roomAbove = .55f;
    public Color keyColor = new(1f, .93f, .82f), rimColor = new(.55f, .7f, 1f);

    static readonly Vector3 Where = new(0f, -3000f, 0f);

    Camera cam;
    RenderTexture texture;
    Light key, fill, rim;
    Transform holder;
    readonly Dictionary<GameObject, GameObject> copies = new();
    GameObject shown;

    public Texture Texture => texture;

    // Films this speaker (the object holding its Animator, or its root). Null hides the portrait.
    public void Show(GameObject source)
    {
        Build();
        if (shown != null) shown.SetActive(false);
        shown = null;
        if (source == null) { cam.enabled = false; return; }
        if (!copies.TryGetValue(source, out var copy) || copy == null) copies[source] = copy = Copy(source);
        if (copy == null) { cam.enabled = false; return; }
        shown = copy;
        copy.SetActive(true);
        var animator = copy.GetComponent<Animator>();
        if (animator != null) animator.Update(0f);
        Frame(copy);
        cam.enabled = true;
    }

    public void Clear()
    {
        foreach (var c in copies.Values) if (c != null) Destroy(c);
        copies.Clear();
        shown = null;
        if (cam != null) cam.enabled = false;
    }

    void OnDestroy()
    {
        Clear();
        if (texture != null) { texture.Release(); Destroy(texture); }
        if (holder != null) Destroy(holder.gameObject);
    }

    void Build()
    {
        if (cam != null) return;
        holder = new GameObject("Portrait stage").transform;
        holder.position = Where;
        texture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, name = "Portrait" };
        var camObject = new GameObject("Portrait camera");
        camObject.transform.SetParent(holder, false);
        cam = camObject.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.fieldOfView = fieldOfView;
        cam.targetTexture = texture;
        cam.enabled = false;
        var data = camObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        data.renderShadows = false;
        key = MakeLight("Key", LightType.Spot, keyColor, 3f);
        fill = MakeLight("Fill", LightType.Point, keyColor, .8f);
        rim = MakeLight("Rim", LightType.Spot, rimColor, 2.5f);
    }

    Light MakeLight(string name, LightType type, Color color, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(holder, false);
        var light = go.AddComponent<Light>();
        light.type = type; light.color = color; light.intensity = intensity;
        light.shadows = LightShadows.None;
        if (type == LightType.Spot) light.spotAngle = 60f;
        return light;
    }

    // Only what shows is copied: the Animator's object, its bones and meshes. Copied under an inactive parent so none
    // of its scripts wake, then everything else is taken off.
    GameObject Copy(GameObject source)
    {
        var animator = source.GetComponentInChildren<Animator>(true);
        var from = animator != null ? animator.gameObject : source;
        var waiting = new GameObject("Portrait copy (waiting)");
        waiting.SetActive(false);
        waiting.transform.SetParent(holder, false);
        var copy = Instantiate(from, waiting.transform);
        copy.transform.localScale = from.transform.lossyScale;
        Strip(copy);
        copy.transform.SetParent(holder, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.SetActive(false);
        Destroy(waiting);
        var anim = copy.GetComponent<Animator>();
        if (anim != null) { anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
        foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }
        return copy;
    }

    static void Strip(GameObject copy)
    {
        foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
        foreach (var light in copy.GetComponentsInChildren<Light>(true)) light.enabled = false;
        foreach (var line in copy.GetComponentsInChildren<LineRenderer>(true)) line.enabled = false;
        foreach (var trail in copy.GetComponentsInChildren<TrailRenderer>(true)) trail.enabled = false;
        foreach (var joint in copy.GetComponentsInChildren<Joint>(true)) DestroyImmediate(joint);
        foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(behaviour);
        foreach (var behaviour in copy.GetComponentsInChildren<Behaviour>(true)) if (!(behaviour is Animator)) behaviour.enabled = false;
        foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
        foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(body);
        foreach (var audio in copy.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(audio);
    }

    // The camera faces the head and shoulders, the model turned a little; lights placed round it, all scaled to it.
    void Frame(GameObject copy)
    {
        copy.transform.localRotation = Quaternion.Euler(0f, 180f - turn, 0f);   // toward the camera, which looks along +z
        var animator = copy.GetComponent<Animator>();
        Vector3 head, chest;
        if (animator != null && animator.isHuman && animator.GetBoneTransform(HumanBodyBones.Head) != null)
        {
            head = animator.GetBoneTransform(HumanBodyBones.Head).position;
            var c = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = c != null ? c.position : head - Vector3.up * .4f;
        }
        else
        {
            var b = new Bounds(copy.transform.position, Vector3.zero);
            foreach (var r in copy.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            head = new Vector3(b.center.x, b.max.y - b.size.y * .1f, b.center.z);
            chest = head - Vector3.up * b.size.y * .2f;
        }
        float unit = Mathf.Max(.05f, head.y - chest.y);
        // Always the same size (Room Above plus Body Below, in head-to-chest lengths); only where it sits moves: its top
        // clears the model's own top over the head (horns, a helmet, a hat), capped so a raised weapon can't push it
        // up off the face, and less of the body shows below.
        float span = unit * (roomAbove + bodyBelow);
        float crown = head.y;
        foreach (var r in copy.GetComponentsInChildren<Renderer>())
        {
            var b = r.bounds;
            if (Mathf.Abs(b.center.x - head.x) < unit * 2f && Mathf.Abs(b.center.z - head.z) < unit * 2f) crown = Mathf.Max(crown, b.max.y);
        }
        crown = Mathf.Min(crown, head.y + unit * 1.05f);
        float top = Mathf.Max(head.y + unit * roomAbove, crown + unit * .12f), bottom = top - span;
        var centre = new Vector3(head.x, (top + bottom) * .5f, head.z);
        float distance = span * .5f / Mathf.Tan(fieldOfView * .5f * Mathf.Deg2Rad);
        cam.transform.position = centre - Vector3.forward * distance;
        cam.transform.rotation = Quaternion.identity;
        cam.nearClipPlane = Mathf.Max(.01f, distance * .2f);
        cam.farClipPlane = distance * 3f;
        float range = distance * 3f;
        key.transform.position = centre + new Vector3(-distance * .6f, unit * 1.2f, -distance * .8f);
        key.transform.LookAt(head);
        key.range = range;
        fill.transform.position = centre + new Vector3(distance * .7f, 0f, -distance * .6f);
        fill.range = range;
        rim.transform.position = centre + new Vector3(distance * .5f, unit * 1.5f, distance * .9f);
        rim.transform.LookAt(head);
        rim.range = range;
        foreach (var l in new[] { key, fill, rim }) l.intensity = l == fill ? .8f : l == key ? 3f : 2.5f;
        // Lights are set in units of the model's size: a twenty-times figure (the Dungeon Master) needs far more.
        float scale = Mathf.Max(1f, distance * distance);
        foreach (var l in new[] { key, fill, rim }) l.intensity *= scale;
    }
}
