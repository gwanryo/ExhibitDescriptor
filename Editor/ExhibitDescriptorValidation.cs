#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using TMPro;

/// <summary>검사 결과의 심각도. 오류가 하나라도 있으면 그 Scene 은 정상 동작하지 않습니다.</summary>
public enum ExhibitFindingSeverity
{
    Error,
    Warning
}

/// <summary>
/// Scene 검사가 찾아낸 한 건. <b>표시 방법을 모릅니다</b> — 콘솔에 찍는 것도, 창에 그리는 것도
/// 이 구조체를 받는 쪽의 일입니다.
///
/// 이렇게 나눈 이유: 예전에는 검사와 <c>Debug.LogWarning</c> 이 서른 곳 넘게 붙어 있어서
/// <see cref="ExhibitDescriptorWindow"/> 가 결과를 쓸 방법이 없었습니다. 창을 위해 검사를
/// 한 번 더 구현하면 두 판본이 갈라지므로, 수집을 한 곳으로 모으고 표시만 둘로 나눕니다.
/// </summary>
public struct ExhibitFinding
{
    public ExhibitFindingSeverity severity;

    /// <summary>묶음 키. 같은 <c>code</c> 는 창에서 한 항목으로 접힙니다. (작품 100개 × 같은 문제 = 한 줄)</summary>
    public string code;

    /// <summary>무엇이 잘못되었는지. 같은 <c>code</c> 안에서는 모든 건이 같은 문장이어야 합니다.</summary>
    public string title;

    /// <summary>이 건에만 해당하는 것 — 경로, 측정값. 창에서는 한 줄로, 콘솔에서는 뒤에 붙습니다.</summary>
    public string detail;

    /// <summary>어떻게 고치는지. 비어 있어도 됩니다.</summary>
    public string advice;

    /// <summary>클릭하면 Hierarchy 에서 선택할 오브젝트. Scene 전체에 대한 건이면 null 입니다.</summary>
    public Object target;
}

public static partial class ExhibitDescriptorTools
{
    // findings 의 묶음 키. 문자열 리터럴을 여기 모아 두면 창과 검사가 같은 값을 씁니다.
    private const string CodeManagerMissing = "manager.missing";
    private const string CodeManagerDuplicate = "manager.duplicate";
    private const string CodeManagerInactiveObject = "manager.inactiveObject";
    private const string CodeManagerDisabled = "manager.disabledComponent";
    private const string CodeManagerName = "manager.name";
    private const string CodeSettingsMissing = "manager.settingsMissing";
    private const string CodeManagerNoneAnywhere = "manager.noneAnywhere";
    private const string CodeExhibitOverlayUnlinked = "exhibit.overlayUnlinked";
    private const string CodeExhibitCrossScene = "exhibit.crossSceneManager";
    private const string CodeExhibitPlaceholder = "exhibit.placeholder";
    private const string CodeIconMissing = "icon.missing";
    private const string CodeIconNoCollider = "icon.noCollider";
    private const string CodeIconNotWorldSpace = "icon.notWorldSpace";
    private const string CodeIconNoTarget = "icon.noTarget";
    private const string CodeIconNoCanvasGroup = "icon.noCanvasGroup";
    private const string CodeGeometryEmpty = "exhibit.geometryEmpty";
    private const string CodeIconActiveSaved = "icon.activeSaved";
    private const string CodeCenterInsideCollider = "panel.centerInsideCollider";
    private const string CodePanelNoDepth = "panel.noDepthRoom";
    private const string CodePanelNoSideways = "panel.noSidewaysRoom";
    private const string CodeStrayCollider = "exhibit.strayCollider";
    private const string CodeOverlayCanvasGroup = "overlay.canvasGroupUnlinked";
    private const string CodeOverlayDescription = "overlay.descriptionUnlinked";
    private const string CodeOverlayScroll = "overlay.scrollUnlinked";
    private const string CodeOverlayNotWorldSpace = "overlay.notWorldSpace";
    private const string CodeOverlayActiveSaved = "overlay.activeSaved";
    private const string CodeButtonNoCollider = "button.noCollider";
    private const string CodeButtonLayer = "button.layer";
    private const string CodeButtonInsidePanel = "button.insidePanel";
    private const string CodeSwitchNoCollider = "switch.noCollider";
    private const string CodeSwitchCrossScene = "switch.crossSceneManager";
    private const string CodeSwitchNoManager = "switch.noManagerInScene";
    private const string CodeFontNoAsset = "font.noAsset";
    private const string CodeFontMissingKR = "font.missingKR";
    private const string CodeFontMissingJP = "font.missingJP";
    private const string CodeFontMissingSymbol = "font.missingSymbol";

    /// <summary>도구가 권하는 Setup 메뉴의 경로. 문구를 한 곳에서 고치기 위해 상수로 둡니다.</summary>
    private const string SetupAllMenuHint = "Tools > Exhibit Descriptor > Setup > All Exhibits In Scene";

    // =====================================================================
    // 4. Validate — 메뉴 (콘솔 판본)
    // =====================================================================

    /// <summary>
    /// 열려 있는 Scene 을 검사해 결과를 콘솔에 찍습니다.
    ///
    /// <see cref="CollectFindings"/> 가 찾은 것을 그대로 옮기기만 합니다. <c>target</c> 을 context 로
    /// 넘기므로 콘솔 줄을 클릭하면 해당 오브젝트가 Hierarchy 에서 선택되는 동작이 유지됩니다.
    /// </summary>
    [MenuItem(MenuRoot + "Validate Scene", false, 50)]
    public static void ValidateScene()
    {
        List<ExhibitFinding> findings = CollectFindings();

        int errors = 0;
        for (int i = 0; i < findings.Count; i++)
        {
            ExhibitFinding finding = findings[i];
            string message = ComposeFindingMessage(finding);

            if (finding.severity == ExhibitFindingSeverity.Error)
            {
                errors++;
                Debug.LogError(message, finding.target);
            }
            else
            {
                Debug.LogWarning(message, finding.target);
            }
        }

        int exhibitCount = Object.FindObjectsOfType<ExhibitInteractable>(true).Length;

        if (errors == 0) Debug.Log("[ExhibitDescriptor] Validate 통과. 작품 " + exhibitCount + " 개.");
        else Debug.LogError("[ExhibitDescriptor] Validate 실패: 오류 " + errors + " 건.");
    }

    /// <summary>한 건을 콘솔 한 줄로 만듭니다. (제목 · 조치 · 대상 순서)</summary>
    private static string ComposeFindingMessage(ExhibitFinding finding)
    {
        string message = "[ExhibitDescriptor] " + finding.title;

        if (!string.IsNullOrEmpty(finding.advice)) message += " " + finding.advice;
        if (!string.IsNullOrEmpty(finding.detail)) message += ": " + finding.detail;

        return message;
    }

    // =====================================================================
    // 4.1. Validate — 수집
    // =====================================================================

    /// <summary>
    /// 열려 있는 모든 Scene 을 검사해 찾은 것을 돌려줍니다. <b>콘솔에 아무것도 찍지 않습니다.</b>
    /// </summary>
    internal static List<ExhibitFinding> CollectFindings()
    {
        List<ExhibitFinding> findings = new List<ExhibitFinding>();

        ExhibitInteractable[] exhibits = Object.FindObjectsOfType<ExhibitInteractable>(true);
        ExhibitOverlay[] overlays = Object.FindObjectsOfType<ExhibitOverlay>(true);
        ExhibitOverlayButton[] buttons = Object.FindObjectsOfType<ExhibitOverlayButton>(true);
        ExhibitLanguageSwitch[] switches = Object.FindObjectsOfType<ExhibitLanguageSwitch>(true);

        CollectManagerFindings(findings, exhibits);

        for (int i = 0; i < exhibits.Length; i++) CollectExhibitFindings(findings, exhibits[i]);
        for (int i = 0; i < overlays.Length; i++) CollectOverlayFindings(findings, overlays[i]);
        for (int i = 0; i < buttons.Length; i++) CollectButtonFindings(findings, buttons[i]);
        for (int i = 0; i < switches.Length; i++) CollectLanguageSwitchFindings(findings, switches[i]);

        // 폰트에 글리프가 없으면 참조가 전부 맞아도 화면에는 □ 만 뜹니다.
        CollectFontFindings(findings, overlays, switches);

        return findings;
    }

    /// <summary>
    /// 같은 문제를 가진 여러 건을 하나로 접은 묶음. 창이 그리는 단위입니다.
    /// </summary>
    internal struct ExhibitFindingGroup
    {
        public ExhibitFindingSeverity severity;
        public string code;
        public string title;
        public string advice;

        /// <summary>이 묶음에 속한 건들. 창에서는 한 줄씩 [선택] 버튼과 함께 나옵니다.</summary>
        public List<ExhibitFinding> items;
    }

    /// <summary>
    /// findings 를 <c>code</c> 로 접습니다. 오류 묶음이 먼저, 그 안에서는 처음 나온 순서를 지킵니다.
    ///
    /// 왜 필요한가: 작품 100개가 같은 폰트를 쓰면 "한글 글리프 없음" 이 100건 나옵니다. 그대로
    /// 나열하면 나머지 문제가 스크롤 밖으로 밀려나 안 보입니다. 콘솔이 못 하는 일이 이것입니다.
    /// </summary>
    internal static List<ExhibitFindingGroup> GroupFindings(List<ExhibitFinding> findings)
    {
        List<ExhibitFindingGroup> groups = new List<ExhibitFindingGroup>();
        if (findings == null) return groups;

        Dictionary<string, int> indexByCode = new Dictionary<string, int>();

        for (int i = 0; i < findings.Count; i++)
        {
            ExhibitFinding finding = findings[i];
            string key = finding.code != null ? finding.code : "";

            int index;
            if (!indexByCode.TryGetValue(key, out index))
            {
                ExhibitFindingGroup group = new ExhibitFindingGroup();
                group.severity = finding.severity;
                group.code = key;
                group.title = finding.title;
                group.advice = finding.advice;
                group.items = new List<ExhibitFinding>();

                index = groups.Count;
                indexByCode.Add(key, index);
                groups.Add(group);
            }

            // groups[index] 는 struct 의 사본을 돌려주지만, items 는 참조 타입이라 사본을 거쳐도
            // 같은 List 를 가리킵니다. 그래서 이 Add 는 원본에 들어갑니다.
            // (severity/code 같은 값 필드를 여기서 고치려 하면 조용히 버려집니다 — 그래서 그것들은
            //  묶음을 만들 때 한 번만 정합니다)
            groups[index].items.Add(finding);
        }

        // 오류를 앞으로. List.Sort 는 안정 정렬이 아니므로 직접 두 번 훑어 순서를 보존합니다.
        List<ExhibitFindingGroup> ordered = new List<ExhibitFindingGroup>();
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].severity == ExhibitFindingSeverity.Error) ordered.Add(groups[i]);
        }
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].severity != ExhibitFindingSeverity.Error) ordered.Add(groups[i]);
        }

        return ordered;
    }

    /// <summary>findings 안의 오류 건수. (묶음 수가 아니라 건수입니다)</summary>
    internal static int CountBySeverity(List<ExhibitFinding> findings, ExhibitFindingSeverity severity)
    {
        if (findings == null) return 0;

        int count = 0;
        for (int i = 0; i < findings.Count; i++)
        {
            if (findings[i].severity == severity) count++;
        }

        return count;
    }

    private static void Add(List<ExhibitFinding> findings, ExhibitFindingSeverity severity, string code,
                            string title, string detail, string advice, Object target)
    {
        ExhibitFinding finding = new ExhibitFinding();
        finding.severity = severity;
        finding.code = code;
        finding.title = title;
        finding.detail = detail;
        finding.advice = advice;
        finding.target = target;

        findings.Add(finding);
    }

    private static void AddError(List<ExhibitFinding> findings, string code, string title, string detail,
                                 string advice, Object target)
    {
        Add(findings, ExhibitFindingSeverity.Error, code, title, detail, advice, target);
    }

    private static void AddWarning(List<ExhibitFinding> findings, string code, string title, string detail,
                                   string advice, Object target)
    {
        Add(findings, ExhibitFindingSeverity.Warning, code, title, detail, advice, target);
    }

    /// <summary>
    /// Manager 구성 검사 (Scene 별).
    ///
    /// "Scene 당 1개" 가 규칙이므로 로드된 Scene 마다 따로 셉니다. Additive 로 3개 Scene 을 열고
    /// 각 Scene 에 1개씩 둔 정상 구성은 통과해야 합니다.
    /// </summary>
    private static void CollectManagerFindings(List<ExhibitFinding> findings, ExhibitInteractable[] exhibits)
    {
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
                    AddError(findings, CodeManagerMissing,
                             "Scene '" + scene.name + "' 에 작품이 " + exhibitsInScene +
                             " 개 있는데 ExhibitManager 가 없습니다.",
                             null,
                             "이 Scene 에도 1개를 만들어 주세요.",
                             null);
                }
                continue;
            }

            if (managers.Count > 1)
            {
                AddError(findings, CodeManagerDuplicate,
                         "Scene '" + scene.name + "' 에 ExhibitManager 가 " + managers.Count + " 개 있습니다.",
                         null,
                         "Scene 당 1개만 남기세요.",
                         managers[0]);
                continue;
            }

            ExhibitManager manager = managers[0];

            if (!manager.gameObject.activeInHierarchy)
            {
                AddError(findings, CodeManagerInactiveObject,
                         "Scene '" + scene.name + "' 의 ExhibitManager 오브젝트가 비활성 상태입니다.",
                         null,
                         "Update 틱이 돌지 않아 Overlay 애니메이션이 생략됩니다.",
                         manager);
            }
            // 오브젝트가 켜져 있어도 컴포넌트 체크가 꺼져 있으면 Update() 는 호출되지 않습니다.
            // ExhibitOverlay._ManagerCanTick() 도 이 경우를 "쓸 수 없는 Manager" 로 보고
            // Fade/Scale/스크롤을 전부 건너뛰므로, 런타임과 같은 기준으로 검사합니다.
            else if (!manager.enabled)
            {
                AddError(findings, CodeManagerDisabled,
                         "Scene '" + scene.name + "' 의 ExhibitManager 컴포넌트가 비활성(enabled 체크 해제) 상태입니다.",
                         null,
                         "Update 틱이 돌지 않아 Overlay 애니메이션이 생략됩니다.",
                         manager);
            }
            else if (manager.gameObject.name != "ExhibitManager")
            {
                AddWarning(findings, CodeManagerName,
                           "Scene '" + scene.name + "' 의 ExhibitManager 오브젝트 이름이 'ExhibitManager' 가 아닙니다.",
                           null,
                           "작품의 managerObjectName 과 일치시키거나 manager 를 직접 연결하세요.",
                           manager);
            }

            // Overlay 폰트 슬롯은 Manager 가 아니라 이 컴포넌트에 있습니다(Udon 화이트리스트 때문).
            // 옛 버전으로 만든 Scene 에는 없으므로 "폰트 미지정" 으로 동작합니다. 오류는 아닙니다.
            if (CollectSettingsInScene(scene).Count == 0)
            {
                AddWarning(findings, CodeSettingsMissing,
                           "Scene '" + scene.name + "' 의 ExhibitManager 에 ExhibitDescriptorSettings 컴포넌트가 없어 " +
                           "Overlay 폰트를 지정할 수 없습니다.",
                           null,
                           "(지금은 폰트 미지정 = TMP 기본 폰트) " + SetupAllMenuHint +
                           " 을 한 번 실행하면 자동으로 붙습니다.",
                           manager);
            }
        }

        if (totalManagers == 0 && exhibits.Length == 0)
        {
            AddError(findings, CodeManagerNoneAnywhere,
                     "열려 있는 어떤 Scene 에도 ExhibitManager 가 없습니다.",
                     null, null, null);
        }
    }

    private static void CollectExhibitFindings(List<ExhibitFinding> findings, ExhibitInteractable exhibit)
    {
        string path = GetPath(exhibit.transform);
        SerializedObject so = new SerializedObject(exhibit);

        if (GetObject(so, "overlay") == null)
        {
            AddError(findings, CodeExhibitOverlayUnlinked, "overlay 미연결", path, null, exhibit);
        }

        // manager 는 Scene 참조입니다. 다른 Scene 을 가리키면 그 Scene 이 언로드될 때
        // 언어 전환과 애니메이션 틱이 함께 끊깁니다.
        ExhibitManager assignedManager = GetObject(so, "manager") as ExhibitManager;
        if (assignedManager != null && assignedManager.gameObject.scene != exhibit.gameObject.scene)
        {
            AddError(findings, CodeExhibitCrossScene,
                     "manager 가 다른 Scene('" + assignedManager.gameObject.scene.name +
                     "') 의 ExhibitManager 를 가리킵니다.",
                     path, null, exhibit);
        }

        // Placeholder 는 완전히 투명해서 Scene View 만 봐서는 교체를 잊은 것을 알 수 없습니다.
        // 일부러 비워 두는 경우도 있으므로 에러가 아니라 경고로만 알립니다.
        if (UsesPlaceholderMaterial(exhibit))
        {
            AddWarning(findings, CodeExhibitPlaceholder,
                       "Artwork 가 아직 투명 Placeholder 입니다.",
                       path,
                       "실제 작품 Mesh/Material 로 교체하세요.",
                       exhibit);
        }

        // ---------------------------------------------------------------
        // Interact 수단 검사. 판정은 ⓘ 아이콘 하나만 받습니다.
        // (작품 정면에는 판정 영역이 없어야 정상입니다 - 그게 이 설계의 요점입니다)
        // ---------------------------------------------------------------
        ExhibitInfoIcon icon = exhibit.GetComponentInChildren<ExhibitInfoIcon>(true);

        if (icon == null)
        {
            AddError(findings, CodeIconMissing,
                     "ⓘ 아이콘이 없어 설명을 열 방법이 없습니다.",
                     path,
                     SetupAllMenuHint + " 을 실행하면 만들어집니다.",
                     exhibit);
        }
        else
        {
            CollectIconFindings(findings, exhibit, icon, so, path);
        }

        // 작품 정면을 덮는 Collider 는 감상을 방해합니다.
        Collider[] strayColliders = exhibit.GetComponentsInChildren<Collider>(true);
        for (int c = 0; c < strayColliders.Length; c++)
        {
            Collider stray = strayColliders[c];
            if (stray == null) continue;
            if (icon != null && stray.transform.IsChildOf(icon.transform)) continue;
            if (stray.GetComponentInParent<ExhibitOverlay>(true) != null) continue;   // Overlay 버튼

            AddWarning(findings, CodeStrayCollider,
                       "작품 안에 아이콘/Overlay 가 아닌 Collider 가 있습니다.",
                       GetPath(stray.transform),
                       "Interact 레이가 여기에 먼저 막히면 아이콘을 클릭할 수 없습니다.",
                       stray);
        }
    }

    private static void CollectIconFindings(List<ExhibitFinding> findings, ExhibitInteractable exhibit,
                                            ExhibitInfoIcon icon, SerializedObject so, string path)
    {
        string iconPath = GetPath(icon.transform);

        if (icon.GetComponent<Collider>() == null)
        {
            AddError(findings, CodeIconNoCollider,
                     "ⓘ 아이콘에 Collider 가 없어 Interact 할 수 없습니다.", iconPath, null, icon);
        }

        Canvas iconCanvas = icon.GetComponent<Canvas>();
        if (iconCanvas == null || iconCanvas.renderMode != RenderMode.WorldSpace)
        {
            AddError(findings, CodeIconNotWorldSpace,
                     "ⓘ 아이콘이 World Space Canvas 가 아닙니다.", iconPath, null, icon);
        }

        SerializedObject iconSo = new SerializedObject(icon);
        if (GetObject(iconSo, "target") == null)
        {
            AddError(findings, CodeIconNoTarget, "ⓘ 아이콘의 target 이 비어 있습니다.", iconPath, null, icon);
        }
        if (GetObject(iconSo, "canvasGroup") == null)
        {
            AddWarning(findings, CodeIconNoCanvasGroup,
                       "ⓘ 아이콘에 CanvasGroup 이 없어 페이드가 생략됩니다.", iconPath, null, icon);
        }

        // 기하가 0 이면 런타임이 아이콘 로직을 건너뜁니다 = 아이콘이 영영 뜨지 않습니다.
        SerializedProperty extentsProperty = so.FindProperty("boundsExtentsLocal");
        if (extentsProperty == null || extentsProperty.vector3Value.sqrMagnitude <= 0f)
        {
            AddError(findings, CodeGeometryEmpty,
                     "기하 정보가 비어 있어 아이콘이 뜨지 않습니다.",
                     path,
                     "작품 Mesh(Renderer)가 있는지 확인하고 Setup 을 실행하세요.",
                     exhibit);
        }

        if (icon.gameObject.activeSelf)
        {
            AddWarning(findings, CodeIconActiveSaved,
                       "ⓘ 아이콘이 활성 상태로 저장되어 있습니다.",
                       iconPath,
                       "비활성으로 저장하는 것을 권장합니다.",
                       icon);
        }

        // 저작 시점에 "여기엔 Panel 이 들어갈 자리가 없다" 를 알려 줍니다.
        // 런타임은 최대한 앞으로 클램프하지만, 그 상태를 조용히 두면 왜 잠기는지 알 수 없습니다.
        CollectPanelDepthFindings(findings, exhibit, path);
        CollectPanelSidewaysFindings(findings, exhibit, path);
    }

    private static void CollectOverlayFindings(List<ExhibitFinding> findings, ExhibitOverlay overlay)
    {
        string path = GetPath(overlay.transform);
        SerializedObject so = new SerializedObject(overlay);

        if (GetObject(so, "canvasGroup") == null)
        {
            AddError(findings, CodeOverlayCanvasGroup, "canvasGroup 미연결", path, null, overlay);
        }
        if (GetObject(so, "descriptionText") == null)
        {
            AddError(findings, CodeOverlayDescription, "descriptionText 미연결", path, null, overlay);
        }
        if (GetObject(so, "scrollViewport") == null || GetObject(so, "scrollContent") == null)
        {
            AddWarning(findings, CodeOverlayScroll,
                       "scrollViewport / scrollContent 미연결 (스크롤 비활성)", path, null, overlay);
        }

        Canvas canvas = overlay.GetComponent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.WorldSpace)
        {
            AddError(findings, CodeOverlayNotWorldSpace,
                     "Canvas Render Mode 가 World Space 가 아닙니다.", path, null, overlay);
        }

        if (overlay.gameObject.activeSelf)
        {
            AddWarning(findings, CodeOverlayActiveSaved,
                       "Overlay 가 활성 상태로 저장되어 있습니다.",
                       path,
                       "비활성으로 저장하는 것을 권장합니다.",
                       overlay);
        }
    }

    private static void CollectButtonFindings(List<ExhibitFinding> findings, ExhibitOverlayButton button)
    {
        string path = GetPath(button.transform);

        if (button.GetComponent<Collider>() == null)
        {
            AddError(findings, CodeButtonNoCollider, "버튼에 Collider 가 없습니다.", path, null, button);
        }
        if (button.gameObject.layer != 0)
        {
            AddWarning(findings, CodeButtonLayer,
                       "버튼 Layer 가 Default 가 아닙니다(Interact 실패 가능).", path, null, button);
        }

        // 버튼이 판넬 안에 있으면 Interact 툴팁이 본문 글자를 덮습니다.
        // (툴팁은 Collider 위쪽으로 자라므로 버튼이 본문 아래에 있는 한 구조적으로 겹칩니다)
        //
        // 3.0 은 이 구조를 고쳐 주지 않습니다. Setup 은 아이콘은 만들어 주지만 Overlay 는 만들지
        // 않으므로, 이 상태에서 정직한 조치는 작품을 다시 만드는 것뿐입니다.
        Transform parent = button.transform.parent;
        if (parent != null && parent.name != "ButtonColumn")
        {
            AddWarning(findings, CodeButtonInsidePanel,
                       "버튼이 판넬 안에 있어 Interact 툴팁이 설명 본문을 덮습니다.",
                       path,
                       "이 작품은 옛 버전 구조입니다. 작품을 지우고 Create 로 다시 만드세요.",
                       button);
        }
    }

    private static void CollectLanguageSwitchFindings(List<ExhibitFinding> findings,
                                                      ExhibitLanguageSwitch languageSwitch)
    {
        string path = GetPath(languageSwitch.transform);
        SerializedObject so = new SerializedObject(languageSwitch);

        if (languageSwitch.GetComponent<Collider>() == null)
        {
            AddError(findings, CodeSwitchNoCollider,
                     "언어 전환 버튼에 Collider 가 없습니다.", path, null, languageSwitch);
        }

        ExhibitManager assignedManager = GetObject(so, "manager") as ExhibitManager;
        if (assignedManager != null && assignedManager.gameObject.scene != languageSwitch.gameObject.scene)
        {
            AddError(findings, CodeSwitchCrossScene,
                     "언어 전환 버튼의 manager 가 다른 Scene('" + assignedManager.gameObject.scene.name +
                     "') 을 가리킵니다.",
                     path, null, languageSwitch);
        }
        else if (assignedManager == null && CollectManagersInScene(languageSwitch.gameObject.scene).Count == 0)
        {
            AddWarning(findings, CodeSwitchNoManager,
                       "언어 전환 버튼과 같은 Scene 에 ExhibitManager 가 없습니다.", path, null, languageSwitch);
        }
    }

    // =====================================================================
    // 4.2. Validate — 판넬이 들어갈 자리
    // =====================================================================

    /// <summary>
    /// 아이콘 자리의 앞뒤 여유가 Panel + 여백보다 좁으면 알립니다.
    /// 런타임과 같은 두 번의 Raycast 를 에디터에서 돌립니다.
    ///
    /// 작품이 콜라이더 안에 파묻힌 경우도 함께 봅니다 — 레이가 콜라이더 안에서 시작하면 그 콜라이더를
    /// 보고하지 않으므로 앞뒤 측정이 둘 다 실패하고, 벽 보정이 동작하지 않습니다.
    /// </summary>
    private static void CollectPanelDepthFindings(List<ExhibitFinding> findings, ExhibitInteractable exhibit,
                                                  string path)
    {
        SerializedObject so = new SerializedObject(exhibit);

        SerializedProperty extentsProperty = so.FindProperty("boundsExtentsLocal");
        if (extentsProperty == null || extentsProperty.vector3Value.sqrMagnitude <= 0f) return;

        Vector3 centerLocal = so.FindProperty("boundsCenterLocal").vector3Value;
        int thinAxis = so.FindProperty("thinAxis").intValue;

        ExhibitManager manager = FindManagerForScene(exhibit);
        int mask = manager != null ? manager.iconProbeLayerMask : DefaultIconProbeLayerMask;
        float clearance = manager != null ? manager.iconClearance : 0.02f;

        Transform t = exhibit.transform;
        Vector3 center = t.TransformPoint(centerLocal);
        Vector3 axis = t.rotation * (thinAxis == 0 ? Vector3.right : (thinAxis == 1 ? Vector3.up : Vector3.forward));

        if (Physics.CheckSphere(center, 0.01f, mask))
        {
            AddWarning(findings, CodeCenterInsideCollider,
                       "작품 중심이 콜라이더 안에 있어 벽 측정이 동작하지 않습니다.",
                       path,
                       "작품을 벽에서 조금 빼내거나 그 콜라이더를 Icon Probe Layers 에서 제외하세요.",
                       exhibit);
            return;
        }

        // 관람자가 설 수 있는 쪽을 정면으로 봅니다. (양쪽 다 막혀 있으면 + 쪽으로 가정)
        RaycastHit sideHit;
        bool positiveBlocked = Physics.Raycast(center, axis, out sideHit, 1f, mask, QueryTriggerInteraction.Ignore);
        bool negativeBlocked = Physics.Raycast(center, -axis, out sideHit, 1f, mask, QueryTriggerInteraction.Ignore);
        Vector3 front = positiveBlocked && !negativeBlocked ? -axis : axis;

        RaycastHit hit;
        float back = Physics.Raycast(center, -front, out hit, 1f, mask, QueryTriggerInteraction.Ignore) ? hit.distance : -1f;
        float forward = Physics.Raycast(center, front, out hit, 1f, mask, QueryTriggerInteraction.Ignore) ? hit.distance : -1f;

        if (back < 0f || forward < 0f) return;    // 한쪽이라도 열려 있으면 런타임이 해결합니다.

        float needed = OverlayWidth * CanvasScale * 0.5f + clearance * 2f;
        if (back + forward >= needed) return;

        AddWarning(findings, CodePanelNoDepth,
                   "아이콘 자리의 앞뒤 여유가 좁아 Panel 이 벽에 잠깁니다.",
                   "여유 " + (back + forward).ToString("0.00") + "m / 필요 " + needed.ToString("0.00") + "m — " + path,
                   "Icon Placement 를 다른 쪽으로 바꾸세요.",
                   exhibit);
    }

    /// <summary>
    /// 아이콘이 붙는 <b>옆방향</b>에 Panel 이 들어갈 자리가 있는지 봅니다. 모서리에 걸린 작품을 찾는 검사입니다.
    ///
    /// 런타임은 사람이 고른 <c>Icon Placement</c> 를 존중하고 자동으로 뒤집지 않습니다. Right/Left 는
    /// 관람자 기준이라 관람자가 걸어 다니면 판정이 경계에서 흔들리고, 그러면 아이콘이 좌우로 튀어
    /// 클리핑보다 나쁜 증상이 됩니다. 그래서 <b>고르는 일은 사람이, 찾는 일은 이 검사가</b> 합니다.
    ///
    /// 필요한 여유는 "작품 가장자리 → 여백 → Panel 전체 폭" 입니다. 반대쪽이 넉넉하면 그쪽을 권합니다.
    /// </summary>
    private static void CollectPanelSidewaysFindings(List<ExhibitFinding> findings, ExhibitInteractable exhibit,
                                                     string path)
    {
        SerializedObject so = new SerializedObject(exhibit);

        SerializedProperty extentsProperty = so.FindProperty("boundsExtentsLocal");
        if (extentsProperty == null || extentsProperty.vector3Value.sqrMagnitude <= 0f) return;

        int placement = so.FindProperty("iconPlacement").enumValueIndex;
        ExhibitManager manager = FindManagerForScene(exhibit);

        if (placement == (int)ExhibitIconPlacement.Default)
        {
            placement = manager != null ? (int)manager.defaultIconPlacement : (int)ExhibitIconPlacement.Right;
        }
        // Above / Below 는 월드 Y 고정이라 이 검사의 대상이 아닙니다. (앞뒤 검사가 이미 봅니다)
        if (placement != (int)ExhibitIconPlacement.Right && placement != (int)ExhibitIconPlacement.Left) return;

        Vector3 extents = extentsProperty.vector3Value;
        Vector3 centerLocal = so.FindProperty("boundsCenterLocal").vector3Value;
        int thinAxis = so.FindProperty("thinAxis").intValue;

        int mask = manager != null ? manager.iconProbeLayerMask : DefaultIconProbeLayerMask;
        float clearance = manager != null ? manager.iconClearance : 0.02f;
        float gap = manager != null ? manager.defaultIconGap : 0.15f;

        Transform t = exhibit.transform;
        Vector3 center = t.TransformPoint(centerLocal);
        Vector3 axis = t.rotation * (thinAxis == 0 ? Vector3.right : (thinAxis == 1 ? Vector3.up : Vector3.forward));

        // 관람자가 설 수 있는 쪽을 정면으로 봅니다.
        RaycastHit hit;
        bool positiveBlocked = Physics.Raycast(center, axis, out hit, 1f, mask, QueryTriggerInteraction.Ignore);
        bool negativeBlocked = Physics.Raycast(center, -axis, out hit, 1f, mask, QueryTriggerInteraction.Ignore);
        Vector3 front = positiveBlocked && !negativeBlocked ? -axis : axis;

        Vector3 flat = new Vector3(front.x, 0f, front.z);
        if (flat.sqrMagnitude < 0.0001f) return;    // 바닥에 눕힌 작품은 옆방향이 정해지지 않습니다.
        flat = flat.normalized;

        Vector3 chosen = placement == (int)ExhibitIconPlacement.Left
            ? Vector3.Cross(Vector3.up, flat)
            : -Vector3.Cross(Vector3.up, flat);

        float needed = gap + OverlayWidth * CanvasScale + clearance;  // 여백 + Overlay 전체 폭(본문+버튼 열) + 여백

        float chosenRoom = SidewaysRoom(exhibit, center, extents, chosen, needed, mask);
        if (chosenRoom < 0f) return;                                  // 막히지 않았습니다.

        float oppositeRoom = SidewaysRoom(exhibit, center, extents, -chosen, needed, mask);
        string current = placement == (int)ExhibitIconPlacement.Left ? "Left" : "Right";
        string suggestion = oppositeRoom < 0f
            ? "반대쪽(" + (current == "Left" ? "Right" : "Left") + ")은 여유가 있습니다. Icon Placement 를 그쪽으로 바꾸세요."
            : "반대쪽도 좁습니다 (" + oppositeRoom.ToString("0.00") + "m) — Above 또는 Below 를 쓰세요.";

        AddWarning(findings, CodePanelNoSideways,
                   "옆 여유가 좁아 Panel 이 옆 벽에 물립니다.",
                   current + " 쪽 " + chosenRoom.ToString("0.00") + "m / 필요 " + needed.ToString("0.00") + "m — " + path,
                   suggestion,
                   exhibit);
    }

    /// <summary>
    /// <paramref name="direction"/> 쪽으로 Panel 이 들어갈 자리가 있는지. 막혔으면 그 거리(m),
    /// 충분하면 -1 을 돌려줍니다.
    /// </summary>
    private static float SidewaysRoom(ExhibitInteractable exhibit, Vector3 center, Vector3 extents,
                                      Vector3 direction, float needed, int mask)
    {
        Transform t = exhibit.transform;

        float halfExtent =
            Mathf.Abs(Vector3.Dot(direction, t.right)) * extents.x +
            Mathf.Abs(Vector3.Dot(direction, t.up)) * extents.y +
            Mathf.Abs(Vector3.Dot(direction, t.forward)) * extents.z;

        Vector3 edge = center + direction * halfExtent;

        RaycastHit hit;
        if (!Physics.Raycast(edge, direction, out hit, needed, mask, QueryTriggerInteraction.Ignore)) return -1f;
        return hit.distance;
    }

    // =====================================================================
    // 4.3. Validate — 폰트 글리프
    // =====================================================================

    // 폰트 검사용 표본 문자
    //  KR: 이 패키지는 titleKR / descriptionKR / 기본 Interact 문구가 한국어인 KR 우선 패키지입니다.
    //  JP: 언어 전환 버튼 라벨이 "日本語" 이고 JP 데이터도 함께 다룹니다.
    //  기호: 도구가 만드는 버튼 라벨(× ▲ ▼). 사용자가 폰트를 바꿨을 때 여기서 깨지는지 봅니다.
    private const string GlyphProbeKR = "한글작품설명";
    private const string GlyphProbeJP = "日本語説明";
    private const string GlyphProbeSymbol = "×▲▼";

    /// <summary>폰트를 어디에 지정하는지. 문구를 한 곳에서 고치기 위해 상수로 둡니다.</summary>
    private const string FontAdvice =
        "CJK 글리프를 포함한 TMP Font Asset 을 만들어 ExhibitManager 의 " +
        "ExhibitDescriptorSettings > 'Overlay Font' 에 지정하세요. " +
        "(Exhibit Descriptor 창의 '전시 준비' 에서 바로 지정할 수 있고, 지정하면 창이 그 자리에서 반영합니다. " +
        "만드는 방법은 README 의 'CJK 폰트 준비' 참고)";

    /// <summary>
    /// Overlay / 언어 전환 버튼이 실제로 쓰는 TMP 폰트에 한글·일본어·버튼 기호 글리프가 있는지 봅니다.
    ///
    /// 폰트를 지정하지 않으면 TMP 기본값(LiberationSans SDF)이 쓰이는데, 여기에는 한글도 일본어도
    /// 없어 설명이 통째로 □ 로 보입니다. 참조 검사만으로는 절대 드러나지 않는 문제라 함께 봅니다.
    /// 같은 경고를 100번 찍지 않도록 <b>폰트 1개당 1번만</b> 보고합니다.
    /// </summary>
    private static void CollectFontFindings(List<ExhibitFinding> findings, ExhibitOverlay[] overlays,
                                            ExhibitLanguageSwitch[] switches)
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

                AddWarning(findings, CodeFontNoAsset,
                           "TMP 폰트가 지정되지 않았고 TMP 기본 폰트도 없습니다.",
                           GetPath(text.transform),
                           "Window > TextMeshPro > Import TMP Essential Resources 를 먼저 실행하세요.",
                           text);
                continue;
            }

            if (reported.Contains(font)) continue;
            reported.Add(font);

            string missingKR = FindMissingGlyphs(font, GlyphProbeKR);
            string missingJP = FindMissingGlyphs(font, GlyphProbeJP);
            string missingSymbol = FindMissingGlyphs(font, GlyphProbeSymbol);

            string where = "폰트 '" + font.name + "' / 예: " + GetPath(text.transform);

            if (missingKR.Length > 0)
            {
                AddWarning(findings, CodeFontMissingKR,
                           "지정한 폰트에 한글 글리프가 없습니다. 설명이 □ 로 표시됩니다.",
                           "없는 글자 " + missingKR + " — " + where, FontAdvice, text);
            }
            if (missingJP.Length > 0)
            {
                AddWarning(findings, CodeFontMissingJP,
                           "일본어 글리프가 없습니다. JP 설명이 □ 로 표시됩니다.",
                           "없는 글자 " + missingJP + " — " + where, FontAdvice, text);
            }
            if (missingSymbol.Length > 0)
            {
                AddWarning(findings, CodeFontMissingSymbol,
                           "버튼 라벨 기호가 없습니다. × ▲ ▼ 가 □ 로 표시됩니다.",
                           "없는 글자 " + missingSymbol + " — " + where, FontAdvice, text);
            }
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
}
#endif
