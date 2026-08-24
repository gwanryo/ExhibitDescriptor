#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;

/// <summary>
/// Exhibit Descriptor 의 설정·만들기·점검을 한 화면에 모은 창. <c>Tools > Exhibit Descriptor</c>.
///
/// <b>이 창은 로직을 갖지 않습니다.</b> 버튼은 전부 <see cref="ExhibitDescriptorTools"/> 의 메뉴
/// 함수를 그대로 부르고, 설정 칸은 실제 컴포넌트를 <see cref="SerializedObject"/> 로 그립니다.
/// 창이 값을 따로 들고 있으면 Inspector 와 어긋나고 <c>Ctrl+Z</c> 가 깨지므로, 값의 원본은 언제나
/// Scene 의 <see cref="ExhibitManager"/> / <see cref="ExhibitDescriptorSettings"/> 입니다.
///
/// 창이 없애려는 실패는 둘입니다 — 폰트·레이어를 지정하고 Setup 을 잊어 한글이 □ 로 남는 것,
/// 그리고 작품 100개 씬에서 Validate 결과가 콘솔에 흩어져 범인을 못 찾는 것.
/// </summary>
public class ExhibitDescriptorWindow : EditorWindow
{
    [MenuItem("Tools/Exhibit Descriptor/Exhibit Descriptor", false, -20)]
    public static void Open()
    {
        ExhibitDescriptorWindow window = GetWindow<ExhibitDescriptorWindow>("Exhibit Descriptor");
        window.minSize = new Vector2(340f, 320f);
        window.Refresh();
    }

    // ---------------------------------------------------------------------
    // 상태 (표시용 캐시만 — 설정값은 여기 두지 않습니다)
    // ---------------------------------------------------------------------

    /// <summary>null 이면 "아직 검사하지 않음". 빈 목록은 "검사했고 아무 문제 없음" 과 다릅니다.</summary>
    private List<ExhibitFinding> findings;

    private Vector2 scroll;
    private int exhibitCount;
    private int switchCount;

    /// <summary>
    /// Scene 한 개의 표시용 묶음. <c>OnGUI</c> 는 초당 여러 번 도므로 여기에 미리 담아 두고
    /// 그리기만 합니다. (Scene 마다 <c>FindObjectsOfType</c> 을 다시 부르면 작품 수백 개에서 창을
    /// 띄워 놓은 것만으로 에디터가 끕끕해집니다)
    /// </summary>
    private struct SceneRow
    {
        public Scene scene;
        public int exhibits;
        public List<ExhibitManager> managers;
    }

    private readonly List<SceneRow> sceneRows = new List<SceneRow>();

    /// <summary>마지막으로 반영한 결과 한 줄. 창이 조용히 아무것도 안 한 것처럼 보이지 않게 합니다.</summary>
    private string applyNote;

    /// <summary>접어 둔 묶음의 <c>code</c>. 기본은 펼침입니다.</summary>
    private readonly HashSet<string> collapsed = new HashSet<string>();

    private void OnEnable()
    {
        // Hierarchy 가 바뀌면 작품 수가 달라지고, 선택이 바뀌면 "선택 3개를 작품으로" 라벨이 달라집니다.
        EditorApplication.hierarchyChanged += Refresh;
        Selection.selectionChanged += Repaint;
        Refresh();
    }

    private void OnDisable()
    {
        EditorApplication.hierarchyChanged -= Refresh;
        Selection.selectionChanged -= Repaint;
    }

    /// <summary>
    /// 표시용 캐시를 다시 만듭니다. Hierarchy 가 바뀔 때만 도므로 Scene 을 훑어도 됩니다.
    /// </summary>
    private void Refresh()
    {
        ExhibitInteractable[] exhibits = Object.FindObjectsOfType<ExhibitInteractable>(true);

        exhibitCount = exhibits.Length;
        switchCount = Object.FindObjectsOfType<ExhibitLanguageSwitch>(true).Length;

        sceneRows.Clear();

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            int inScene = 0;
            for (int i = 0; i < exhibits.Length; i++)
            {
                if (exhibits[i].gameObject.scene == scene) inScene++;
            }

            List<ExhibitManager> managers = ExhibitDescriptorTools.CollectManagersInScene(scene);

            // 작품도 Manager 도 없는 Scene(환경 전용 등)은 이 창의 관심사가 아닙니다.
            if (inScene == 0 && managers.Count == 0) continue;

            SceneRow row = new SceneRow();
            row.scene = scene;
            row.exhibits = inScene;
            row.managers = managers;

            sceneRows.Add(row);
        }

        Repaint();
    }

    // ---------------------------------------------------------------------
    // 그리기
    // ---------------------------------------------------------------------

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawPreparationSection();
        EditorGUILayout.Space(8f);
        DrawExhibitSection();
        EditorGUILayout.Space(8f);
        DrawValidationSection();

        EditorGUILayout.EndScrollView();
    }

    // ---------------------------------------------------------------------
    // ① 전시 준비
    // ---------------------------------------------------------------------

    /// <summary>
    /// Scene 마다 한 블록씩 그립니다. Manager 는 "Scene 당 1개" 가 규칙이라 Additive 로 여러 Scene 을
    /// 열어 둔 구성에서는 각자 따로 설정해야 합니다. 규칙을 숨기지 않고 보이게 두는 쪽을 택했습니다.
    /// </summary>
    private void DrawPreparationSection()
    {
        Header("① 전시 준비");

        for (int i = 0; i < sceneRows.Count; i++)
        {
            DrawSceneBlock(sceneRows[i].scene, sceneRows[i].managers);
        }

        if (sceneRows.Count == 0)
        {
            EditorGUILayout.HelpBox("아직 전시가 없습니다. 아래 'ExhibitionRoot 만들기' 로 시작하세요.",
                                    MessageType.Info);
            if (GUILayout.Button("ExhibitionRoot 만들기")) ExhibitDescriptorTools.CreateExhibitionRoot();
        }
    }

    private void DrawSceneBlock(Scene scene, List<ExhibitManager> managers)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Scene: " + scene.name, EditorStyles.miniBoldLabel);

        if (managers.Count == 0)
        {
            EditorGUILayout.HelpBox("이 Scene 에 작품이 있는데 ExhibitManager 가 없습니다.", MessageType.Error);
            if (GUILayout.Button("이 Scene 에 ExhibitionRoot 만들기"))
            {
                // CreateExhibitionRoot 는 활성 Scene 을 기준으로 만듭니다.
                SceneManager.SetActiveScene(scene);
                ExhibitDescriptorTools.CreateExhibitionRoot();
            }

            EditorGUILayout.EndVertical();
            return;
        }

        if (managers.Count > 1)
        {
            EditorGUILayout.HelpBox("ExhibitManager 가 " + managers.Count + " 개입니다. Scene 당 1개만 남기세요.",
                                    MessageType.Error);
        }

        ExhibitManager manager = managers[0];
        DrawManagerSettings(scene, manager);

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// 안 채우면 반드시 막히는 값 셋만 그립니다 — 폰트 · 벽 레이어 · 기본 언어.
    /// 아이콘 크기·시선 각도·페이드처럼 기본값이 이미 정답인 것들은 Manager Inspector 에 둡니다.
    /// (창에 전부 옮기면 같은 것을 두 곳에서 그리게 되고, 어느 쪽이 원본인지 흐려집니다)
    /// </summary>
    private void DrawManagerSettings(Scene scene, ExhibitManager manager)
    {
        // GetComponent 로 "읽기만" 합니다. EnsureSettings 는 없으면 컴포넌트를 붙이는데, 그것을
        // OnGUI 에서 부르면 창을 띄워 놓은 것만으로 Scene 이 dirty 가 되고 repaint 마다 Undo 기록이
        // 쌓입니다. 붙이는 일은 사람이 버튼을 눌렀을 때만 해야 합니다.
        ExhibitDescriptorSettings settings = manager.GetComponent<ExhibitDescriptorSettings>();

        if (settings == null)
        {
            EditorGUILayout.HelpBox("이 Manager 에 ExhibitDescriptorSettings 가 없어 폰트를 지정할 수 " +
                                    "없습니다. (지금은 TMP 기본 폰트 = 한글이 □)", MessageType.Warning);

            if (GUILayout.Button("설정 컴포넌트 추가"))
            {
                ExhibitDescriptorTools.EnsureSettings(manager.gameObject);
                MarkDirty(scene);
            }

            return;
        }

        // --- 폰트 · 벽 레이어 (Editor 전용 컴포넌트) -----------------------
        SerializedObject settingsSo = new SerializedObject(settings);
        settingsSo.Update();

        SerializedProperty fontProperty = settingsSo.FindProperty("overlayFont");
        SerializedProperty layerProperty = settingsSo.FindProperty("iconProbeLayers");

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(fontProperty, new GUIContent("Overlay 폰트"));
        EditorGUILayout.PropertyField(layerProperty, new GUIContent("벽 판정 Layer"));
        bool settingsChanged = EditorGUI.EndChangeCheck();

        if (settingsChanged)
        {
            settingsSo.ApplyModifiedProperties();

            // 여기서 바로 반영하는 이유: 폰트도 레이어도 Setup 이 굽기 전에는 아무 효과가 없습니다.
            // 값만 들어가고 화면은 그대로인 상태가 이 창이 없애려는 실패 그 자체입니다.
            ApplyExhibitionSettings(scene);
        }

        if (fontProperty.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("폰트가 없어 한글·일본어가 □ 로 보입니다. CJK 글리프를 포함한 " +
                                    "TMP Font Asset 을 지정하세요.", MessageType.Warning);
        }

        // --- 기본 언어 (UdonSharpBehaviour 필드라 Udon 에 구워야 합니다) ---
        SerializedObject managerSo = new SerializedObject(manager);
        managerSo.Update();

        SerializedProperty languageProperty = managerSo.FindProperty("defaultLanguage");

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(languageProperty, new GUIContent("기본 언어"));

        if (EditorGUI.EndChangeCheck())
        {
            managerSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);

            // Manager 의 직렬화 필드는 Udon 힙에 구워져야 런타임에 보입니다.
            ExhibitDescriptorTools.TryCopyProxyToUdon(manager);
            MarkDirty(scene);

            applyNote = "기본 언어를 " + languageProperty.enumDisplayNames[languageProperty.enumValueIndex] +
                        " 로 바꿨습니다.";
        }

        // --- 열림 방식 ---
        SerializedProperty openModeProperty = managerSo.FindProperty("defaultOpenMode");

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(openModeProperty, new GUIContent("열림 방식"));

        if (EditorGUI.EndChangeCheck())
        {
            managerSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            ExhibitDescriptorTools.TryCopyProxyToUdon(manager);
            MarkDirty(scene);

            applyNote = "열림 방식을 " + openModeProperty.enumDisplayNames[openModeProperty.enumValueIndex] +
                        " 로 바꿨습니다.";
        }

        if (openModeProperty.enumValueIndex == 2)   // Proximity
        {
            EditorGUILayout.HelpBox("작품을 응시한 채로 Gaze Distance 안에 들어가면 설명이 저절로 " +
                                    "열립니다. ⓘ 아이콘은 뜨지 않습니다. 작품별로 다르게 두려면 각 " +
                                    "ExhibitInteractable 의 Open Mode 를 바꾸세요.", MessageType.Info);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("ExhibitManager 선택", EditorStyles.miniButton))
        {
            Selection.activeGameObject = manager.gameObject;
            EditorGUIUtility.PingObject(manager.gameObject);
        }
        EditorGUILayout.LabelField("세부 값은 Inspector 에서", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 폰트·레이어를 실제로 반영합니다. 레이어는 Manager 에 int 로 굽고, 폰트는 Setup 이 각 TMP
    /// 텍스트에 굽습니다. 둘 다 멱등이라 여러 번 눌러도 안전합니다.
    /// </summary>
    private void ApplyExhibitionSettings(Scene scene)
    {
        // Setup 은 열린 Scene 전체를 훑고, 그 안에서 벽 판정 레이어도 Scene 당 1회 굽습니다.
        // 폰트를 Scene 하나만 바꿨어도 전부 다시 구우면 되므로(멱등) 진입점을 따로 만들지 않고
        // 기존 메뉴 함수를 그대로 씁니다.
        ExhibitDescriptorTools.SetupAllExhibitsInScene();

        applyNote = "작품 " + exhibitCount + " 개 · 언어 전환 버튼 " + switchCount + " 개에 반영했습니다.";

        // 반영이 무언가를 고쳤을 수 있으니 점검 결과는 낡은 것으로 봅니다.
        findings = null;
    }

    // ---------------------------------------------------------------------
    // ② 작품
    // ---------------------------------------------------------------------

    private void DrawExhibitSection()
    {
        Header("② 작품" + (exhibitCount > 0 ? "  —  " + exhibitCount + " 개" : ""));

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        int selected = CountSelectedGameObjects();

        using (new EditorGUI.DisabledScope(selected == 0))
        {
            string label = selected > 0
                ? "선택한 " + selected + " 개를 작품으로 변환"
                : "선택한 Mesh 를 작품으로 변환 (Hierarchy 에서 먼저 선택)";

            if (GUILayout.Button(label)) ExhibitDescriptorTools.CreateExhibitsFromSelectedMeshes();
        }

        if (GUILayout.Button("빈 작품 1개 만들기")) ExhibitDescriptorTools.CreateExhibitTemplate();

        EditorGUILayout.Space(4f);

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(selected == 0))
        {
            if (GUILayout.Button("Setup — 선택")) ExhibitDescriptorTools.SetupSelectedExhibits();
        }
        if (GUILayout.Button("Setup — 씬 전체")) ExhibitDescriptorTools.SetupAllExhibitsInScene();
        EditorGUILayout.EndHorizontal();

        bool autoSetup = EditorGUILayout.ToggleLeft("저장할 때 자동 Setup", ExhibitDescriptorTools.AutoSetupOnSave);
        if (autoSetup != ExhibitDescriptorTools.AutoSetupOnSave)
        {
            ExhibitDescriptorTools.AutoSetupOnSave = autoSetup;
        }

        if (!string.IsNullOrEmpty(applyNote))
        {
            EditorGUILayout.LabelField(applyNote, EditorStyles.miniLabel);
        }

        EditorGUILayout.EndVertical();
    }

    // ---------------------------------------------------------------------
    // ③ 점검
    // ---------------------------------------------------------------------

    private void DrawValidationSection()
    {
        EditorGUILayout.BeginHorizontal();
        Header("③ 점검");
        if (GUILayout.Button(findings == null ? "검사" : "다시 검사", GUILayout.Width(72f)))
        {
            findings = ExhibitDescriptorTools.CollectFindings();
        }
        EditorGUILayout.EndHorizontal();

        if (findings == null)
        {
            EditorGUILayout.HelpBox("'검사' 를 누르면 참조 누락 · 폰트 글리프 · 판넬이 들어갈 자리를 " +
                                    "한 번에 봅니다.", MessageType.None);
            return;
        }

        int errors = ExhibitDescriptorTools.CountBySeverity(findings, ExhibitFindingSeverity.Error);
        int warnings = ExhibitDescriptorTools.CountBySeverity(findings, ExhibitFindingSeverity.Warning);

        if (errors == 0 && warnings == 0)
        {
            EditorGUILayout.HelpBox("통과. 작품 " + exhibitCount + " 개.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("오류 " + errors + " 건 · 경고 " + warnings + " 건", EditorStyles.miniBoldLabel);

        List<ExhibitDescriptorTools.ExhibitFindingGroup> groups = ExhibitDescriptorTools.GroupFindings(findings);
        for (int i = 0; i < groups.Count; i++) DrawFindingGroup(groups[i]);
    }

    private void DrawFindingGroup(ExhibitDescriptorTools.ExhibitFindingGroup group)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        bool isError = group.severity == ExhibitFindingSeverity.Error;
        bool isCollapsed = collapsed.Contains(group.code);

        string prefix = isError ? "✖ " : "⚠ ";
        string suffix = group.items.Count > 1 ? "  (" + group.items.Count + ")" : "";

        // Foldout 을 쓰면 같은 문제 100건이 한 줄로 접힙니다. 콘솔이 못 하는 일이 이것입니다.
        bool expanded = EditorGUILayout.Foldout(!isCollapsed, prefix + group.title + suffix, true);
        if (expanded == isCollapsed)
        {
            if (expanded) collapsed.Remove(group.code);
            else collapsed.Add(group.code);
        }

        if (expanded)
        {
            EditorGUI.indentLevel++;

            for (int i = 0; i < group.items.Count; i++) DrawFindingRow(group.items[i]);

            if (!string.IsNullOrEmpty(group.advice))
            {
                EditorGUILayout.LabelField("→ " + group.advice, WrappedMiniLabel());
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawFindingRow(ExhibitFinding finding)
    {
        EditorGUILayout.BeginHorizontal();

        string text = !string.IsNullOrEmpty(finding.detail) ? finding.detail : "(Scene 전체)";
        EditorGUILayout.LabelField(text, WrappedMiniLabel());

        // target 이 없는 건(Scene 단위 문제)은 고를 대상이 없으므로 버튼을 내지 않습니다.
        using (new EditorGUI.DisabledScope(finding.target == null))
        {
            if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(40f)) && finding.target != null)
            {
                Selection.activeObject = finding.target;
                EditorGUIUtility.PingObject(finding.target);
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    // ---------------------------------------------------------------------
    // 잡동사니
    // ---------------------------------------------------------------------

    private static void Header(string title)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    /// <summary>
    /// 경로와 조치 문구는 길어서 창 폭에서 잘립니다. 줄바꿈되는 라벨 스타일을 씁니다.
    /// (<c>EditorStyles.miniLabel</c> 을 그대로 고치면 에디터 전역에 영향이 갑니다)
    /// </summary>
    private static GUIStyle wrappedMiniLabel;

    private static GUIStyle WrappedMiniLabel()
    {
        if (wrappedMiniLabel == null)
        {
            wrappedMiniLabel = new GUIStyle(EditorStyles.miniLabel);
            wrappedMiniLabel.wordWrap = true;
        }

        return wrappedMiniLabel;
    }

    private static int CountSelectedGameObjects()
    {
        GameObject[] selection = Selection.gameObjects;
        return selection != null ? selection.Length : 0;
    }

    private static void MarkDirty(Scene scene)
    {
        if (scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
}
#endif
