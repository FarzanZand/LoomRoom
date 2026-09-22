# Project instructions

## Designer-friendly organization and editing

- Build new features so designers can easily find and edit their appearance, content, and tuning in the Unity Inspector without changing code.
- Keep reusable UI elements in `Assets/Game/UI/Prefabs/`, with clearly named subfolders when helpful. Item slots, enemy health bars, damage numbers, and similar repeated elements should be editable prefabs with serialized references. Do not construct their visual hierarchy, sizes, fonts, colors, or artwork entirely in code.
- Full menus and screens, such as inventory and character stats, may remain directly in the scene. Use reusable UI prefabs within them where appropriate; do not require every screen to become a prefab.
- Expose art, dimensions, layout, animation settings, and gameplay tuning through appropriate prefabs, serialized fields, or ScriptableObject assets. Scripts should bind data and handle behavior while preserving designer-authored styling.
- Group feature-specific content in clearly named feature or level folders, and keep shared reusable assets in an obvious shared location. Preserve Unity `.meta` files and references when moving existing assets.
- Use clear asset names, Inspector headers, and tooltips where useful. For new systems, briefly document the main assets designers should edit and what each controls.
- Setup and generation tools must preserve designer edits. Do not silently regenerate or overwrite authored prefabs, styles, or tuning during normal imports or gameplay.
- Apply these conventions to future additions and to existing systems when they are refactored; avoid unrelated mass reorganizations.

## Unity API compatibility

- Do not introduce deprecated Unity APIs in runtime scripts, editor tools, or temporary setup/validation scripts. Use APIs supported by the project's installed Unity version.
- Never use `Object.FindFirstObjectByType<T>()` or its `FindObjectsInactive` overload. Use `Object.FindAnyObjectByType<T>()` or `Object.FindAnyObjectByType<T>(FindObjectsInactive.Include)` when any matching instance is sufficient.
- Prefer explicit serialized references or existing ownership/singleton references when a specific instance is required. Do not rely on instance ID ordering to select an object.
- For multiple objects, use the supported `Object.FindObjectsByType<T>()` or `Object.FindObjectsByType<T>(FindObjectsInactive.Include)` overloads. Do not use deprecated overloads taking `FindObjectsSortMode`. If ordering is required, sort explicitly by a meaningful property.
- Fix obsolete API usage in code you add or modify; do not suppress CS0618 warnings to hide it.

## Editor menus

- Put new project menu commands under `Tools/LoomRoom/` (or an existing feature submenu under Tools). Do not add new top-level menu groups.
