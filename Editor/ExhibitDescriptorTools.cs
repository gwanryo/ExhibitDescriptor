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
///   - Create Exhibition Root            : ExhibitionRoot + ExhibitManager 생성
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
        anchor.transform.localRotation = Quaternion.identity;

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
        CreateButton(panel.transform, "ScrollUpButton", "▲", new Vector2(0f, 0f), new Vector2(28f, 24f), new Vector2(64f, 60f));
        CreateButton(panel.transform, "ScrollDownButton", "▼", new Vector2(0f, 0f), new Vector2(100f, 24f), new Vector2(64f, 60f));
        CreateButton(panel.transform, "CloseButton", "✕", new Vector2(1f, 0f), new Vector2(-28f, 24f), new Vector2(140f, 60f));

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
        EditorUtility.SetDirty(overlay);
        UdonSharpEditorUtility.CopyProxyToUdon(overlay);
        RecordPrefabModifications(overlay);
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

        if (errors == 0) Debug.Log("[ExhibitDescriptor] Validate 통과. 작품 " + exhibits.Length + " 개.");
        else Debug.LogError("[ExhibitDescriptor] Validate 실패: 오류 " + errors + " 건.");
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
