using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 작품 1개를 담당하는 컴포넌트. Exhibit Prefab 의 Root 에 붙입니다.
///
/// - 작품 데이터(KR/EN/JP)를 자기 자신이 직접 보유합니다.
/// - VRChat 기본 Interact 로 자기 Overlay 를 Toggle 합니다.
/// - Update() 를 전혀 사용하지 않습니다. (애니메이션은 ExhibitManager 가 일괄 처리)
/// - Local Only: 네트워크 동기화를 사용하지 않습니다.
///
/// Collider 안내
///  - Interact 를 받으려면 이 컴포넌트가 붙은 GameObject "또는 그 자식" 에 Collider 가 있어야 합니다.
///  - 권장: 자식 "InteractionArea" 오브젝트에 BoxCollider 를 두고 Mesh 와 분리합니다.
///    (Artwork 의 MeshCollider 와 무관하게 클릭 영역을 자유롭게 조절할 수 있습니다.)
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ExhibitInteractable : UdonSharpBehaviour
{
    // ---------------------------------------------------------------------
    // References
    // ---------------------------------------------------------------------

    [Header("References")]
    [Tooltip("Scene 의 ExhibitManager. 비워두면 실행 시 이름으로 자동 탐색합니다.")]
    [SerializeField] private ExhibitManager manager;

    [Tooltip("이 작품 전용 Overlay. Prefab 내부에 있으므로 Prefab 안에서 연결됩니다.")]
    [SerializeField] private ExhibitOverlay overlay;

    [Tooltip("Overlay 가 표시될 위치/방향. 기본값은 작품 옆입니다.")]
    [SerializeField] private Transform overlayAnchor;

    [Tooltip("manager 가 비어 있을 때 GameObject.Find 로 찾을 오브젝트 이름.")]
    [SerializeField] private string managerObjectName = "ExhibitManager";

    [Tooltip("Interact 판정을 대신 받는 자식 영역들(InteractionArea). Interact 문구/거리가 여기에도 적용됩니다.")]
    [SerializeField] private ExhibitInteractRelay[] interactRelays = new ExhibitInteractRelay[0];

    // ---------------------------------------------------------------------
    // Exhibit Data (KR / EN / JP)
    // ---------------------------------------------------------------------

    [Header("Title")]
    [TextArea(1, 3)] [SerializeField] private string titleKR = "작품 제목";
    [TextArea(1, 3)] [SerializeField] private string titleEN = "Artwork Title";
    [TextArea(1, 3)] [SerializeField] private string titleJP = "作品タイトル";

    [Header("Subtitle (작가 / 연도 등, 선택)")]
    [TextArea(1, 3)] [SerializeField] private string subtitleKR = "";
    [TextArea(1, 3)] [SerializeField] private string subtitleEN = "";
    [TextArea(1, 3)] [SerializeField] private string subtitleJP = "";

    [Header("Description")]
    [TextArea(4, 12)] [SerializeField] private string descriptionKR = "작품 설명을 입력하세요.";
    [TextArea(4, 12)] [SerializeField] private string descriptionEN = "Enter the description here.";
    [TextArea(4, 12)] [SerializeField] private string descriptionJP = "作品の説明を入力してください。";

    [Header("Extra Info (선택) - Label / Value 를 같은 순서로 채웁니다")]
    [Tooltip("예: 재료, 크기, 소장처 ...")]
    [SerializeField] private string[] extraLabelsKR = new string[0];
    [SerializeField] private string[] extraValuesKR = new string[0];
    [SerializeField] private string[] extraLabelsEN = new string[0];
    [SerializeField] private string[] extraValuesEN = new string[0];
    [SerializeField] private string[] extraLabelsJP = new string[0];
    [SerializeField] private string[] extraValuesJP = new string[0];

    // ---------------------------------------------------------------------
    // Display Options
    // ---------------------------------------------------------------------

    [Header("표시할 정보")]
    [SerializeField] private bool showTitle = true;
    [SerializeField] private bool showSubtitle = false;
    [SerializeField] private bool showDescription = true;
    [SerializeField] private bool showExtraInfo = false;

    // ---------------------------------------------------------------------
    // Interaction
    // ---------------------------------------------------------------------

    [Header("Interaction")]
    [Tooltip("VRChat Interact 표시 문구 (한국어). 비우면 Manager 기본값(\"설명\")을 사용합니다.")]
    [SerializeField] private string interactionTextKR = "";
    [SerializeField] private string interactionTextEN = "";
    [SerializeField] private string interactionTextJP = "";

    // Editor 도구가 SerializedObject 로만 읽는 값이라 C# 코드에서는 참조되지 않습니다. (CS0414 억제)
#pragma warning disable 0414
    [Tooltip("Interact 가능 거리(m). 기본값 2m.\n" +
             "Editor 도구(Setup)가 이 값을 UdonBehaviour 에 구워 넣습니다. 런타임 변경은 불가합니다.")]
    [Min(0.1f)] [SerializeField] private float interactionProximity = 2f;
#pragma warning restore 0414

    [Header("Overlay 배치")]
    [Tooltip("Overlay 를 열 때 OverlayAnchor 의 위치/회전으로 스냅합니다. (방향 고정)")]
    [SerializeField] private bool snapToAnchorOnOpen = true;

    // ---------------------------------------------------------------------
    // Runtime
    // ---------------------------------------------------------------------

    private bool _started;
    private bool _warnedNoManager;

    // ---------------------------------------------------------------------
    // Unity / Udon Events
    // ---------------------------------------------------------------------

    void Start()
    {
        _started = true;
        _EnsureManager();
        _ApplyInteractSettings();

        if (!Utilities.IsValid(overlay))
        {
            Debug.LogWarning("[ExhibitInteractable] overlay 참조가 비어 있습니다: " + gameObject.name);
            return;
        }

        // 편집 중 Overlay 를 켜 둔 채로 저장했더라도 월드 입장 시에는 반드시 닫힌 상태로 시작합니다.
        if (overlay.gameObject.activeInHierarchy) overlay._CloseImmediate();
    }

    void OnEnable()
    {
        _EnsureManager();

        if (Utilities.IsValid(manager))
        {
            manager._RegisterExhibit(this);
        }

        // Start() 이후에 재활성화된 경우 현재 언어를 다시 반영합니다.
        if (_started) _ApplyInteractSettings();
    }

    void OnDisable()
    {
        if (Utilities.IsValid(manager))
        {
            manager._UnregisterExhibit(this);
        }

        // Overlay 가 이 작품의 자식이 아닌 경우(외부 배치)에도 확실히 닫습니다.
        // 자식인 경우에는 Overlay 자신의 OnDisable 이 처리하므로 여기서는 호출하지 않습니다.
        if (Utilities.IsValid(overlay) && overlay.gameObject.activeInHierarchy)
        {
            overlay._CloseImmediate();
        }
    }

    /// <summary>
    /// VRChat 기본 Interact. 같은 작품을 다시 누르면 Toggle 로 닫힙니다.
    /// 다른 작품의 Overlay 에는 전혀 영향을 주지 않습니다.
    /// </summary>
    public override void Interact()
    {
        if (!Utilities.IsValid(overlay)) return;

        _EnsureManager();

        if (overlay.IsOpen) _CloseOverlay();
        else _OpenOverlay();
    }

    // ---------------------------------------------------------------------
    // Public API (버튼 / 다른 스크립트에서 호출 가능)
    // ---------------------------------------------------------------------

    public void _OpenOverlay()
    {
        if (!Utilities.IsValid(overlay)) return;

        GameObject overlayObject = overlay.gameObject;
        if (!overlayObject.activeSelf) overlayObject.SetActive(true);

        if (snapToAnchorOnOpen && Utilities.IsValid(overlayAnchor))
        {
            Transform overlayTransform = overlayObject.transform;
            overlayTransform.position = overlayAnchor.position;
            overlayTransform.rotation = overlayAnchor.rotation; // 플레이어를 따라 회전하지 않는 고정 방향
        }

        _PushContent();
        overlay._Open(manager);
    }

    public void _CloseOverlay()
    {
        if (!Utilities.IsValid(overlay)) return;
        if (!overlay.gameObject.activeInHierarchy) return;
        overlay._Close();
    }

    public void _ToggleOverlay()
    {
        if (!Utilities.IsValid(overlay)) return;
        if (overlay.IsOpen) _CloseOverlay();
        else _OpenOverlay();
    }

    /// <summary>ExhibitManager 가 언어를 바꿀 때 호출합니다.</summary>
    public void _OnLanguageChanged()
    {
        _ApplyInteractSettings();

        // 열려 있는 Overlay 만 즉시 갱신합니다.
        if (Utilities.IsValid(overlay) && overlay.IsOpen && overlay.gameObject.activeInHierarchy)
        {
            _PushContent();
            overlay._ApplyLanguage(_CurrentLanguage());

            // 언어마다 본문 길이가 달라지므로 스크롤을 맨 위로 되돌립니다.
            overlay._ScrollToTop();
        }
    }

    // ---------------------------------------------------------------------
    // Internal
    // ---------------------------------------------------------------------

    /// <summary>
    /// ExhibitManager 를 찾아 연결합니다.
    ///  1) 자기 Hierarchy Root 안을 먼저 찾습니다. Transform Root 는 항상 같은 Scene 이므로
    ///     Additive Scene 에서도 안전합니다.
    ///  2) 못 찾으면 이름으로 찾습니다. (GameObject.Find)
    ///
    /// Scene 검증에 대하여
    ///  Udon 은 UnityEngine.SceneManagement.Scene 타입을 전혀 노출하지 않습니다.
    ///  (GameObject.scene / Scene.GetRootGameObjects 모두 화이트리스트에 없어 UdonSharp 컴파일이 실패합니다.
    ///   VRChat Worlds SDK 3.10.4 의 Udon extern 목록으로 확인했습니다.)
    ///  따라서 런타임에서는 2) 가 찾은 오브젝트가 같은 Scene 인지 확인할 방법이 없습니다.
    ///
    ///  Additive 로 여러 Scene 을 띄우고 각 Scene 에 동명의 Manager 를 두는 구성이라면
    ///  2) 가 다른 Scene 의 Manager 를 물 수 있고, 그 Scene 이 언로드되면 이 작품이 멈춥니다.
    ///  그런 구성에서는 반드시 Tools > Exhibit Descriptor > Setup All Exhibits In Scene 을 실행하세요.
    ///  Editor 도구는 같은 Scene 의 Manager 만 manager 필드에 구워 넣으므로 1)/2) 가 아예 실행되지 않습니다.
    ///
    ///  (Manager 를 못 찾아도 Overlay 는 애니메이션만 생략하고 열림/닫힘은 동작합니다.)
    /// </summary>
    private void _EnsureManager()
    {
        if (Utilities.IsValid(manager)) return;

        Transform root = transform.root;
        if (Utilities.IsValid(root))
        {
            ExhibitManager local = root.GetComponentInChildren<ExhibitManager>();
            if (!Utilities.IsValid(local)) local = root.GetComponentInChildren<ExhibitManager>(true);

            if (Utilities.IsValid(local))
            {
                _LinkManager(local);
                return;
            }
        }

        if (managerObjectName != null && managerObjectName.Length > 0)
        {
            GameObject found = GameObject.Find(managerObjectName);

            if (Utilities.IsValid(found))
            {
                ExhibitManager foundManager = found.GetComponent<ExhibitManager>();
                if (Utilities.IsValid(foundManager))
                {
                    _LinkManager(foundManager);
                    return;
                }
            }
        }

        if (!_warnedNoManager)
        {
            _warnedNoManager = true;
            Debug.LogWarning("[ExhibitInteractable] ExhibitManager 를 찾지 못했습니다: " + gameObject.name +
                             " (Tools > Exhibit Descriptor > Setup All Exhibits In Scene 을 실행하면 " +
                             "같은 Scene 의 Manager 가 manager 필드에 연결됩니다)");
        }
    }

    /// <summary>
    /// 찾아낸 Manager 를 참조에 넣고 **등록까지** 마칩니다.
    ///
    /// OnEnable() 시점에는 Manager 오브젝트가 아직 없거나 꺼져 있어서 못 찾는 일이 흔합니다.
    /// 그 경우 Start() 나 첫 Interact() 에서 뒤늦게 찾게 되는데, 참조만 채우고 끝내면
    /// 이 작품은 Manager 의 등록 목록에 영영 들어가지 못해 언어 전환 브로드캐스트
    /// (_OnLanguageChanged) 를 한 번도 받지 못합니다.
    ///
    /// _RegisterExhibit 은 중복 등록을 스스로 걸러 내므로 OnEnable 의 등록과 겹쳐도 안전합니다.
    /// </summary>
    private void _LinkManager(ExhibitManager found)
    {
        manager = found;
        found._RegisterExhibit(this);
    }

    private int _CurrentLanguage()
    {
        if (!Utilities.IsValid(manager)) return 0;
        return manager._GetLanguageIndex();
    }

    /// <summary>Interact 문구와 Proximity 를 UdonBehaviour 에 적용합니다.</summary>
    private void _ApplyInteractSettings()
    {
        int lang = _CurrentLanguage();

        // 우선순위는 "현재 언어" 가 먼저입니다.
        //  1) 현재 언어의 작품 문구
        //  2) 현재 언어의 Manager 기본 문구
        //  3) 그래도 없으면 다른 언어로라도 (작품 -> Manager 순)
        //
        // 1) 에서 곧바로 다른 언어까지 fallback 하면 안 됩니다. EN 문구만 채운 작품을
        // KR 로 볼 때 영어 문구가 잡혀 버려서, 정작 번역되어 있는 Manager 의 한국어
        // 기본 문구("설명")를 건너뛰고 "View" 가 그대로 보이게 됩니다.
        string text = _PickExact(interactionTextKR, interactionTextEN, interactionTextJP, lang);

        if (_IsEmpty(text) && Utilities.IsValid(manager))
        {
            text = _PickExact(manager.defaultInteractionTextKR,
                              manager.defaultInteractionTextEN,
                              manager.defaultInteractionTextJP,
                              lang);
        }

        if (_IsEmpty(text))
        {
            text = _Pick(interactionTextKR, interactionTextEN, interactionTextJP, lang);
        }

        if (_IsEmpty(text) && Utilities.IsValid(manager))
        {
            text = _Pick(manager.defaultInteractionTextKR,
                         manager.defaultInteractionTextEN,
                         manager.defaultInteractionTextJP,
                         lang);
        }

        if (_IsEmpty(text)) text = "설명";

        // -----------------------------------------------------------------
        // InteractionText 는 UdonSharpBehaviour 가 제공하므로 런타임에 바꿀 수 있습니다.
        // 반면 Interact 거리(proximity)는 UdonBehaviour 쪽 필드라 UdonSharp 에서 접근할 수 없습니다.
        // (UdonSharpBehaviour 에 proximity 멤버가 없습니다 - Worlds SDK 3.10.4 기준)
        // 그래서 거리는 Editor Tool 이 UdonBehaviour 에 직접 구워 넣습니다:
        //  Tools > Exhibit Descriptor > Setup All Exhibits In Scene
        // -----------------------------------------------------------------
        InteractionText = text;

        // Interact 판정을 대신 받는 자식 영역(InteractionArea)에도 같은 값을 적용합니다.
        if (interactRelays == null) return;

        for (int i = 0; i < interactRelays.Length; i++)
        {
            ExhibitInteractRelay relay = interactRelays[i];
            if (!Utilities.IsValid(relay)) continue;
            if (!relay.gameObject.activeInHierarchy) continue;
            relay._SetInteractText(text);
        }
    }

    /// <summary>현재 언어 기준으로 Overlay 에 표시할 텍스트를 만들어 전달합니다.</summary>
    private void _PushContent()
    {
        if (!Utilities.IsValid(overlay)) return;

        int lang = _CurrentLanguage();

        string title = "";
        if (showTitle) title = _Pick(titleKR, titleEN, titleJP, lang);

        string subtitle = "";
        if (showSubtitle) subtitle = _Pick(subtitleKR, subtitleEN, subtitleJP, lang);

        string body = "";

        if (showExtraInfo)
        {
            string extra = _BuildExtraInfo(lang);
            if (!_IsEmpty(extra)) body = extra;
        }

        if (showDescription)
        {
            string description = _Pick(descriptionKR, descriptionEN, descriptionJP, lang);
            if (!_IsEmpty(description))
            {
                if (!_IsEmpty(body)) body = body + "\n\n";
                body = body + description;
            }
        }

        overlay._SetContent(title, subtitle, body);
    }

    /// <summary>
    /// Extra Info 를 "Label : Value" 줄 목록으로 만듭니다.
    ///
    /// Label 과 Value 는 같은 인덱스로 짝지어지며, fallback 은 배열 단위가 아니라 **칸 단위**입니다.
    /// EN Label 만 채우고 EN Value 를 비워 둔 것처럼 한쪽만 번역된 데이터에서도
    /// 짧은 배열 길이에 맞춰 줄이 잘려 나가지 않고, 비어 있는 칸만 KR -> EN -> JP 로 대체됩니다.
    /// </summary>
    private string _BuildExtraInfo(int lang)
    {
        // 줄 수는 여섯 배열 중 가장 긴 것을 기준으로 합니다.
        // (특정 언어 배열이 비어 있어도 다른 언어의 줄이 통째로 사라지지 않습니다.)
        int count = _LongestLength(extraLabelsKR, extraLabelsEN, extraLabelsJP);
        int valueCount = _LongestLength(extraValuesKR, extraValuesEN, extraValuesJP);
        if (valueCount > count) count = valueCount;

        string result = "";

        for (int i = 0; i < count; i++)
        {
            string label = _PickAt(extraLabelsKR, extraLabelsEN, extraLabelsJP, i, lang);
            string value = _PickAt(extraValuesKR, extraValuesEN, extraValuesJP, i, lang);
            if (_IsEmpty(label) && _IsEmpty(value)) continue;

            if (!_IsEmpty(result)) result = result + "\n";

            if (_IsEmpty(label)) result = result + value;
            else if (_IsEmpty(value)) result = result + label;
            else result = result + label + " : " + value;
        }

        return result;
    }

    private int _LongestLength(string[] kr, string[] en, string[] jp)
    {
        int longest = 0;
        if (kr != null && kr.Length > longest) longest = kr.Length;
        if (en != null && en.Length > longest) longest = en.Length;
        if (jp != null && jp.Length > longest) longest = jp.Length;
        return longest;
    }

    /// <summary>같은 인덱스의 KR/EN/JP 칸 하나를 현재 언어 기준으로 고릅니다. (비어 있으면 fallback)</summary>
    private string _PickAt(string[] kr, string[] en, string[] jp, int index, int lang)
    {
        return _Pick(_ElementAt(kr, index), _ElementAt(en, index), _ElementAt(jp, index), lang);
    }

    private string _ElementAt(string[] array, int index)
    {
        if (array == null) return "";
        if (index < 0 || index >= array.Length) return "";
        return array[index];
    }

    /// <summary>현재 언어 칸만 그대로 읽습니다. (다른 언어로 fallback 하지 않습니다)</summary>
    private string _PickExact(string kr, string en, string jp, int lang)
    {
        string result;
        if (lang == 1) result = en;
        else if (lang == 2) result = jp;
        else result = kr;

        if (result == null) result = "";
        return result;
    }

    private string _Pick(string kr, string en, string jp, int lang)
    {
        string result = _PickExact(kr, en, jp, lang);

        // 미번역 항목은 KR -> EN -> JP 순으로 존재하는 텍스트를 대신 사용합니다.
        if (_IsEmpty(result)) result = kr;
        if (_IsEmpty(result)) result = en;
        if (_IsEmpty(result)) result = jp;
        if (result == null) result = "";
        return result;
    }

    private bool _IsEmpty(string value)
    {
        return value == null || value.Length == 0;
    }
}
