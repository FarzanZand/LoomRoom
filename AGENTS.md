# Project instructions

## Unity API compatibility

- Do not introduce deprecated Unity APIs in runtime scripts, editor tools, or temporary setup/validation scripts. Use APIs supported by the project's installed Unity version.
- Never use `Object.FindFirstObjectByType<T>()` or its `FindObjectsInactive` overload. Use `Object.FindAnyObjectByType<T>()` or `Object.FindAnyObjectByType<T>(FindObjectsInactive.Include)` when any matching instance is sufficient.
- Prefer explicit serialized references or existing ownership/singleton references when a specific instance is required. Do not rely on instance ID ordering to select an object.
- For multiple objects, use the supported `Object.FindObjectsByType<T>()` or `Object.FindObjectsByType<T>(FindObjectsInactive.Include)` overloads. Do not use deprecated overloads taking `FindObjectsSortMode`. If ordering is required, sort explicitly by a meaningful property.
- Fix obsolete API usage in code you add or modify; do not suppress CS0618 warnings to hide it.
