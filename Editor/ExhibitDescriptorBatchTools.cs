#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 대량 작업용 Editor 도구.
///
/// Tools > Exhibit Descriptor
///   - Create Exhibits From Selected Meshes : 이미 배치해 둔 작품 Mesh 를 골라 Exhibit 으로 일괄 변환
///   - Auto Setup On Save                   : Scene 저장 시 Setup 을 자동 실행 (토글)
///
/// <see cref="ExhibitDescriptorTools"/> 의 partial 이므로 그쪽 private 헬퍼를 그대로 씁니다.
/// </summary>
public static partial class ExhibitDescriptorTools
{
    // =====================================================================
    // 5. 선택한 Mesh 로 작품 일괄 생성
    // =====================================================================

    /// <summary>InteractionArea BoxCollider 를 작품 Bounds 보다 얼마나 키울지 (m, 한쪽 기준).</summary>
    private const float InteractionPadding = 0.15f;

    /// <summary>작품 옆면과 Overlay Panel 사이의 여백 (m).</summary>
    private const float OverlayGap = 0.15f;

    [MenuItem(MenuRoot + "Create Exhibits From Selected Meshes", false, 12)]
    public static void CreateExhibitsFromSelectedMeshes()
    {
        GameObject[] selection = Selection.gameObjects;
        if (selection == null || selection.Length == 0)
        {
            Debug.LogWarning("[ExhibitDescriptor] 선택된 오브젝트가 없습니다. " +
                             "Hierarchy 에서 작품 Mesh 들을 선택한 뒤 다시 실행하세요.");
            return;
        }

        List<GameObject> sources = CollectMeshSources(selection);
        if (sources.Count == 0)
        {
            Debug.LogWarning("[ExhibitDescriptor] 작품으로 만들 수 있는 오브젝트가 없습니다. " +
                             "(Renderer 가 있는 Scene 오브젝트만 대상입니다)");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();

        // 이름 번호는 "만들기 시작하는 시점" 기준으로 한 번만 계산하고 순차 증가시킵니다.
        // 매번 Scene 을 다시 훑으면 작품 100개일 때 O(n^2) 이 됩니다.
        Dictionary<Scene, int> nextIndex = new Dictionary<Scene, int>();

        List<GameObject> created = new List<GameObject>();

        for (int i = 0; i < sources.Count; i++)
        {
            GameObject exhibit = CreateExhibitFromMesh(sources[i], nextIndex);
            if (exhibit != null) created.Add(exhibit);
        }

        Undo.SetCurrentGroupName("Create Exhibits From Selected Meshes");
        Undo.CollapseUndoOperations(undoGroup);

        if (created.Count > 0) Selection.objects = created.ToArray();

        Debug.Log("[ExhibitDescriptor] 작품 " + created.Count + " 개를 생성했습니다. " +
                  "Title 은 원본 오브젝트 이름으로 채워 두었으니 Inspector 에서 Description 만 입력하세요.");
    }

    /// <summary>
    /// 선택 목록에서 작품으로 만들 오브젝트만 골라냅니다.
    ///
    /// 제외 대상
    ///  - Prefab Asset 등 Scene 밖의 오브젝트 (부모/Scene 을 특정할 수 없음)
    ///  - Renderer 가 없는 오브젝트 (빈 오브젝트, Manager 등)
    ///  - 이미 어떤 Exhibit 안에 들어 있는 오브젝트 (이중 변환 방지)
    ///  - 선택 목록 안의 다른 오브젝트의 자식 (부모만 한 번 처리하면 됩니다)
    /// </summary>
    private static List<GameObject> CollectMeshSources(GameObject[] selection)
    {
        List<GameObject> result = new List<GameObject>();

        for (int i = 0; i < selection.Length; i++)
        {
            GameObject candidate = selection[i];
            if (candidate == null) continue;

            if (!candidate.scene.IsValid())
            {
                Debug.LogWarning("[ExhibitDescriptor] Scene 오브젝트가 아니라 건너뜁니다(Project 창의 Prefab 은 대상이 아닙니다): " +
                                 candidate.name, candidate);
                continue;
            }

            if (candidate.GetComponentInChildren<Renderer>(true) == null)
            {
                Debug.LogWarning("[ExhibitDescriptor] Renderer 가 없어 건너뜁니다: " + GetPath(candidate.transform), candidate);
                continue;
            }

            if (candidate.GetComponentInParent<ExhibitInteractable>() != null ||
                candidate.GetComponentInChildren<ExhibitInteractable>(true) != null)
            {
                Debug.LogWarning("[ExhibitDescriptor] 이미 Exhibit 에 속해 있어 건너뜁니다: " + GetPath(candidate.transform), candidate);
                continue;
            }

            if (HasSelectedAncestor(candidate, selection)) continue;

            result.Add(candidate);
        }

        return result;
    }

    private static bool HasSelectedAncestor(GameObject candidate, GameObject[] selection)
    {
        Transform current = candidate.transform.parent;
        while (current != null)
        {
            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] != null && selection[i].transform == current) return true;
            }
            current = current.parent;
        }
        return false;
    }

    /// <summary>Mesh 오브젝트 1개를 Exhibit 안으로 감싸고 크기/위치를 자동 계산합니다.</summary>
    private static GameObject CreateExhibitFromMesh(GameObject source, Dictionary<Scene, int> nextIndex)
    {
        Scene scene = source.scene;
        Transform originalParent = source.transform.parent;
        int siblingIndex = source.transform.GetSiblingIndex();

        string name = "Exhibit_" + NextExhibitNumber(scene, nextIndex).ToString("D3");

        // 템플릿을 그대로 만들고, 투명 Placeholder 만 실제 작품으로 교체합니다.
        GameObject exhibit = BuildExhibit(name);

        Transform placeholder = exhibit.transform.Find("Artwork");
        if (placeholder != null) Object.DestroyImmediate(placeholder.gameObject);

        if (scene.IsValid() && exhibit.scene != scene) SceneManager.MoveGameObjectToScene(exhibit, scene);

        // Exhibit Root 는 원본과 같은 위치/회전, **월드 Scale 은 1** 로 둡니다.
        // localScale 을 1 로 두는 것만으로는 부족합니다. 부모가 스케일돼 있으면 그 Scale 이
        // 그대로 상속돼 World Space Canvas(0.001) 와 InteractionArea 까지 함께 찌그러집니다.
        exhibit.transform.SetParent(originalParent, false);
        exhibit.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        NeutralizeWorldScale(exhibit.transform);
        exhibit.transform.SetSiblingIndex(siblingIndex);

        Undo.RegisterCreatedObjectUndo(exhibit, "Create Exhibit From Mesh");

        // 원본은 World 좌표를 유지한 채 Exhibit 아래로 들어갑니다. (이름은 바꾸지 않습니다)
        Undo.SetTransformParent(source.transform, exhibit.transform, "Move Artwork Into Exhibit");

        WarnAboutArtworkColliders(source);

        FitExhibitToBounds(exhibit, source);

        ExhibitInteractable interactable = exhibit.GetComponent<ExhibitInteractable>();
        SetArtworkTitle(interactable, source.name);

        // 참조 연결과 Interact 값 굽기는 최종 위치에 자리 잡은 뒤에 합니다.
        SetupExhibitFull(interactable);
        MarkSceneDirtyFor(exhibit);

        return exhibit;
    }

    /// <summary>Scene 안에서 아직 쓰이지 않은 <c>Exhibit_###</c> 번호를 돌려줍니다.</summary>
    private static int NextExhibitNumber(Scene scene, Dictionary<Scene, int> cache)
    {
        int next;
        if (!cache.TryGetValue(scene, out next))
        {
            next = 1;

            ExhibitInteractable[] all = Object.FindObjectsOfType<ExhibitInteractable>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].gameObject.scene != scene) continue;

                int parsed;
                if (!TryParseExhibitNumber(all[i].gameObject.name, out parsed)) continue;
                if (parsed >= next) next = parsed + 1;
            }
        }

        cache[scene] = next + 1;
        return next;
    }

    /// <summary>"Exhibit_042" → 42. 접두사가 다르거나 숫자가 아니면 false.</summary>
    private static bool TryParseExhibitNumber(string objectName, out int number)
    {
        number = 0;

        const string prefix = "Exhibit_";
        if (string.IsNullOrEmpty(objectName) || !objectName.StartsWith(prefix)) return false;

        string digits = objectName.Substring(prefix.Length);
        if (digits.Length == 0) return false;

        for (int i = 0; i < digits.Length; i++)
        {
            if (digits[i] < '0' || digits[i] > '9') return false;
        }

        return int.TryParse(digits, out number);
    }

    /// <summary>
    /// 작품 Bounds 에 맞춰 InteractionArea 의 BoxCollider 와 OverlayAnchor 위치/방향을 계산합니다.
    /// 계산은 전부 Exhibit Root 의 로컬 좌표에서 합니다. (Root 가 회전해 있어도 안전)
    ///
    /// <b>축을 하드코딩하지 않습니다.</b> 예전에는 "얇은 축 = 로컬 Z, 옆방향 = 로컬 X" 라고 전제했는데,
    /// 벽에 걸린 액자는 로컬 X 가 얇은 경우가 흔합니다(= 작품 정면 법선이 X). 그 작품에서는
    /// Panel 이 그림 정면으로 튀어나와 작품을 가리고, 관람자에게는 Panel 이 옆면(선)으로만 보였습니다.
    /// <see cref="ExhibitOverlay"/> 에는 빌보드/LookAt 이 없어 런타임에도 보정되지 않으므로
    /// 여기서 정한 위치/회전이 그대로 최종 결과입니다.
    /// </summary>
    private static void FitExhibitToBounds(GameObject exhibit, GameObject source)
    {
        Bounds bounds;
        if (!TryGetLocalBounds(exhibit.transform, source, out bounds))
        {
            Debug.LogWarning("[ExhibitDescriptor] Bounds 를 계산하지 못해 기본 크기로 둡니다: " + GetPath(source.transform), source);
            return;
        }

        int normalAxis = GetFrontNormalAxis(bounds.size);
        int pushAxis = GetOverlayPushAxis(bounds.size, normalAxis);

        // ---- InteractionArea: 작품을 덮는 판정 박스 ------------------------
        Transform area = exhibit.transform.Find("InteractionArea");
        if (area != null)
        {
            area.localPosition = bounds.center;

            BoxCollider box = area.GetComponent<BoxCollider>();
            if (box != null) box.size = GetInteractionSize(bounds.size, normalAxis);
        }

        // ---- OverlayAnchor: 작품 옆에 Panel 이 겹치지 않게 배치 ------------
        Transform anchor = exhibit.transform.Find("OverlayAnchor");
        if (anchor != null)
        {
            float panelHalfWidth = PanelWidth * CanvasScale * 0.5f;
            float panelHalfHeight = PanelHeight * CanvasScale * 0.5f;

            Vector3 position = bounds.center;
            position[pushAxis] = bounds.center[pushAxis] + bounds.extents[pushAxis] + OverlayGap + panelHalfWidth;
            // 낮은 좌대/조각은 중심에 맞추면 Panel 아래쪽이 바닥에 묻힙니다.
            position.y = Mathf.Max(bounds.center.y, bounds.min.y + panelHalfHeight);

            anchor.localPosition = position;
            anchor.localRotation = GetOverlayRotation(normalAxis, pushAxis);
        }
    }

    /// <summary>
    /// 작품의 "정면 법선축" (0 = X, 1 = Y, 2 = Z) 을 고릅니다. 로컬 Bounds 에서 <b>가장 얇은 축</b>입니다.
    ///
    /// 액자·그림·안내판은 두께가 가장 얇은 축이 곧 정면이 바라보는 축입니다.
    /// 부호(+/-)까지는 Bounds 만으로 알 수 없으므로 <b>+ 방향을 정면으로 봅니다.</b>
    /// Exhibit Root 는 원본 Mesh 의 회전을 그대로 물려받으므로, 작품이 로컬 +축을 관람자 쪽으로
    /// 두고 배치돼 있으면 그대로 맞습니다. 반대로 배치된 작품은 생성 후 OverlayAnchor 를
    /// 180도 돌려 주면 됩니다 - Setup 은 Anchor 를 다시 계산하지 않으므로 그 수정이 유지됩니다.
    ///
    /// 두께가 같으면 X → Y → Z 순으로 먼저 오는 축을 씁니다. (정육면체는 X 가 법선축)
    /// </summary>
    private static int GetFrontNormalAxis(Vector3 size)
    {
        if (size.x <= size.y && size.x <= size.z) return 0;
        if (size.y <= size.z) return 1;
        return 2;
    }

    /// <summary>
    /// Panel 을 밀어낼 축을 고릅니다. "법선축과 수직인 <b>수평</b>축 중 넓은 쪽" 입니다.
    ///
    /// 로컬 Y 는 위/아래라 후보에서 뺍니다. 그래서 실제로는
    ///  - 법선축이 X → 남는 수평축은 Z 하나,
    ///  - 법선축이 Z → X 하나로 결정됩니다. (예전 동작과 같아 회귀가 없습니다)
    ///  - 법선축이 <b>Y</b> 인 경우(바닥에 눕힌 좌대/평면 작품)에만 X 와 Z 가 모두 수평이라
    ///    선택지가 생깁니다. 이때는 <b>넓은 쪽</b>으로 밀어냅니다. 긴 변을 따라 옆으로 비켜서야
    ///    작품 위를 덜 가리기 때문입니다. 두 값이 같으면 X 입니다.
    ///
    /// "수평" 판단은 Exhibit Root 의 로컬 축 기준입니다. Root 는 원본의 회전을 물려받으므로
    /// 작품이 심하게 기울어 놓여 있으면 로컬 Y 도 그만큼 기웁니다. (똑바로 선 작품이 기준)
    /// </summary>
    private static int GetOverlayPushAxis(Vector3 size, int normalAxis)
    {
        if (normalAxis == 1) return size.x >= size.z ? 0 : 2;
        return normalAxis == 0 ? 2 : 0;
    }

    /// <summary>
    /// InteractionArea BoxCollider 의 크기입니다.
    /// 얇은 액자도 정면에서 확실히 잡히도록 <b>정면 법선축만</b> 최소 깊이를 크게(0.3m) 잡고,
    /// 나머지 두 축은 최소 0.2m 입니다. 어느 축이든 작품 Bounds + 사방 <see cref="InteractionPadding"/> 은 보장합니다.
    ///
    /// (예전에는 이 최소 깊이가 Z 에 하드코딩돼 있어, X 가 얇은 액자는 깊이 보정을 받지 못하고
    ///  엉뚱하게 폭이 부풀었습니다.)
    /// </summary>
    private static Vector3 GetInteractionSize(Vector3 size, int normalAxis)
    {
        Vector3 padded = size + Vector3.one * (InteractionPadding * 2f);

        Vector3 minimum = new Vector3(0.2f, 0.2f, 0.2f);
        minimum[normalAxis] = 0.3f;

        return new Vector3(
            Mathf.Max(padded.x, minimum.x),
            Mathf.Max(padded.y, minimum.y),
            Mathf.Max(padded.z, minimum.z));
    }

    /// <summary>
    /// OverlayAnchor 의 로컬 회전입니다. Panel 의 <b>글자가 읽히는 면</b>이 관람자를 향하도록 세웁니다.
    /// <see cref="ExhibitOverlay"/> 에는 빌보드가 없어 여기서 정한 방향이 런타임까지 그대로 갑니다.
    ///
    /// <b>부호 규약:</b> World Space Canvas 는 자기 <b>forward(+Z) 의 반대쪽</b>에 선 사람에게 글자가
    /// 정방향으로 보입니다. (회전 없는 Canvas 가 -Z 에 놓인 기본 카메라에 정방향으로 보이는 그 구도입니다.
    ///  그래서 UI 빌보드도 카메라를 <i>바라보게</i> 하지 않고 <c>canvas.forward = camera.forward</c> 로 맞춥니다.)
    /// 즉 Panel 의 forward 는 관람자에게서 <b>멀어지는</b> 쪽이어야 하고, 판정식은
    /// <c>dot(관람자 - Panel, Panel.forward) &lt; 0</c> 입니다.
    ///
    ///  - 법선축이 X / Z: 관람자는 작품 정면 쪽에 서 있으므로 forward 는 <b>작품 정면의 반대</b>입니다.
    ///  - 법선축이 <b>Y</b> (바닥에 눕힌 좌대/평면 작품): 정면이 하늘이나 바닥을 향하므로
    ///    그대로 따라가면 서 있는 관람자가 Panel 을 읽을 수 없습니다. 대신 Panel 을 밀어낸
    ///    방향(<paramref name="pushAxis"/>) <b>바깥</b>에서 읽도록 수직으로 세웁니다.
    ///    작품에서 멀어지는 쪽이라 Panel 이 작품을 가리지도 않습니다.
    ///
    /// 위쪽은 항상 로컬 +Y 입니다.
    ///
    /// (예전에는 관람자 쪽을 그대로 <c>LookRotation</c> 에 넘겨 <b>캔버스 뒷면</b>을 보여 줬습니다.
    ///  재검증 #3 실기에서 세 케이스 모두 글자가 좌우 반전됐고 dot 이 +2.5 / +2.5 / +1.45 로 양수였습니다.
    ///  각도만 보는 검증(<c>Vector3.Angle(forward, 정면) ≈ 0</c>)은 이 실수를 통과시킵니다.
    ///  <b>법선축이 Z 인 작품도 이제 identity 가 아니라 Y 180도</b>입니다 — 그 전에 만든 Exhibit 은
    ///  뒤집힌 Anchor 를 그대로 들고 있으므로 Anchor 를 180도 돌리거나 다시 생성해야 합니다.
    ///  Setup 은 Anchor 를 다시 계산하지 않습니다.)
    /// </summary>
    private static Quaternion GetOverlayRotation(int normalAxis, int pushAxis)
    {
        // 관람자가 서 있는 쪽. (법선축이 Y 면 Panel 을 밀어낸 쪽에서 읽습니다)
        Vector3 viewerSide = GetAxisVector(normalAxis == 1 ? pushAxis : normalAxis);

        // Canvas 는 forward 의 반대쪽에서 읽히므로 관람자 쪽의 반대를 봅니다.
        return Quaternion.LookRotation(-viewerSide, Vector3.up);
    }

    /// <summary>축 인덱스(0 = X, 1 = Y, 2 = Z)를 단위 벡터로 바꿉니다.</summary>
    private static Vector3 GetAxisVector(int axis)
    {
        if (axis == 0) return Vector3.right;
        if (axis == 1) return Vector3.up;
        return Vector3.forward;
    }

    /// <summary>
    /// source 의 모든 Renderer 를 감싸는 AABB 를 <paramref name="space"/> 의 로컬 좌표로 구합니다.
    ///
    /// 각 Renderer 의 **로컬(Mesh) Bounds** 8개 꼭짓점을 월드로 보낸 뒤 다시 space 로 가져옵니다.
    /// <see cref="Renderer.bounds"/>(월드 AABB)에서 출발하면 안 됩니다. 월드 AABB 는 이미 회전을
    /// 흡수해 부풀어 있는 상자라, 그 꼭짓점을 space 로 되돌리면 한 번 더 부풀어 오릅니다.
    /// (예: Y 45도로 돌아간 두께 0.05m 액자 → 깊이가 1m 넘게 잡혀 InteractionArea 가 통로를 막고
    ///  OverlayAnchor 도 작품에서 그만큼 멀어집니다.)
    /// </summary>
    private static bool TryGetLocalBounds(Transform space, GameObject source, out Bounds bounds)
    {
        bounds = new Bounds();

        Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
        bool initialized = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            if (renderer is ParticleSystemRenderer) continue; // 파티클은 Bounds 가 런타임에 변합니다.

            Bounds shape;
            Transform shapeSpace;
            if (!TryGetShapeBounds(renderer, out shape, out shapeSpace))
            {
                // 로컬 형상을 알 수 없는 Renderer(LineRenderer 등)는 예전처럼 월드 AABB 로 대신합니다.
                shape = renderer.bounds;
                shapeSpace = null;
            }

            Vector3 center = shape.center;
            Vector3 extents = shape.extents;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = center + new Vector3(
                    (corner & 1) == 0 ? -extents.x : extents.x,
                    (corner & 2) == 0 ? -extents.y : extents.y,
                    (corner & 4) == 0 ? -extents.z : extents.z);

                Vector3 world = shapeSpace == null ? point : shapeSpace.TransformPoint(point);
                Vector3 local = space.InverseTransformPoint(world);

                if (!initialized)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }
        }

        return initialized;
    }

    /// <summary>
    /// Renderer 가 그리는 Mesh 의 회전 전 Bounds 와, 그 Bounds 가 놓인 좌표계를 돌려줍니다.
    ///
    /// Mesh 의 정점은 - Skinned 든 아니든 - Renderer 의 로컬 좌표계에 있습니다.
    /// (Unity 의 bindpose 는 bone.worldToLocal * renderer.localToWorld 로 구워지므로,
    ///  sharedMesh.bounds 역시 rootBone 이 아니라 Renderer Transform 기준입니다.)
    /// rootBone 을 좌표계로 쓰면 rootBone 의 위치/회전/스케일이 Renderer 와 다를 때
    /// InteractionArea 와 OverlayAnchor 가 엉뚱한 곳에 놓입니다.
    /// 형상을 알 수 없는 Renderer 는 false 를 돌려 호출부가 월드 AABB 로 대체하게 합니다.
    /// </summary>
    private static bool TryGetShapeBounds(Renderer renderer, out Bounds shape, out Transform shapeSpace)
    {
        shape = new Bounds();
        shapeSpace = null;

        SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
        if (skinned != null)
        {
            if (skinned.sharedMesh == null) return false;

            shape = skinned.sharedMesh.bounds;
            shapeSpace = skinned.transform;
            return true;
        }

        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return false;

        shape = filter.sharedMesh.bounds;
        shapeSpace = renderer.transform;
        return true;
    }

    /// <summary>
    /// 작품 Mesh 에 남아 있는 Collider 를 경고합니다.
    ///
    /// 지우지 않고 알리기만 하는 이유: 사용자가 물리 충돌(벽처럼 막기)용으로 일부러 둔 것일 수 있습니다.
    /// 다만 Interact 레이가 작품 Collider 에 먼저 맞으면 InteractionArea 가 반응하지 않으므로
    /// 방치하면 "클릭해도 아무 일도 없음" 의 원인이 됩니다.
    /// </summary>
    private static void WarnAboutArtworkColliders(GameObject source)
    {
        Collider[] colliders = source.GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0) return;

        Debug.LogWarning("[ExhibitDescriptor] 작품 Mesh 에 Collider 가 " + colliders.Length + " 개 있습니다: " +
                         GetPath(source.transform) + "\n" +
                         "Interact 레이가 여기에 먼저 막히면 클릭이 InteractionArea 로 가지 않습니다. " +
                         "물리 충돌 용도가 아니라면 제거하세요.", source);
    }

    /// <summary>
    /// Title 을 원본 오브젝트 이름으로 채웁니다.
    ///
    /// 방금 <see cref="BuildExhibit"/> 로 만든 작품이라 titleKR 에는 템플릿 기본값
    /// ("작품 제목") 이 들어 있습니다. 비어 있는지 검사하면 절대 덮이지 않으므로 그냥 씁니다.
    ///
    /// EN / JP 는 **비웁니다.** 기본값("Artwork Title")을 남겨 두면 언어를 바꿨을 때
    /// 작품 이름 대신 그 자리표시자가 그대로 보입니다. 비워 두면 KR 로 fallback 합니다.
    /// </summary>
    private static void SetArtworkTitle(ExhibitInteractable interactable, string title)
    {
        if (interactable == null) return;

        SerializedObject so = new SerializedObject(interactable);

        SetString(so, "titleKR", title);
        SetString(so, "titleEN", "");
        SetString(so, "titleJP", "");

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(interactable);
    }

    // =====================================================================
    // 6. 저장 시 자동 Setup
    // =====================================================================

    private const string AutoSetupMenuPath = MenuRoot + "Auto Setup On Save";
    private const string AutoSetupPrefKey = "ExhibitDescriptor.AutoSetupOnSave";

    /// <summary>
    /// Scene 을 저장할 때 <c>Setup All Exhibits In Scene</c> 을 자동으로 돌릴지 여부입니다.
    /// EditorPrefs 라 프로젝트가 아니라 이 PC 의 Unity 설정에 저장됩니다.
    /// </summary>
    public static bool AutoSetupOnSave
    {
        get { return EditorPrefs.GetBool(AutoSetupPrefKey, true); }
        set { EditorPrefs.SetBool(AutoSetupPrefKey, value); }
    }

    [MenuItem(AutoSetupMenuPath, false, 32)]
    private static void ToggleAutoSetupOnSave()
    {
        AutoSetupOnSave = !AutoSetupOnSave;
        Debug.Log("[ExhibitDescriptor] Auto Setup On Save = " + (AutoSetupOnSave ? "ON" : "OFF"));
    }

    [MenuItem(AutoSetupMenuPath, true, 32)]
    private static bool ToggleAutoSetupOnSaveValidate()
    {
        Menu.SetChecked(AutoSetupMenuPath, AutoSetupOnSave);
        return true;
    }

    /// <summary>
    /// 저장 직전에 한 Scene 안의 작품/언어 전환 버튼을 전부 Setup 합니다.
    /// <c>sceneSaving</c> 단계에서 부르므로 여기서 바꾼 값이 그대로 파일에 기록됩니다.
    /// </summary>
    internal static void SetupSceneOnSave(Scene scene)
    {
        if (!scene.IsValid()) return;

        List<ExhibitInteractable> exhibits = new List<ExhibitInteractable>();
        ExhibitInteractable[] allExhibits = Object.FindObjectsOfType<ExhibitInteractable>(true);
        for (int i = 0; i < allExhibits.Length; i++)
        {
            if (allExhibits[i].gameObject.scene == scene) exhibits.Add(allExhibits[i]);
        }

        List<ExhibitLanguageSwitch> switches = new List<ExhibitLanguageSwitch>();
        ExhibitLanguageSwitch[] allSwitches = Object.FindObjectsOfType<ExhibitLanguageSwitch>(true);
        for (int i = 0; i < allSwitches.Length; i++)
        {
            if (allSwitches[i].gameObject.scene == scene) switches.Add(allSwitches[i]);
        }

        if (exhibits.Count == 0 && switches.Count == 0) return;

        for (int i = 0; i < exhibits.Count; i++) SetupExhibitFull(exhibits[i]);
        for (int i = 0; i < switches.Count; i++) SetupLanguageSwitch(switches[i]);

        Debug.Log("[ExhibitDescriptor] 저장 전 자동 Setup - Scene '" + scene.name + "': 작품 " + exhibits.Count +
                  " 개, 언어 전환 버튼 " + switches.Count + " 개");
    }
}

/// <summary>
/// Scene 저장 시점에 자동 Setup 을 걸어 주는 훅입니다.
///
/// <c>sceneSaving</c> 은 디스크에 쓰기 **직전**에 호출되므로, 여기서 수정한 참조와
/// 구운 Interact 값이 그대로 저장됩니다. (<c>sceneSaved</c> 는 이미 늦습니다)
/// </summary>
[InitializeOnLoad]
internal static class ExhibitDescriptorAutoSetup
{
    /// <summary>자동 Setup 이 또 다른 저장을 부르는 재진입을 막습니다.</summary>
    private static bool running;

    static ExhibitDescriptorAutoSetup()
    {
        // 도메인 리로드마다 생성자가 다시 돌므로 중복 구독을 먼저 끊습니다.
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        EditorSceneManager.sceneSaving += OnSceneSaving;
    }

    private static void OnSceneSaving(Scene scene, string path)
    {
        if (running) return;
        if (!ExhibitDescriptorTools.AutoSetupOnSave) return;

        // Play 중에는 저장해도 런타임 상태만 건드리게 되므로 건너뜁니다.
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        running = true;
        try
        {
            ExhibitDescriptorTools.SetupSceneOnSave(scene);
        }
        finally
        {
            running = false;
        }
    }
}
#endif
