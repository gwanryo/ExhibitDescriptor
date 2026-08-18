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

        ExhibitInteractable interactable = exhibit.GetComponent<ExhibitInteractable>();
        SetArtworkTitle(interactable, source.name);

        // 참조 연결 / 기하 굽기 / Interact 값 굽기는 최종 위치에 자리 잡은 뒤에 합니다.
        // (기하는 SetupExhibitFull 안에서 매번 다시 구우므로 여기서 따로 계산하지 않습니다)
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
    /// 런타임이 아이콘을 배치하는 데 필요한 <b>기하 정보 세 개</b>를 작품에 굽습니다.
    /// (중심 / extents / 얇은 축. 전부 Exhibit Root 의 로컬 좌표라 Root 가 회전해 있어도 안전)
    ///
    /// 사람이 손으로 고칠 값이 아니므로 <b>실행될 때마다 무조건 덮어씁니다.</b>
    /// 그래서 작품 Mesh 를 교체·이동·스케일해도 아이콘이 자동으로 따라갑니다.
    ///
    /// <paramref name="bounds"/> 는 <b>작품 Mesh 만</b>의 Bounds 여야 합니다. Overlay 는 Canvas/TMP 라
    /// Renderer 를 가지므로 반드시 걸러야 합니다. <see cref="CollectArtworkRenderers"/> 가 담당합니다.
    /// </summary>
    private static void BakeExhibitGeometry(ExhibitInteractable interactable, Bounds bounds)
    {
        if (interactable == null) return;

        SerializedObject so = new SerializedObject(interactable);

        SetVector3(so, "boundsCenterLocal", bounds.center);
        SetVector3(so, "boundsExtentsLocal", bounds.extents);
        SetInt(so, "thinAxis", GetThinAxis(bounds.size));

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(interactable);
    }

    /// <summary>
    /// 작품의 Bounds 를 구해 굽습니다. 대상 Renderer 가 하나도 없으면 <b>굽지 않고</b>
    /// 기존 값을 유지한 뒤 경고합니다. (0 으로 밀어 버리면 이미 잘 놓인 아이콘이 작품 중심에 박힙니다)
    /// </summary>
    private static bool TryBakeExhibitGeometry(ExhibitInteractable interactable)
    {
        if (interactable == null) return false;

        Renderer[] renderers = CollectArtworkRenderers(interactable);

        Bounds bounds;
        if (renderers.Length == 0 || !TryGetLocalBounds(interactable.transform, renderers, out bounds))
        {
            Debug.LogWarning("[ExhibitDescriptor] 작품 Mesh 의 Bounds 를 구하지 못해 기하 정보를 그대로 둡니다: " +
                             GetPath(interactable.transform) +
                             "\nExhibit 아래에 작품 Mesh(Renderer)가 있는지 확인하세요. " +
                             "(Overlay / InfoIcon 의 Renderer 는 대상이 아닙니다)", interactable);
            return false;
        }

        BakeExhibitGeometry(interactable, bounds);
        return true;
    }

    /// <summary>
    /// Bounds 계산 대상 Renderer: Exhibit 하위 전부에서 <see cref="ExhibitOverlay"/> 와
    /// <see cref="ExhibitInfoIcon"/> 하위를 제외한 것입니다.
    ///
    /// Overlay 는 World Space Canvas + TMP 라 Renderer 를 가집니다. 걸러내지 않으면 Panel 크기가
    /// 작품 Bounds 에 섞여 아이콘이 엉뚱하게 멀리 놓입니다.
    /// </summary>
    private static Renderer[] CollectArtworkRenderers(ExhibitInteractable interactable)
    {
        List<Renderer> result = new List<Renderer>();

        Renderer[] all = interactable.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Renderer renderer = all[i];
            if (renderer == null) continue;
            if (renderer.GetComponentInParent<ExhibitOverlay>(true) != null) continue;
            if (renderer.GetComponentInParent<ExhibitInfoIcon>(true) != null) continue;

            result.Add(renderer);
        }

        return result.ToArray();
    }

    /// <summary>
    /// 작품 로컬 Bounds 에서 <b>가장 얇은 축</b>(0 = X, 1 = Y, 2 = Z)을 고릅니다.
    /// 액자·그림·안내판은 두께가 가장 얇은 축이 곧 정면이 바라보는 축입니다.
    /// 두께가 같으면 X → Y → Z 순으로 먼저 오는 축을 씁니다. (정육면체는 X)
    ///
    /// <b>부호(+/-)는 추측하지 않습니다.</b> 부호는 런타임이 플레이어 머리 위치로 정합니다
    /// (<c>ExhibitInteractable._TickIcon</c> 의 <c>side</c>). 이것이 1.1 의 핵심 변경입니다.
    ///
    /// <b>왜 이렇게 바뀌었나 (1.0.x 의 회귀 이력)</b>
    ///  - 예전 <c>GetFrontNormalAxis</c> 는 Bounds 만으로 부호를 알 수 없어 <b>항상 + 를 정면으로
    ///    가정</b>했습니다. 작품이 로컬 -축을 관람자 쪽으로 두고 배치돼 있으면 그대로 틀렸고,
    ///    사람이 <c>OverlayAnchor</c> 를 180도 돌려 고쳐야 했습니다.
    ///  - 그 부호가 캔버스 규약(<c>GetOverlayRotation</c>)과 맞물려 Panel 이 뒤집혀 나왔습니다.
    ///    재검증 #3 실기에서 세 케이스 모두 글자가 좌우 반전됐고 dot 이 +2.5 / +2.5 / +1.45 로
    ///    양수였는데도 <c>Vector3.Angle(forward, 정면) ≈ 0</c> 검증은 전부 통과했습니다.
    ///    <b>각도만 보는 검증은 180도 뒤집힌 Panel 을 잡지 못합니다.</b> 스크린샷에서 글자가
    ///    실제로 읽히는지 눈으로 보는 것만 검증으로 인정합니다.
    ///  - 또 배치 값이 <b>생성 시점 Bounds 로 한 번 구워지고</b> Setup 이 다시 계산하지 않아
    ///    (사용자의 수동 보정을 보존하려는 의도였습니다) Mesh 를 교체·이동·스케일하면 전부
    ///    어긋났습니다. 이제 굽는 값은 배치 결과가 아니라 <b>기하</b>라서 사람이 고칠 값이 아니고,
    ///    그래서 <see cref="BakeExhibitGeometry"/> 가 저장할 때마다 무조건 덮어씁니다.
    /// </summary>
    private static int GetThinAxis(Vector3 size)
    {
        if (size.x <= size.y && size.x <= size.z) return 0;
        if (size.y <= size.z) return 1;
        return 2;
    }

    /// <summary>source 하위의 모든 Renderer 를 모아 아래 오버로드로 넘깁니다.</summary>
    private static bool TryGetLocalBounds(Transform space, GameObject source, out Bounds bounds)
    {
        return TryGetLocalBounds(space, source.GetComponentsInChildren<Renderer>(true), out bounds);
    }

    /// <summary>
    /// 주어진 Renderer 들을 감싸는 AABB 를 <paramref name="space"/> 의 로컬 좌표로 구합니다.
    ///
    /// 각 Renderer 의 **로컬(Mesh) Bounds** 8개 꼭짓점을 월드로 보낸 뒤 다시 space 로 가져옵니다.
    /// <see cref="Renderer.bounds"/>(월드 AABB)에서 출발하면 안 됩니다. 월드 AABB 는 이미 회전을
    /// 흡수해 부풀어 있는 상자라, 그 꼭짓점을 space 로 되돌리면 한 번 더 부풀어 오릅니다.
    /// (예: Y 45도로 돌아간 두께 0.05m 액자 → 깊이가 1m 넘게 잡혀 아이콘이 작품에서 그만큼
    ///  멀어집니다. 1.0.x 에서는 InteractionArea 가 통로까지 막았습니다.)
    /// </summary>
    private static bool TryGetLocalBounds(Transform space, Renderer[] renderers, out Bounds bounds)
    {
        bounds = new Bounds();

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
    /// 다만 Interact 레이가 작품 Collider 에 먼저 맞으면 ⓘ 아이콘이 반응하지 않으므로
    /// 방치하면 "클릭해도 아무 일도 없음" 의 원인이 됩니다.
    /// </summary>
    private static void WarnAboutArtworkColliders(GameObject source)
    {
        Collider[] colliders = source.GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0) return;

        Debug.LogWarning("[ExhibitDescriptor] 작품 Mesh 에 Collider 가 " + colliders.Length + " 개 있습니다: " +
                         GetPath(source.transform) + "\n" +
                         "Interact 레이가 여기에 먼저 막히면 ⓘ 아이콘을 클릭할 수 없습니다. " +
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
