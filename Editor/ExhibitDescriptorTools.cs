#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;

/// <summary>
/// Exhibit Descriptor 용 Editor 자동화 도구.
///
/// Tools > Exhibit Descriptor
///   - Create Exhibition Root            : ExhibitionRoot + ExhibitManager(+ ExhibitDescriptorSettings) 생성
///   - Create Exhibit (Template)         : Overlay 포함 작품 1개를 통째로 생성 + 자동 연결
///   - Create Exhibits From Selected Meshes : 선택한 Mesh 를 작품으로 일괄 변환 (ExhibitDescriptorBatchTools.cs)
///   - Setup Selected Exhibits           : 선택한 작품들의 참조 자동 연결 + Interact 값 반영
///   - Setup All Exhibits In Scene       : 씬 전체 일괄 처리 (100개 이상일 때 사용)
///   - Auto Setup On Save                : 저장할 때 자동으로 Setup 실행 (ExhibitDescriptorBatchTools.cs)
///   - Validate Scene                    : 누락된 참조/Collider 를 콘솔에 보고
///
/// 이 파일은 Editor 폴더에 있으므로 빌드에 포함되지 않습니다.
/// </summary>
public static partial class ExhibitDescriptorTools
{
    private const string MenuRoot = "Tools/Exhibit Descriptor/";

    // Canvas 로컬 단위(px). Canvas scale 0.001 이므로 600 = 0.6m
    private const float PanelWidth = 600f;
    private const float PanelHeight = 440f;
    private const float CanvasScale = 0.001f;

    // =====================================================================
    // 1. ExhibitionRoot + ExhibitManager
    // =====================================================================

    [MenuItem(MenuRoot + "Create Exhibition Root", false, 10)]
    public static void CreateExhibitionRoot()
    {
        // Additive Scene 대응: "Scene 당 1개" 규칙이므로 활성 Scene 안에서만 중복을 검사합니다.
        // 다른 Scene 에 Manager 가 있다고 해서 활성 Scene 에 만들지 못할 이유가 없습니다.
        Scene activeScene = EditorSceneManager.GetActiveScene();

        List<ExhibitManager> existingInScene = CollectManagersInScene(activeScene);
        if (existingInScene.Count > 0)
        {
            ExhibitManager existing = existingInScene[0];
            Debug.LogWarning("[ExhibitDescriptor] 이미 이 Scene(" + activeScene.name + ") 에 ExhibitManager 가 존재합니다: " +
                             GetPath(existing.transform), existing);
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        GameObject root = new GameObject("ExhibitionRoot");
        Undo.RegisterCreatedObjectUndo(root, "Create Exhibition Root");

        GameObject managerObject = new GameObject("ExhibitManager");
        managerObject.transform.SetParent(root.transform, false);
        managerObject.AddUdonSharpComponent<ExhibitManager>();

        // Overlay 폰트 슬롯은 Udon 화이트리스트 때문에 Manager 가 아니라 이 컴포넌트에 있습니다.
        // (이유는 ExhibitDescriptorSettings 주석 참고)
        EnsureSettings(managerObject);

        Selection.activeGameObject = managerObject;
        MarkSceneDirty();

        Debug.Log("[ExhibitDescriptor] ExhibitionRoot / ExhibitManager 를 생성했습니다. (Scene: " + activeScene.name + ")");
    }

    // =====================================================================
    // 2. Exhibit 템플릿 생성
    // =====================================================================

    [MenuItem(MenuRoot + "Create Exhibit (Template)", false, 11)]
    public static void CreateExhibitTemplate()
    {
        GameObject parent = null;

        // Project 창에서 선택한 Prefab Asset 은 부모가 될 수 없으므로 Scene 오브젝트만 인정합니다.
        GameObject selected = Selection.activeGameObject;
        if (selected != null && !selected.scene.IsValid()) selected = null;

        // 작품을 만들 Scene: 선택이 있으면 그 오브젝트의 Scene, 없으면 활성 Scene 입니다.
        // 부모 후보(ExhibitionRoot)도 반드시 그 Scene 안에서만 찾습니다. (Additive Scene 대응)
        Scene targetScene = selected != null ? selected.scene : EditorSceneManager.GetActiveScene();

        List<ExhibitManager> managers = CollectManagersInScene(targetScene);
        if (managers.Count > 0 && managers[0].transform.parent != null)
        {
            parent = managers[0].transform.parent.gameObject;
        }

        if (selected != null) parent = selected;

        GameObject exhibit = BuildExhibit("Exhibit_New");
        if (parent != null)
        {
            // 스케일된 오브젝트를 부모로 골랐을 때도 Overlay 가 의도한 크기로 나오도록
            // 월드 Scale 을 1 로 되돌립니다. (일괄 변환 도구와 같은 규칙)
            exhibit.transform.SetParent(parent.transform, false);
            NeutralizeWorldScale(exhibit.transform);
        }
        else if (targetScene.IsValid() && exhibit.scene != targetScene) SceneManager.MoveGameObjectToScene(exhibit, targetScene);

        // 참조 연결은 작품이 최종 Scene 에 자리 잡은 뒤에 합니다.
        // (manager 는 Scene 참조라, 옮기기 전에 연결하면 다른 Scene 의 Manager 를 물 수 있습니다.)
        SetupExhibitFull(exhibit.GetComponent<ExhibitInteractable>());

        Undo.RegisterCreatedObjectUndo(exhibit, "Create Exhibit");
        Selection.activeGameObject = exhibit;
        MarkSceneDirtyFor(exhibit);

        if (managers.Count == 0)
        {
            Debug.LogWarning("[ExhibitDescriptor] Scene(" + targetScene.name + ") 에 ExhibitManager 가 없습니다. " +
                             "Create Exhibition Root 를 먼저 실행하세요. (manager 참조가 비어 있는 상태로 생성됩니다)", exhibit);
        }

        Debug.Log("[ExhibitDescriptor] Exhibit 템플릿을 생성했습니다. Inspector 에서 데이터를 입력한 뒤 Prefab 으로 저장하세요.");
    }

    private static GameObject BuildExhibit(string name)
    {
        // ---- Root -------------------------------------------------------
        GameObject root = new GameObject(name);

        // ---- Artwork (placeholder) --------------------------------------
        GameObject artwork = GameObject.CreatePrimitive(PrimitiveType.Cube);
        artwork.name = "Artwork";
        artwork.transform.SetParent(root.transform, false);
        artwork.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        artwork.transform.localScale = new Vector3(1f, 1f, 0.05f);
        // Mesh 의 Collider 는 Interact 용이 아니므로 제거합니다. (Interaction 은 InteractionArea 담당)
        Object.DestroyImmediate(artwork.GetComponent<BoxCollider>());

        // CreatePrimitive 가 붙여 주는 built-in "Default-Material" 은 불투명 흰색이라
        // 교체 전까지 흰 상자가 Overlay/OverlayAnchor 배치를 가립니다.
        // 투명 전용 머티리얼로 바꿔 "아직 비어 있는 자리" 로 두고, 실제 작품 Mesh 로 교체하게 합니다.
        ApplyPlaceholderMaterial(artwork);

        // ---- InteractionArea --------------------------------------------
        // Interact 는 Collider 와 UdonBehaviour 가 같은 GameObject 일 때 가장 안전합니다.
        // 그래서 판정 전용 릴레이를 여기에 함께 둡니다.
        GameObject interactionArea = new GameObject("InteractionArea");
        interactionArea.transform.SetParent(root.transform, false);
        interactionArea.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        interactionArea.layer = 0; // Default
        BoxCollider box = interactionArea.AddComponent<BoxCollider>();
        box.size = new Vector3(1.2f, 1.2f, 0.3f);
        box.isTrigger = true;
        interactionArea.AddUdonSharpComponent<ExhibitInteractRelay>();

        // ---- OverlayAnchor (작품 옆) -------------------------------------
        GameObject anchor = new GameObject("OverlayAnchor");
        anchor.transform.SetParent(root.transform, false);
        anchor.transform.localPosition = new Vector3(0.9f, 1.5f, 0f);
        // Placeholder 작품은 로컬 +Z 를 정면으로 보므로 관람자도 +Z 쪽에 섭니다.
        // World Space Canvas 는 forward(+Z) 의 <반대쪽>에서 글자가 정방향으로 읽히므로
        // Panel 의 forward 는 -Z(관람자 반대쪽)여야 합니다. identity 로 두면 뒷면을 보여 줘
        // 글자가 좌우 반전됩니다. (같은 규약: ExhibitDescriptorBatchTools.GetOverlayRotation)
        anchor.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        // ---- Overlay -----------------------------------------------------
        GameObject overlayObject = BuildOverlay(root.transform);

        // ---- Udon Components --------------------------------------------
        ExhibitOverlay overlay = overlayObject.AddUdonSharpComponent<ExhibitOverlay>();
        root.AddUdonSharpComponent<ExhibitInteractable>();

        // 버튼에 Udon 부착
        AttachButton(overlayObject, "CloseButton", ExhibitButtonAction.Close, "닫기", "Close", "閉じる");
        AttachButton(overlayObject, "ScrollUpButton", ExhibitButtonAction.ScrollUp, "위로", "Up", "上へ");
        AttachButton(overlayObject, "ScrollDownButton", ExhibitButtonAction.ScrollDown, "아래로", "Down", "下へ");

        // Scene 에 자리 잡기 전이라 manager(Scene 참조) 연결은 하지 않습니다.
        // Prefab 내부에서 닫히는 참조만 먼저 연결하고, 나머지는 호출자가 SetupExhibitFull 로 마무리합니다.
        SetupOverlay(overlay);

        overlayObject.SetActive(false); // 닫힌 상태로 시작

        return root;
    }

    // ---------------------------------------------------------------------
    // Artwork Placeholder 머티리얼
    // ---------------------------------------------------------------------

    private const string PlaceholderMaterialFolder = "Assets/ExhibitDescriptor/Materials";
    private const string PlaceholderMaterialPath = PlaceholderMaterialFolder + "/ExhibitPlaceholder.mat";

    /// <summary>Placeholder Mesh 를 투명하게 만들고 그림자도 끕니다.</summary>
    private static void ApplyPlaceholderMaterial(GameObject target)
    {
        MeshRenderer renderer = target.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        Material placeholder = GetPlaceholderMaterial();
        if (placeholder != null) renderer.sharedMaterial = placeholder;

        // 투명한데 그림자만 남으면 보이지 않는 상자가 바닥에 그림자를 드리웁니다.
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>
    /// Placeholder 용 투명 머티리얼을 가져옵니다. 없으면 1회만 생성합니다.
    ///
    /// built-in "Default-Material" 의 색을 직접 바꾸지 않는 이유
    ///  - 읽기 전용 내장 애셋이라 수정이 저장되지 않거나, 저장되면 그 머티리얼을 쓰는
    ///    프로젝트 안의 다른 오브젝트까지 전부 반투명해집니다.
    ///
    /// 작품마다 새로 만들지 않고 프로젝트에 1개만 두는 이유
    ///  - 작품 100개 = 머티리얼 100개가 되면 배칭이 깨지고 빌드 용량도 늘어납니다.
    /// </summary>
    private static Material GetPlaceholderMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(PlaceholderMaterialPath);
        if (existing != null) return existing;

        Shader standard = Shader.Find("Standard");
        if (standard == null)
        {
            Debug.LogWarning("[ExhibitDescriptor] Standard 셰이더를 찾지 못해 Placeholder 머티리얼을 만들지 못했습니다. " +
                             "Artwork 는 기본 머티리얼(불투명 흰색)로 생성됩니다.");
            return null;
        }

        Material material = new Material(standard);
        material.name = "ExhibitPlaceholder";

        // 알파 0 = 완전히 투명. Mesh 는 남아 있으므로 Hierarchy 에서 선택하면
        // Scene View 에 Bounds/Wireframe 이 그려져 위치와 크기는 그대로 잡을 수 있습니다.
        SetupTransparent(material, new Color(1f, 1f, 1f, 0f));

        EnsureAssetFolder(PlaceholderMaterialFolder);
        AssetDatabase.CreateAsset(material, PlaceholderMaterialPath);
        AssetDatabase.SaveAssets();

        Debug.Log("[ExhibitDescriptor] Placeholder 머티리얼을 생성했습니다: " + PlaceholderMaterialPath);
        return material;
    }

    /// <summary>
    /// Standard 셰이더를 Transparent(알파 블렌드) 모드로 전환합니다.
    /// Standard 는 Opaque 상태에서 알파를 무시하므로, 색만 바꿔서는 투명해지지 않습니다.
    /// (Unity 의 StandardShaderGUI.SetupMaterialWithBlendMode 와 같은 설정입니다.)
    /// </summary>
    private static void SetupTransparent(Material material, Color color)
    {
        material.SetFloat("_Mode", 3f); // 0 Opaque / 1 Cutout / 2 Fade / 3 Transparent
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.color = color;
    }

    /// <summary>
    /// 이 작품의 Renderer 중에 아직 Placeholder 머티리얼을 쓰는 것이 있는지 확인합니다.
    /// Placeholder 애셋 자체가 없으면(= 템플릿을 한 번도 만들지 않은 프로젝트) 항상 false 입니다.
    /// </summary>
    private static bool UsesPlaceholderMaterial(Component context)
    {
        if (context == null) return false;

        Material placeholder = AssetDatabase.LoadAssetAtPath<Material>(PlaceholderMaterialPath);
        if (placeholder == null) return false;

        Renderer[] renderers = context.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].sharedMaterials;
            if (materials == null) continue;

            for (int m = 0; m < materials.Length; m++)
            {
                if (materials[m] == placeholder) return true;
            }
        }

        return false;
    }

    /// <summary>"Assets/A/B" 형태의 경로를 한 단계씩 만들어 둡니다.</summary>
    private static void EnsureAssetFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string[] parts = folder.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static GameObject BuildOverlay(Transform parent)
    {
        // Overlay Root = World Space Canvas
        GameObject overlayObject = new GameObject("Overlay");
        overlayObject.transform.SetParent(parent, false);
        overlayObject.transform.localPosition = new Vector3(0.9f, 1.5f, 0f);

        Canvas canvas = overlayObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform canvasRect = overlayObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        canvasRect.localScale = new Vector3(CanvasScale, CanvasScale, CanvasScale);

        overlayObject.AddComponent<CanvasGroup>();

        // ---- Panel (Scale 애니메이션 대상) --------------------------------
        GameObject panel = CreateUIObject("Panel", overlayObject.transform);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        Stretch(panelRect);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.04f, 0.04f, 0.06f, 0.86f);
        panelImage.raycastTarget = false;

        // ---- TitleText ---------------------------------------------------
        GameObject titleObject = CreateUIObject("TitleText", panel.transform);
        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(-56f, 56f);   // 좌우 28px 여백
        titleRect.anchoredPosition = new Vector2(0f, -20f);
        TextMeshProUGUI title = titleObject.AddComponent<TextMeshProUGUI>();
        ConfigureText(title, 40f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        title.text = "Title";

        // ---- SubtitleText ------------------------------------------------
        GameObject subtitleObject = CreateUIObject("SubtitleText", panel.transform);
        RectTransform subtitleRect = subtitleObject.GetComponent<RectTransform>();
        subtitleRect.anchorMin = new Vector2(0f, 1f);
        subtitleRect.anchorMax = new Vector2(1f, 1f);
        subtitleRect.pivot = new Vector2(0.5f, 1f);
        subtitleRect.sizeDelta = new Vector2(-56f, 34f);
        subtitleRect.anchoredPosition = new Vector2(0f, -80f);
        TextMeshProUGUI subtitle = subtitleObject.AddComponent<TextMeshProUGUI>();
        ConfigureText(subtitle, 24f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        subtitle.color = new Color(0.78f, 0.78f, 0.82f, 1f);
        subtitle.text = "Subtitle";

        // ---- DescriptionScrollView --------------------------------------
        GameObject scrollView = CreateUIObject("DescriptionScrollView", panel.transform);
        RectTransform scrollRect = scrollView.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0f, 0f);
        scrollRect.anchorMax = new Vector2(1f, 1f);
        scrollRect.offsetMin = new Vector2(28f, 96f);   // 아래쪽 버튼 영역 확보
        scrollRect.offsetMax = new Vector2(-28f, -122f);

        GameObject viewport = CreateUIObject("Viewport", scrollView.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewport.AddComponent<RectMask2D>();

        GameObject content = CreateUIObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        GameObject descriptionObject = CreateUIObject("DescriptionText", content.transform);
        TextMeshProUGUI description = descriptionObject.AddComponent<TextMeshProUGUI>();
        ConfigureText(description, 26f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        description.text = "Description";

        // ---- Buttons ------------------------------------------------------
        // 라벨 문자는 TMP 기본 폰트(LiberationSans SDF)의 fallback 으로도 그려지는 것만 씁니다.
        //  - ▲ U+25B2 / ▼ U+25BC : fallback 에 있어 정상 표시
        //  - ✕ U+2715 : 본 폰트에도 fallback 에도 없어 □ 로 보였습니다. → × U+00D7 로 교체
        CreateButton(panel.transform, "ScrollUpButton", "▲", new Vector2(0f, 0f), new Vector2(28f, 24f), new Vector2(64f, 60f));
        CreateButton(panel.transform, "ScrollDownButton", "▼", new Vector2(0f, 0f), new Vector2(100f, 24f), new Vector2(64f, 60f));
        CreateButton(panel.transform, "CloseButton", "×", new Vector2(1f, 0f), new Vector2(-28f, 24f), new Vector2(140f, 60f));

        return overlayObject;
    }

    private static void CreateButton(Transform parent, string name, string label, Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject buttonObject = CreateUIObject(name, parent);
        buttonObject.layer = 0; // Default: Udon Interact 가 확실하게 인식하는 Layer

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(anchor.x, anchor.y);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.22f, 0.24f, 0.30f, 0.95f);
        image.raycastTarget = false;

        BoxCollider collider = buttonObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(size.x, size.y, 10f);
        collider.center = new Vector3(size.x * (0.5f - anchor.x), size.y * (0.5f - anchor.y), 0f);
        collider.isTrigger = true;

        GameObject labelObject = CreateUIObject("Label", buttonObject.transform);
        labelObject.layer = 0;
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        Stretch(labelRect);
        TextMeshProUGUI text = labelObject.AddComponent<TextMeshProUGUI>();
        ConfigureText(text, 30f, FontStyles.Bold, TextAlignmentOptions.Center);
        text.text = label;
    }

    private static void AttachButton(GameObject overlayObject, string name, ExhibitButtonAction action, string kr, string en, string jp)
    {
        Transform target = FindChildRecursive(overlayObject.transform, name);
        if (target == null) return;

        ExhibitOverlayButton button = target.gameObject.AddUdonSharpComponent<ExhibitOverlayButton>();

        SerializedObject so = new SerializedObject(button);
        SetEnum(so, "action", (int)action);
        SetString(so, "interactionTextKR", kr);
        SetString(so, "interactionTextEN", en);
        SetString(so, "interactionTextJP", jp);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(button);
    }

    // =====================================================================
    // 3. 자동 연결 (Setup)
    // =====================================================================

    [MenuItem(MenuRoot + "Setup Selected Exhibits", false, 30)]
    public static void SetupSelectedExhibits()
    {
        GameObject[] selection = Selection.gameObjects;
        if (selection == null || selection.Length == 0)
        {
            Debug.LogWarning("[ExhibitDescriptor] 선택된 오브젝트가 없습니다.");
            return;
        }

        // 폰트를 읽기 전에 옛 Scene 의 Manager 에 설정 컴포넌트를 보강합니다.
        EnsureSettingsForLoadedScenes();

        int count = 0;
        int switchCount = 0;

        for (int i = 0; i < selection.Length; i++)
        {
            ExhibitInteractable[] found = selection[i].GetComponentsInChildren<ExhibitInteractable>(true);
            for (int j = 0; j < found.Length; j++)
            {
                SetupExhibitFull(found[j]);
                MarkSceneDirtyFor(found[j].gameObject);
                count++;
            }

            ExhibitLanguageSwitch[] switches = selection[i].GetComponentsInChildren<ExhibitLanguageSwitch>(true);
            for (int j = 0; j < switches.Length; j++)
            {
                SetupLanguageSwitch(switches[j]);
                MarkSceneDirtyFor(switches[j].gameObject);
                switchCount++;
            }
        }

        Debug.Log("[ExhibitDescriptor] Setup 완료: 작품 " + count + " 개, 언어 전환 버튼 " + switchCount + " 개");
    }

    [MenuItem(MenuRoot + "Setup All Exhibits In Scene", false, 31)]
    public static void SetupAllExhibitsInScene()
    {
        // 폰트를 읽기 전에 옛 Scene 의 Manager 에 설정 컴포넌트를 보강합니다.
        EnsureSettingsForLoadedScenes();

        // 로드된 모든 Scene 을 처리합니다. manager 연결은 각 오브젝트가 속한 Scene 기준으로
        // 이루어지므로, Additive 로 여러 Scene 을 열어 둔 상태에서도 교차 참조가 생기지 않습니다.
        ExhibitInteractable[] all = Object.FindObjectsOfType<ExhibitInteractable>(true);
        for (int i = 0; i < all.Length; i++)
        {
            SetupExhibitFull(all[i]);
            MarkSceneDirtyFor(all[i].gameObject);
        }

        ExhibitLanguageSwitch[] switches = Object.FindObjectsOfType<ExhibitLanguageSwitch>(true);
        for (int i = 0; i < switches.Length; i++)
        {
            SetupLanguageSwitch(switches[i]);
            MarkSceneDirtyFor(switches[i].gameObject);
        }

        LogPerSceneSummary(all, switches);
    }

    private static void LogPerSceneSummary(ExhibitInteractable[] exhibits, ExhibitLanguageSwitch[] switches)
    {
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            int exhibitCount = 0;
            for (int i = 0; i < exhibits.Length; i++)
            {
                if (exhibits[i].gameObject.scene == scene) exhibitCount++;
            }

            int switchCount = 0;
            for (int i = 0; i < switches.Length; i++)
            {
                if (switches[i].gameObject.scene == scene) switchCount++;
            }

            if (exhibitCount == 0 && switchCount == 0) continue;

            Debug.Log("[ExhibitDescriptor] Setup 완료 - Scene '" + scene.name + "': 작품 " + exhibitCount +
                      " 개, 언어 전환 버튼 " + switchCount + " 개, Manager " +
                      CollectManagersInScene(scene).Count + " 개");
        }
    }

    /// <summary>언어 전환 버튼에 같은 Scene 의 Manager 를 연결합니다.</summary>
    private static void SetupLanguageSwitch(ExhibitLanguageSwitch languageSwitch)
    {
        if (languageSwitch == null) return;

        ExhibitManager manager = FindManagerForScene(languageSwitch);

        SerializedObject so = new SerializedObject(languageSwitch);

        AssignManagerProperty(so, languageSwitch, manager, "언어 전환 버튼");

        so.ApplyModifiedProperties();

        // 라벨이 "한국어" / "日本語" 라 폰트가 없으면 그대로 □ 가 됩니다.
        ApplyOverlayFont(languageSwitch, FindOverlayFont(languageSwitch));

        if (languageSwitch.GetComponent<Collider>() == null)
        {
            Debug.LogError("[ExhibitDescriptor] 언어 전환 버튼에 Collider 가 없습니다: " +
                           GetPath(languageSwitch.transform), languageSwitch);
        }

        EditorUtility.SetDirty(languageSwitch);
        UdonSharpEditorUtility.CopyProxyToUdon(languageSwitch);
        RecordPrefabModifications(languageSwitch);
    }

    private static void SetupExhibitFull(ExhibitInteractable interactable)
    {
        if (interactable == null) return;

        SetupInteractable(interactable);

        ExhibitOverlay overlay = interactable.GetComponentInChildren<ExhibitOverlay>(true);
        if (overlay != null) SetupOverlay(overlay);

        ExhibitOverlayButton[] buttons = interactable.GetComponentsInChildren<ExhibitOverlayButton>(true);
        for (int i = 0; i < buttons.Length; i++) SetupButton(buttons[i], overlay);
    }

    private static void SetupInteractable(ExhibitInteractable interactable)
    {
        if (interactable == null) return;

        ExhibitManager manager = FindManagerForScene(interactable);

        SerializedObject so = new SerializedObject(interactable);

        // Manager (같은 Scene 의 Manager 만 연결합니다 - Additive Scene 대응)
        AssignManagerProperty(so, interactable, manager, "작품");

        // Overlay
        SerializedProperty overlayProperty = so.FindProperty("overlay");
        if (overlayProperty != null && overlayProperty.objectReferenceValue == null)
        {
            ExhibitOverlay overlay = interactable.GetComponentInChildren<ExhibitOverlay>(true);
            if (overlay != null) overlayProperty.objectReferenceValue = overlay;
        }

        // OverlayAnchor
        SerializedProperty anchorProperty = so.FindProperty("overlayAnchor");
        if (anchorProperty != null && anchorProperty.objectReferenceValue == null)
        {
            Transform anchor = FindChildRecursive(interactable.transform, "OverlayAnchor");
            if (anchor != null) anchorProperty.objectReferenceValue = anchor;
        }

        // Interact 판정 릴레이 (InteractionArea) 수집
        ExhibitInteractRelay[] relays = interactable.GetComponentsInChildren<ExhibitInteractRelay>(true);
        SerializedProperty relayProperty = so.FindProperty("interactRelays");
        if (relayProperty != null && relayProperty.isArray)
        {
            relayProperty.arraySize = relays.Length;
            for (int i = 0; i < relays.Length; i++)
            {
                relayProperty.GetArrayElementAtIndex(i).objectReferenceValue = relays[i];
            }
        }

        so.ApplyModifiedProperties(); // Undo 지원 (Ctrl+Z 로 되돌릴 수 있음)

        // UdonBehaviour 에 Interact 문구 / Proximity 를 직접 구워 넣습니다.
        float proximity = GetFloat(so, "interactionProximity", 2f);
        string interactText = GetString(so, "interactionTextKR", "");

        if (string.IsNullOrEmpty(interactText))
        {
            interactText = manager != null ? manager.defaultInteractionTextKR : "설명";
        }

        BakeInteractSettings(interactable, interactText, proximity);

        // 릴레이 쪽도 같은 값으로 맞추고 target 을 연결합니다.
        for (int i = 0; i < relays.Length; i++)
        {
            SetupRelay(relays[i], interactable, interactText, proximity);
        }

        // Scene View 와 런타임 위치가 어긋나지 않도록 Overlay 를 Anchor 로 미리 스냅합니다.
        SnapOverlayToAnchor(interactable);

        EditorUtility.SetDirty(interactable);
        UdonSharpEditorUtility.CopyProxyToUdon(interactable);
        RecordPrefabModifications(interactable);
    }

    private static void SetupRelay(ExhibitInteractRelay relay, ExhibitInteractable target, string interactText, float proximity)
    {
        if (relay == null) return;

        SerializedObject so = new SerializedObject(relay);

        SerializedProperty targetProperty = so.FindProperty("target");
        if (targetProperty != null) targetProperty.objectReferenceValue = target;

        so.ApplyModifiedProperties();

        BakeInteractSettings(relay, interactText, proximity);

        if (relay.GetComponent<Collider>() == null)
        {
            Debug.LogError("[ExhibitDescriptor] InteractionArea 에 Collider 가 없습니다: " + GetPath(relay.transform), relay);
        }

        if (relay.gameObject.layer != 0) relay.gameObject.layer = 0;

        EditorUtility.SetDirty(relay);
        UdonSharpEditorUtility.CopyProxyToUdon(relay);
        RecordPrefabModifications(relay);
    }

    /// <summary>Overlay 를 OverlayAnchor 의 위치/회전으로 에디터에서 미리 이동시킵니다.</summary>
    private static void SnapOverlayToAnchor(ExhibitInteractable interactable)
    {
        SerializedObject so = new SerializedObject(interactable);

        SerializedProperty snapProperty = so.FindProperty("snapToAnchorOnOpen");
        if (snapProperty != null && !snapProperty.boolValue) return;

        Object anchorObject = GetObject(so, "overlayAnchor");
        Object overlayObject = GetObject(so, "overlay");
        if (anchorObject == null || overlayObject == null) return;

        Transform anchor = anchorObject as Transform;
        ExhibitOverlay overlay = overlayObject as ExhibitOverlay;
        if (anchor == null || overlay == null) return;

        Transform overlayTransform = overlay.transform;
        if (overlayTransform.position == anchor.position && overlayTransform.rotation == anchor.rotation) return;

        Undo.RecordObject(overlayTransform, "Snap Overlay To Anchor");
        overlayTransform.position = anchor.position;
        overlayTransform.rotation = anchor.rotation;
        RecordPrefabModifications(overlayTransform);
    }

    /// <summary>
    /// 같은 Scene 에 있는 ExhibitManager 를 찾습니다. (Additive Scene 안전)
    ///
    /// 같은 Scene 에 없으면 <c>null</c> 을 반환합니다.
    /// 다른 Scene 의 Manager 를 대신 직렬화하면, 그 Scene 이 언로드될 때 작품이 죽는
    /// 교차 Scene 참조가 만들어지므로 "누락"으로 보고하는 편이 안전합니다.
    /// </summary>
    private static ExhibitManager FindManagerForScene(Component context)
    {
        if (context == null) return null;

        List<ExhibitManager> managers = CollectManagersInScene(context.gameObject.scene);
        if (managers.Count == 0) return null;

        if (managers.Count > 1)
        {
            Debug.LogWarning("[ExhibitDescriptor] Scene(" + context.gameObject.scene.name + ") 에 ExhibitManager 가 " +
                             managers.Count + " 개 있습니다. Scene 당 1개만 두세요. 첫 번째를 연결합니다: " +
                             GetPath(managers[0].transform), managers[0]);
        }

        // 비활성 Manager 는 Update 틱이 돌지 않으므로 활성 Manager 를 우선합니다.
        for (int i = 0; i < managers.Count; i++)
        {
            if (managers[i].gameObject.activeInHierarchy) return managers[i];
        }

        return managers[0];
    }

    /// <summary>
    /// manager 필드를 같은 Scene 의 Manager 로 맞춥니다.
    ///
    /// 오브젝트를 다른 Scene 으로 복사/이동하면 예전 Scene 의 Manager 참조가 그대로 남습니다.
    /// 그 Scene 이 언로드되면 참조가 죽어 언어 전환과 Overlay 틱이 함께 멈추므로,
    /// 비어 있을 때뿐 아니라 <b>다른 Scene 을 가리킬 때도</b> 교체합니다.
    ///
    /// 같은 Scene 에 Manager 가 없으면 잘못된 참조를 남기지 않고 비웁니다.
    /// (런타임 _EnsureManager 가 다시 찾을 수 있고, Validate 도 "누락" 으로 잡아 줍니다)
    /// 단, Prefab Asset / Prefab 편집 모드처럼 열려 있는 Scene 밖의 오브젝트는
    /// 대신 연결할 Manager 자체가 없으므로 건드리지 않습니다.
    ///
    /// 쓰기는 <paramref name="so"/> 를 통해서만 하므로 Undo / Prefab override 기록 /
    /// Udon proxy 동기화 경로가 그대로 유지됩니다.
    /// </summary>
    private static void AssignManagerProperty(SerializedObject so, Component context, ExhibitManager manager, string label)
    {
        SerializedProperty managerProperty = so.FindProperty("manager");
        if (managerProperty == null) return;

        ExhibitManager assigned = managerProperty.objectReferenceValue as ExhibitManager;

        // 이미 같은 Scene 을 가리키면 그대로 둡니다. (Scene 에 Manager 가 2개 이상일 때
        // 사용자가 고른 쪽을 임의로 바꾸지 않기 위해서입니다)
        if (assigned != null && assigned.gameObject.scene == context.gameObject.scene) return;

        if (manager != null)
        {
            if (assigned != null)
            {
                Debug.Log("[ExhibitDescriptor] manager 가 다른 Scene(" + assigned.gameObject.scene.name +
                          ") 을 가리켜 같은 Scene 의 Manager 로 교체했습니다: " + GetPath(context.transform), context);
            }

            managerProperty.objectReferenceValue = manager;
            return;
        }

        if (!IsLoadedScene(context.gameObject.scene)) return;

        if (managerProperty.objectReferenceValue != null)
        {
            managerProperty.objectReferenceValue = null;

            Debug.LogWarning("[ExhibitDescriptor] Scene(" + context.gameObject.scene.name +
                             ") 에 ExhibitManager 가 없어 다른 Scene 을 가리키던 manager 를 해제했습니다. " +
                             "이 Scene 에도 ExhibitManager 를 1개 만들어 주세요 - " + label + ": " +
                             GetPath(context.transform), context);
            return;
        }

        Debug.LogWarning("[ExhibitDescriptor] Scene(" + context.gameObject.scene.name +
                         ") 에 ExhibitManager 가 없어 manager 를 연결하지 못했습니다 - " + label + ": " +
                         GetPath(context.transform), context);
    }

    /// <summary>해당 Scene 안의 ExhibitManager 를 모두 모읍니다. (비활성 포함)</summary>
    private static List<ExhibitManager> CollectManagersInScene(Scene scene)
    {
        List<ExhibitManager> result = new List<ExhibitManager>();
        if (!scene.IsValid()) return result;

        ExhibitManager[] all = Object.FindObjectsOfType<ExhibitManager>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].gameObject.scene == scene) result.Add(all[i]);
        }

        return result;
    }

    /// <summary>Hierarchy 에 열려 있는 Scene 인지 확인합니다. (Prefab 편집 모드의 Preview Scene 제외)</summary>
    private static bool IsLoadedScene(Scene scene)
    {
        if (!scene.IsValid()) return false;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i) == scene) return true;
        }

        return false;
    }

    private static void SetupOverlay(ExhibitOverlay overlay)
    {
        if (overlay == null) return;

        SerializedObject so = new SerializedObject(overlay);

        AssignIfEmpty(so, "canvasGroup", overlay.GetComponentInChildren<CanvasGroup>(true));

        SerializedProperty scaleRootProperty = so.FindProperty("scaleRoot");
        if (scaleRootProperty != null && scaleRootProperty.objectReferenceValue == null)
        {
            Transform panel = FindChildRecursive(overlay.transform, "Panel");
            scaleRootProperty.objectReferenceValue = panel != null ? panel : overlay.transform;
        }

        AssignIfEmpty(so, "titleText", FindText(overlay.transform, "TitleText"));
        AssignIfEmpty(so, "subtitleText", FindText(overlay.transform, "SubtitleText"));
        AssignIfEmpty(so, "descriptionText", FindText(overlay.transform, "DescriptionText"));

        AssignIfEmpty(so, "scrollViewport", FindRect(overlay.transform, "Viewport"));
        AssignIfEmpty(so, "scrollContent", FindRect(overlay.transform, "Content"));

        // Buttons 배열
        SerializedProperty buttonsProperty = so.FindProperty("buttons");
        if (buttonsProperty != null && buttonsProperty.isArray)
        {
            ExhibitOverlayButton[] buttons = overlay.GetComponentsInChildren<ExhibitOverlayButton>(true);
            buttonsProperty.arraySize = buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                buttonsProperty.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
            }
        }

        so.ApplyModifiedProperties();

        // 한글/일본어가 □ 로 보이지 않도록 Manager 에 지정된 폰트를 구워 넣습니다.
        ApplyOverlayFont(overlay, FindOverlayFont(overlay));

        EditorUtility.SetDirty(overlay);
        UdonSharpEditorUtility.CopyProxyToUdon(overlay);
        RecordPrefabModifications(overlay);
    }

    /// <summary>
    /// 같은 Scene 의 <see cref="ExhibitDescriptorSettings"/> 에 지정된 Overlay 폰트를 가져옵니다.
    /// (컴포넌트가 없거나 비어 있으면 <c>null</c> = 폰트 미지정)
    ///
    /// <see cref="FindManagerForScene"/> 를 쓰지 않는 이유: 그쪽은 Manager 가 2개 이상이면
    /// 경고를 찍습니다. Setup 한 번에 같은 경고가 여러 번 나가지 않도록 여기서는 조용히 찾습니다.
    /// 컴포넌트가 없는 옛 Scene 도 조용히 "폰트 미지정" 으로 처리합니다. 그 상태는
    /// <see cref="ValidateScene"/> 이 한 번만 경고합니다.
    /// </summary>
    private static TMP_FontAsset FindOverlayFont(Component context)
    {
        if (context == null) return null;

        List<ExhibitDescriptorSettings> settings = CollectSettingsInScene(context.gameObject.scene);
        for (int i = 0; i < settings.Count; i++)
        {
            if (settings[i].overlayFont != null) return settings[i].overlayFont;
        }

        return null;
    }

    /// <summary>해당 Scene 안의 <see cref="ExhibitDescriptorSettings"/> 를 모두 모읍니다. (비활성 포함)</summary>
    private static List<ExhibitDescriptorSettings> CollectSettingsInScene(Scene scene)
    {
        List<ExhibitDescriptorSettings> result = new List<ExhibitDescriptorSettings>();
        if (!scene.IsValid()) return result;

        ExhibitDescriptorSettings[] all = Object.FindObjectsOfType<ExhibitDescriptorSettings>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].gameObject.scene == scene) result.Add(all[i]);
        }

        return result;
    }

    /// <summary>
    /// <paramref name="managerObject"/> 에 <see cref="ExhibitDescriptorSettings"/> 를 보장합니다.
    /// (이미 있으면 그것을 그대로 돌려줍니다)
    /// </summary>
    private static ExhibitDescriptorSettings EnsureSettings(GameObject managerObject)
    {
        if (managerObject == null) return null;

        ExhibitDescriptorSettings settings = managerObject.GetComponent<ExhibitDescriptorSettings>();
        if (settings != null) return settings;

        return Undo.AddComponent<ExhibitDescriptorSettings>(managerObject);
    }

    /// <summary>
    /// 로드된 Scene 의 Manager 에 <see cref="ExhibitDescriptorSettings"/> 가 없으면 붙입니다.
    ///
    /// Overlay 폰트 슬롯이 Manager 에서 이 컴포넌트로 옮겨졌기 때문에, 옛 버전으로 만든 Scene 에는
    /// 컴포넌트가 없습니다. 사용자가 Add Component 를 손으로 찾지 않아도 되도록 Setup 메뉴가
    /// 한 번 붙여 줍니다. 저장 시 자동 Setup(<c>sceneSaving</c>) 경로에서는 부르지 않습니다 -
    /// 저장 중에 컴포넌트를 새로 붙이면 그 Scene 이 다시 dirty 가 되기 때문입니다.
    /// </summary>
    private static void EnsureSettingsForLoadedScenes()
    {
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            List<ExhibitManager> managers = CollectManagersInScene(scene);
            for (int i = 0; i < managers.Count; i++)
            {
                if (managers[i].GetComponent<ExhibitDescriptorSettings>() != null) continue;

                EnsureSettings(managers[i].gameObject);
                MarkSceneDirtyFor(managers[i].gameObject);

                Debug.Log("[ExhibitDescriptor] ExhibitManager 에 ExhibitDescriptorSettings 를 추가했습니다. " +
                          "Overlay 폰트는 이제 이 컴포넌트의 'Overlay Font' 에 지정합니다: " +
                          GetPath(managers[i].transform), managers[i]);
            }
        }
    }

    /// <summary>
    /// <paramref name="root"/> 아래의 모든 TMP 텍스트에 폰트를 적용합니다.
    ///
    /// 폰트를 패키지에 동봉하지 않고 <see cref="ExhibitDescriptorSettings.overlayFont"/> 슬롯으로 받는 이유
    ///  - CJK 글리프를 담은 폰트는 재배포 조건이 폰트마다 다릅니다. 패키지가 임의로 품으면
    ///    이 패키지를 쓰는 월드까지 그 라이선스를 따라가게 됩니다. 그래서 폰트 선택은
    ///    프로젝트에 맡기고, 도구는 "지정한 폰트를 빠짐없이 꽂아 주는" 일만 합니다.
    ///  - 폰트를 지정하지 않으면 TMP 기본값(LiberationSans SDF)이 쓰이는데 한글/일본어 글리프가
    ///    없어 전부 □ 로 보입니다. 이 상태는 <see cref="ValidateScene"/> 이 경고합니다.
    ///
    /// 언어 전환 버튼 라벨도 대상입니다. "한국어" / "日本語" 를 표시하므로 같은 문제가 납니다.
    /// </summary>
    private static void ApplyOverlayFont(Component root, TMP_FontAsset font)
    {
        if (root == null || font == null) return;

        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null) continue;
            if (text.font == font) continue;

            Undo.RecordObject(text, "Apply Overlay Font");
            text.font = font;

            EditorUtility.SetDirty(text);
            RecordPrefabModifications(text);
        }
    }

    private static void SetupButton(ExhibitOverlayButton button, ExhibitOverlay overlay)
    {
        if (button == null) return;

        SerializedObject so = new SerializedObject(button);

        SerializedProperty overlayProperty = so.FindProperty("overlay");
        if (overlayProperty != null)
        {
            // 자기를 담고 있는 Overlay 가 정답입니다. SetupOverlay 도 같은 기준(자식 검색)으로
            // buttons 배열을 채우므로 둘이 어긋나지 않습니다.
            // Overlay 는 보통 꺼져 있으므로 includeInactive 로 찾아야 합니다.
            ExhibitOverlay target = button.GetComponentInParent<ExhibitOverlay>(true);
            if (target == null) target = overlay;

            // 비어 있을 때만 채우면 안 됩니다. 버튼을 다른 Overlay 로 복제/이동한 뒤 Setup 을
            // 돌려도 예전 Overlay 참조가 남아, Close/Scroll 이 남의 작품을 조작합니다.
            if (target != null && overlayProperty.objectReferenceValue != target)
            {
                overlayProperty.objectReferenceValue = target;
            }
        }

        so.ApplyModifiedProperties();

        float proximity = GetFloat(so, "interactionProximity", 3f);
        string text = GetString(so, "interactionTextKR", "");
        BakeInteractSettings(button, text, proximity);

        // Collider 가 없으면 Interact 가 동작하지 않으므로 보정합니다.
        if (button.GetComponent<Collider>() == null)
        {
            RectTransform rect = button.GetComponent<RectTransform>();
            BoxCollider collider = button.gameObject.AddComponent<BoxCollider>();
            if (rect != null)
            {
                collider.size = new Vector3(rect.rect.width, rect.rect.height, 10f);
            }
            collider.isTrigger = true;
            Debug.LogWarning("[ExhibitDescriptor] BoxCollider 를 자동 추가했습니다: " + GetPath(button.transform));
        }

        if (button.gameObject.layer != 0)
        {
            button.gameObject.layer = 0;
        }

        EditorUtility.SetDirty(button);
        UdonSharpEditorUtility.CopyProxyToUdon(button);
        RecordPrefabModifications(button);
    }

    /// <summary>Interact 문구 / Proximity 를 UdonBehaviour 에 직접 기록합니다.</summary>
    private static void BakeInteractSettings(UdonSharpBehaviour behaviour, string interactText, float proximity)
    {
        UdonBehaviour udonBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
        if (udonBehaviour == null) return;

        SerializedObject so = new SerializedObject(udonBehaviour);

        SerializedProperty textProperty = so.FindProperty("interactText");
        if (textProperty != null && !string.IsNullOrEmpty(interactText)) textProperty.stringValue = interactText;

        SerializedProperty proximityProperty = so.FindProperty("proximity");
        if (proximityProperty != null) proximityProperty.floatValue = proximity;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(udonBehaviour);
        RecordPrefabModifications(udonBehaviour);
    }

    // =====================================================================
    // 4. Validate
    // =====================================================================

    [MenuItem(MenuRoot + "Validate Scene", false, 50)]
    public static void ValidateScene()
    {
        int errors = 0;

        ExhibitInteractable[] exhibits = Object.FindObjectsOfType<ExhibitInteractable>(true);

        // -----------------------------------------------------------------
        // Manager 구성 검사 (Scene 별)
        //  "Scene 당 1개" 가 규칙이므로 로드된 Scene 마다 따로 셉니다.
        //  Additive 로 3개 Scene 을 열고 각 Scene 에 1개씩 둔 정상 구성은 통과해야 합니다.
        // -----------------------------------------------------------------
        int totalManagers = 0;

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            List<ExhibitManager> managers = CollectManagersInScene(scene);
            totalManagers += managers.Count;

            int exhibitsInScene = 0;
            for (int i = 0; i < exhibits.Length; i++)
            {
                if (exhibits[i].gameObject.scene == scene) exhibitsInScene++;
            }

            if (managers.Count == 0)
            {
                // 작품이 없는 Scene(환경 전용 등)에는 Manager 가 없어도 정상입니다.
                if (exhibitsInScene > 0)
                {
                    Debug.LogError("[ExhibitDescriptor] Scene '" + scene.name + "' 에 작품이 " + exhibitsInScene +
                                   " 개 있는데 ExhibitManager 가 없습니다. 이 Scene 에도 1개를 만들어 주세요.");
                    errors++;
                }
                continue;
            }

            if (managers.Count > 1)
            {
                Debug.LogError("[ExhibitDescriptor] Scene '" + scene.name + "' 에 ExhibitManager 가 " + managers.Count +
                               " 개 있습니다. Scene 당 1개만 남기세요.", managers[0]);
                errors++;
                continue;
            }

            ExhibitManager manager = managers[0];

            if (!manager.gameObject.activeInHierarchy)
            {
                Debug.LogError("[ExhibitDescriptor] Scene '" + scene.name + "' 의 ExhibitManager 오브젝트가 비활성 상태입니다. " +
                               "Update 틱이 돌지 않아 Overlay 애니메이션이 생략됩니다.", manager);
                errors++;
            }
            // 오브젝트가 켜져 있어도 컴포넌트 체크가 꺼져 있으면 Update() 는 호출되지 않습니다.
            // ExhibitOverlay._ManagerCanTick() 도 이 경우를 "쓸 수 없는 Manager" 로 보고
            // Fade/Scale/스크롤을 전부 건너뛰므로, 런타임과 같은 기준으로 검사합니다.
            else if (!manager.enabled)
            {
                Debug.LogError("[ExhibitDescriptor] Scene '" + scene.name + "' 의 ExhibitManager 컴포넌트가 비활성(enabled 체크 해제) 상태입니다. " +
                               "Update 틱이 돌지 않아 Overlay 애니메이션이 생략됩니다.", manager);
                errors++;
            }
            else if (manager.gameObject.name != "ExhibitManager")
            {
                Debug.LogWarning("[ExhibitDescriptor] Scene '" + scene.name + "' 의 ExhibitManager 오브젝트 이름이 " +
                                 "'ExhibitManager' 가 아닙니다. 작품의 managerObjectName 과 일치시키거나 " +
                                 "manager 를 직접 연결하세요.", manager);
            }

            // Overlay 폰트 슬롯은 Manager 가 아니라 이 컴포넌트에 있습니다(Udon 화이트리스트 때문).
            // 옛 버전으로 만든 Scene 에는 없으므로 "폰트 미지정" 으로 동작합니다. 오류는 아닙니다.
            if (CollectSettingsInScene(scene).Count == 0)
            {
                Debug.LogWarning("[ExhibitDescriptor] Scene '" + scene.name + "' 의 ExhibitManager 에 " +
                                 "ExhibitDescriptorSettings 컴포넌트가 없어 Overlay 폰트를 지정할 수 없습니다. " +
                                 "(지금은 폰트 미지정 = TMP 기본 폰트) " +
                                 "Tools > Exhibit Descriptor > Setup All Exhibits In Scene 을 한 번 실행하면 " +
                                 "자동으로 붙습니다.", manager);
            }
        }

        if (totalManagers == 0 && exhibits.Length == 0)
        {
            Debug.LogError("[ExhibitDescriptor] 열려 있는 어떤 Scene 에도 ExhibitManager 가 없습니다.");
            errors++;
        }

        for (int i = 0; i < exhibits.Length; i++)
        {
            ExhibitInteractable exhibit = exhibits[i];
            string path = GetPath(exhibit.transform);

            SerializedObject so = new SerializedObject(exhibit);

            if (GetObject(so, "overlay") == null)
            {
                Debug.LogError("[ExhibitDescriptor] overlay 미연결: " + path, exhibit);
                errors++;
            }

            // manager 는 Scene 참조입니다. 다른 Scene 을 가리키면 그 Scene 이 언로드될 때
            // 언어 전환과 애니메이션 틱이 함께 끊깁니다.
            ExhibitManager assignedManager = GetObject(so, "manager") as ExhibitManager;
            if (assignedManager != null && assignedManager.gameObject.scene != exhibit.gameObject.scene)
            {
                Debug.LogError("[ExhibitDescriptor] manager 가 다른 Scene('" + assignedManager.gameObject.scene.name +
                               "') 의 ExhibitManager 를 가리킵니다: " + path, exhibit);
                errors++;
            }

            if (GetObject(so, "overlayAnchor") == null)
            {
                Debug.LogWarning("[ExhibitDescriptor] overlayAnchor 미연결(Overlay 현재 위치를 그대로 사용): " + path, exhibit);
            }

            // Placeholder 는 완전히 투명해서 Scene View 만 봐서는 교체를 잊은 것을 알 수 없습니다.
            // 일부러 비워 두는 경우도 있으므로 에러가 아니라 경고로만 알립니다.
            if (UsesPlaceholderMaterial(exhibit))
            {
                Debug.LogWarning("[ExhibitDescriptor] Artwork 가 아직 투명 Placeholder 입니다. " +
                                 "실제 작품 Mesh/Material 로 교체하세요: " + path, exhibit);
            }

            // Interact 는 Collider 와 UdonBehaviour 가 같은 GameObject 여야 안전합니다.
            bool selfInteractable = exhibit.GetComponent<Collider>() != null;

            ExhibitInteractRelay[] relays = exhibit.GetComponentsInChildren<ExhibitInteractRelay>(true);
            bool relayInteractable = false;

            for (int r = 0; r < relays.Length; r++)
            {
                if (relays[r].GetComponent<Collider>() != null) relayInteractable = true;

                SerializedObject relaySo = new SerializedObject(relays[r]);
                if (GetObject(relaySo, "target") == null)
                {
                    Debug.LogError("[ExhibitDescriptor] InteractRelay 의 target 이 비어 있습니다: " + GetPath(relays[r].transform), relays[r]);
                    errors++;
                }
            }

            if (!selfInteractable && !relayInteractable)
            {
                if (exhibit.GetComponentInChildren<Collider>(true) != null)
                {
                    Debug.LogError("[ExhibitDescriptor] Collider 가 자식에만 있고 그 GameObject 에 UdonBehaviour 가 없습니다. " +
                                   "해당 오브젝트에 ExhibitInteractRelay 를 붙이거나 Collider 를 작품 Root 로 옮기세요: " + path, exhibit);
                }
                else
                {
                    Debug.LogError("[ExhibitDescriptor] Interact 용 Collider 가 없습니다: " + path, exhibit);
                }
                errors++;
            }
        }

        ExhibitOverlay[] overlays = Object.FindObjectsOfType<ExhibitOverlay>(true);
        for (int i = 0; i < overlays.Length; i++)
        {
            ExhibitOverlay overlay = overlays[i];
            string path = GetPath(overlay.transform);
            SerializedObject so = new SerializedObject(overlay);

            if (GetObject(so, "canvasGroup") == null)
            {
                Debug.LogError("[ExhibitDescriptor] canvasGroup 미연결: " + path, overlay);
                errors++;
            }
            if (GetObject(so, "descriptionText") == null)
            {
                Debug.LogError("[ExhibitDescriptor] descriptionText 미연결: " + path, overlay);
                errors++;
            }
            if (GetObject(so, "scrollViewport") == null || GetObject(so, "scrollContent") == null)
            {
                Debug.LogWarning("[ExhibitDescriptor] scrollViewport / scrollContent 미연결 (스크롤 비활성): " + path, overlay);
            }

            Canvas canvas = overlay.GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.WorldSpace)
            {
                Debug.LogError("[ExhibitDescriptor] Canvas Render Mode 가 World Space 가 아닙니다: " + path, overlay);
                errors++;
            }

            if (overlay.gameObject.activeSelf)
            {
                Debug.LogWarning("[ExhibitDescriptor] Overlay 가 활성 상태로 저장되어 있습니다. 비활성으로 저장하는 것을 권장합니다: " + path, overlay);
            }
        }

        ExhibitOverlayButton[] buttons = Object.FindObjectsOfType<ExhibitOverlayButton>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            ExhibitOverlayButton button = buttons[i];
            if (button.GetComponent<Collider>() == null)
            {
                Debug.LogError("[ExhibitDescriptor] 버튼에 Collider 가 없습니다: " + GetPath(button.transform), button);
                errors++;
            }
            if (button.gameObject.layer != 0)
            {
                Debug.LogWarning("[ExhibitDescriptor] 버튼 Layer 가 Default 가 아닙니다(Interact 실패 가능): " + GetPath(button.transform), button);
            }
        }

        ExhibitLanguageSwitch[] switches = Object.FindObjectsOfType<ExhibitLanguageSwitch>(true);
        for (int i = 0; i < switches.Length; i++)
        {
            ExhibitLanguageSwitch languageSwitch = switches[i];
            string path = GetPath(languageSwitch.transform);
            SerializedObject so = new SerializedObject(languageSwitch);

            if (languageSwitch.GetComponent<Collider>() == null)
            {
                Debug.LogError("[ExhibitDescriptor] 언어 전환 버튼에 Collider 가 없습니다: " + path, languageSwitch);
                errors++;
            }

            ExhibitManager assignedManager = GetObject(so, "manager") as ExhibitManager;
            if (assignedManager != null && assignedManager.gameObject.scene != languageSwitch.gameObject.scene)
            {
                Debug.LogError("[ExhibitDescriptor] 언어 전환 버튼의 manager 가 다른 Scene('" +
                               assignedManager.gameObject.scene.name + "') 을 가리킵니다: " + path, languageSwitch);
                errors++;
            }
            else if (assignedManager == null && CollectManagersInScene(languageSwitch.gameObject.scene).Count == 0)
            {
                Debug.LogWarning("[ExhibitDescriptor] 언어 전환 버튼과 같은 Scene 에 ExhibitManager 가 없습니다: " + path, languageSwitch);
            }
        }

        // 폰트에 글리프가 없으면 참조가 전부 맞아도 화면에는 □ 만 뜹니다.
        ValidateFontGlyphs(overlays, switches);

        if (errors == 0) Debug.Log("[ExhibitDescriptor] Validate 통과. 작품 " + exhibits.Length + " 개.");
        else Debug.LogError("[ExhibitDescriptor] Validate 실패: 오류 " + errors + " 건.");
    }

    // 폰트 검사용 표본 문자
    //  KR: 이 패키지는 titleKR / descriptionKR / 기본 Interact 문구가 한국어인 KR 우선 패키지입니다.
    //  JP: 언어 전환 버튼 라벨이 "日本語" 이고 JP 데이터도 함께 다룹니다.
    //  기호: 도구가 만드는 버튼 라벨(× ▲ ▼). 사용자가 폰트를 바꿨을 때 여기서 깨지는지 봅니다.
    private const string GlyphProbeKR = "한글작품설명";
    private const string GlyphProbeJP = "日本語説明";
    private const string GlyphProbeSymbol = "×▲▼";

    /// <summary>
    /// Overlay / 언어 전환 버튼이 실제로 쓰는 TMP 폰트에 한글·일본어·버튼 기호 글리프가 있는지 봅니다.
    ///
    /// 폰트를 지정하지 않으면 TMP 기본값(LiberationSans SDF)이 쓰이는데, 여기에는 한글도 일본어도
    /// 없어 설명이 통째로 □ 로 보입니다. 참조 검사만으로는 절대 드러나지 않는 문제라 함께 봅니다.
    /// 같은 경고를 100번 찍지 않도록 <b>폰트 1개당 1번만</b> 보고합니다.
    /// </summary>
    private static void ValidateFontGlyphs(ExhibitOverlay[] overlays, ExhibitLanguageSwitch[] switches)
    {
        List<TMP_Text> texts = new List<TMP_Text>();
        for (int i = 0; i < overlays.Length; i++) texts.AddRange(overlays[i].GetComponentsInChildren<TMP_Text>(true));
        for (int i = 0; i < switches.Length; i++) texts.AddRange(switches[i].GetComponentsInChildren<TMP_Text>(true));

        List<TMP_FontAsset> reported = new List<TMP_FontAsset>();
        bool reportedMissingAsset = false;

        for (int i = 0; i < texts.Count; i++)
        {
            TMP_Text text = texts[i];
            if (text == null) continue;

            // font 가 비어 있으면 TMP 가 기본 폰트로 그립니다. 검사도 같은 기준으로 합니다.
            TMP_FontAsset font = text.font != null ? text.font : TMP_Settings.defaultFontAsset;

            if (font == null)
            {
                if (reportedMissingAsset) continue;
                reportedMissingAsset = true;

                Debug.LogWarning("[ExhibitDescriptor] TMP 폰트가 지정되지 않았고 TMP 기본 폰트도 없습니다. " +
                                 "Window > TextMeshPro > Import TMP Essential Resources 를 먼저 실행하세요: " +
                                 GetPath(text.transform), text);
                continue;
            }

            if (reported.Contains(font)) continue;
            reported.Add(font);

            string missingKR = FindMissingGlyphs(font, GlyphProbeKR);
            string missingJP = FindMissingGlyphs(font, GlyphProbeJP);
            string missingSymbol = FindMissingGlyphs(font, GlyphProbeSymbol);

            if (missingKR.Length == 0 && missingJP.Length == 0 && missingSymbol.Length == 0) continue;

            string message = "[ExhibitDescriptor] 폰트 '" + font.name + "' 에 없는 글자가 있어 □ 로 표시됩니다.";
            if (missingKR.Length > 0) message += "\n - 지정한 폰트에 한글 글리프가 없습니다: " + missingKR;
            if (missingJP.Length > 0) message += "\n - 일본어 글리프가 없습니다: " + missingJP;
            if (missingSymbol.Length > 0) message += "\n - 버튼 라벨 기호가 없습니다: " + missingSymbol;

            message += "\nCJK 글리프를 포함한 TMP Font Asset 을 만들어 ExhibitManager 의 " +
                       "ExhibitDescriptorSettings > 'Overlay Font' 에 지정하고 " +
                       "Tools > Exhibit Descriptor > Setup All Exhibits In Scene 을 다시 실행하세요. " +
                       "(만드는 방법은 README 의 'CJK 폰트 준비' 참고)" +
                       "\n예: " + GetPath(text.transform);

            Debug.LogWarning(message, text);
        }
    }

    /// <summary>
    /// <paramref name="probe"/> 의 글자 중 폰트에 없는 것만 모아 돌려줍니다. (없으면 빈 문자열)
    ///
    /// fallback 까지 함께 봅니다. TMP 기본 폰트는 ▲▼ 를 본체에 갖고 있지 않지만
    /// 'LiberationSans SDF - Fallback' 이 대신 그려 주므로 실제로는 정상 표시됩니다.
    /// 없는 글자를 아틀라스에 새로 추가하지는 않습니다(tryAddCharacter = false). 검사만 해야 하니까요.
    /// </summary>
    private static string FindMissingGlyphs(TMP_FontAsset font, string probe)
    {
        if (font == null) return probe;

        string missing = "";
        for (int i = 0; i < probe.Length; i++)
        {
            if (font.HasCharacter(probe[i], true, false)) continue;
            missing += probe[i];
        }

        return missing;
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent != null ? parent.gameObject.layer : 0;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.localPosition = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        return go;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// TMP 텍스트의 공통 설정입니다. <b>font(TMP_FontAsset)는 여기서 정하지 않습니다.</b>
    ///
    /// 폰트는 Scene 의 <see cref="ExhibitDescriptorSettings.overlayFont"/> 에서 가져와
    /// <see cref="ApplyOverlayFont"/> 가 Setup 단계에 적용합니다. 생성 시점의 이 함수는
    /// 아직 어느 Scene 의 Manager 에 붙을지 모르고, 이미 만들어 둔 작품에도 나중에 폰트를
    /// 바꿔 끼울 수 있어야 하기 때문입니다.
    /// (지정하지 않으면 TMP 기본 폰트가 쓰이는데 한글/일본어가 □ 로 보입니다 - Validate 가 경고합니다)
    /// </summary>
    private static void ConfigureText(TextMeshProUGUI text, float fontSize, FontStyles style, TextAlignmentOptions alignment)
    {
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = Color.white;
        text.enableWordWrapping = true;
        text.enableAutoSizing = false;
        text.raycastTarget = false;
        text.richText = true;
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent == null) return null;
        if (parent.name == name) return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildRecursive(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static TextMeshProUGUI FindText(Transform root, string name)
    {
        Transform target = FindChildRecursive(root, name);
        return target != null ? target.GetComponent<TextMeshProUGUI>() : null;
    }

    private static RectTransform FindRect(Transform root, string name)
    {
        Transform target = FindChildRecursive(root, name);
        return target != null ? target.GetComponent<RectTransform>() : null;
    }

    private static void AssignIfEmpty(SerializedObject so, string propertyName, Object value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null) return;
        if (property.objectReferenceValue != null) return;
        if (value == null) return;
        property.objectReferenceValue = value;
    }

    private static Object GetObject(SerializedObject so, string propertyName)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        return property != null ? property.objectReferenceValue : null;
    }

    private static float GetFloat(SerializedObject so, string propertyName, float fallback)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        return property != null ? property.floatValue : fallback;
    }

    private static string GetString(SerializedObject so, string propertyName, string fallback)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        return property != null ? property.stringValue : fallback;
    }

    private static void SetString(SerializedObject so, string propertyName, string value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property != null) property.stringValue = value;
    }

    private static void SetEnum(SerializedObject so, string propertyName, int value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property != null) property.enumValueIndex = value;
    }

    private static void RecordPrefabModifications(Component component)
    {
        if (component == null) return;
        if (!PrefabUtility.IsPartOfPrefabInstance(component)) return;
        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
    }

    /// <summary>
    /// 부모의 Scale 을 상쇄해 Exhibit Root 의 **월드 Scale 을 (1,1,1)** 로 맞춥니다.
    ///
    /// <c>localScale = 1</c> 로 두면 부모의 Scale 이 그대로 상속됩니다. 그러면 작품의 월드 위치는
    /// 맞더라도 World Space Canvas(<see cref="CanvasScale"/> = 0.001)와 InteractionArea 까지 함께
    /// 커지거나 작아지고, 부모가 비균일 Scale 이면 Panel 의 글자가 찌그러집니다.
    /// Overlay 크기와 여백(<c>OverlayGap</c>, <c>InteractionPadding</c>)은 전부 m 단위 상수라
    /// Root 의 월드 Scale 이 1 이어야 의도한 크기가 나옵니다.
    /// </summary>
    private static void NeutralizeWorldScale(Transform exhibitRoot)
    {
        Transform parent = exhibitRoot.parent;
        if (parent == null)
        {
            exhibitRoot.localScale = Vector3.one;
            return;
        }

        Vector3 parentScale = parent.lossyScale;

        // Scale 0 은 역수가 없습니다. 부모가 이미 납작하게 눌러 놓은 상태라
        // 도구가 상쇄할 방법이 없으므로 1 로 두고 사람이 고치도록 알립니다.
        // (Mathf.Approximately 는 상대 오차라 0 비교에는 쓰지 않습니다)
        if (IsDegenerateScale(parentScale.x) ||
            IsDegenerateScale(parentScale.y) ||
            IsDegenerateScale(parentScale.z))
        {
            exhibitRoot.localScale = Vector3.one;
            Debug.LogWarning("[ExhibitDescriptor] 부모의 Scale 에 0 이 있어 Exhibit 의 월드 크기를 보존할 수 없습니다: " +
                             GetPath(parent) + " (lossyScale " + parentScale + ")\n" +
                             "부모 Scale 을 고친 뒤 다시 실행하세요.", parent);
            return;
        }

        exhibitRoot.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);

        // 비균일 Scale 위/아래에 회전이 섞이면 실제 행렬에 shear 가 들어가고,
        // 그때 lossyScale 은 근사값이라 역수만으로 완전히 상쇄되지 않습니다.
        if (!IsUniformScale(parentScale) && HasRotationInParents(parent))
        {
            Debug.LogWarning("[ExhibitDescriptor] 부모 계층에 회전 + 비균일 Scale 이 섞여 있어 " +
                             "Exhibit 의 월드 크기를 정확히 보존하지 못할 수 있습니다: " + GetPath(parent) +
                             " (lossyScale " + parentScale + ")\n" +
                             "Overlay 가 찌그러져 보이면 부모 Scale 을 균일하게 맞추세요.", parent);
        }
    }

    /// <summary>역수를 취하면 값이 폭주하는 Scale 인지. (0 또는 사실상 0)</summary>
    private static bool IsDegenerateScale(float value)
    {
        return Mathf.Abs(value) < 1e-5f;
    }

    private static bool IsUniformScale(Vector3 scale)
    {
        return Mathf.Approximately(scale.x, scale.y) && Mathf.Approximately(scale.y, scale.z);
    }

    private static bool HasRotationInParents(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.localRotation != Quaternion.identity) return true;
            current = current.parent;
        }
        return false;
    }

    private static string GetPath(Transform transform)
    {
        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }

    private static void MarkSceneDirty()
    {
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    /// <summary>
    /// 해당 오브젝트가 속한 Scene 을 Dirty 로 표시합니다.
    /// Additive 로 여러 Scene 을 열어 둔 상태에서 활성 Scene 만 표시하면
    /// 다른 Scene 의 변경 사항이 저장되지 않은 채 사라질 수 있습니다.
    /// </summary>
    private static void MarkSceneDirtyFor(GameObject target)
    {
        if (target == null) return;

        Scene scene = target.scene;
        if (!IsLoadedScene(scene)) return; // Prefab Asset / Preview Scene 은 SetDirty 로 충분합니다.

        EditorSceneManager.MarkSceneDirty(scene);
    }
}
#endif
