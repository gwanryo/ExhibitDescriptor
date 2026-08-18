using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 작품 1개를 담당하는 컴포넌트. Exhibit Prefab 의 Root 에 붙입니다.
///
/// - 작품 데이터(KR/EN/JP)를 자기 자신이 직접 보유합니다.
/// - Update() 를 전혀 사용하지 않습니다. (Overlay 애니메이션과 아이콘 갱신은 ExhibitManager 가 일괄 처리)
/// - Local Only: 네트워크 동기화를 사용하지 않습니다.
///
/// <b>Interact 는 이 컴포넌트가 받지 않습니다.</b> 작품 정면에는 판정 영역이 없고,
/// 응시할 때만 켜지는 자식 <see cref="ExhibitInfoIcon"/> 이 유일한 Interact 대상입니다.
/// 그래서 작품을 감상하는 동안 화면 중앙에 툴팁도 하이라이트도 뜨지 않습니다.
/// (아이콘의 위치/회전/페이드는 이 컴포넌트가 <see cref="_TickIcon"/> 에서 계산합니다)
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

    [Tooltip("manager 가 비어 있을 때 GameObject.Find 로 찾을 오브젝트 이름.")]
    [SerializeField] private string managerObjectName = "ExhibitManager";

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
    [Tooltip("ⓘ 아이콘을 Interact 할 수 있는 거리(m). 기본값 2m.\n" +
             "Editor 도구(Setup)가 이 값을 아이콘의 UdonBehaviour 에 구워 넣습니다. 런타임 변경은 불가합니다.")]
    [Min(0.1f)] [SerializeField] private float interactionProximity = 2f;
#pragma warning restore 0414

    // ---------------------------------------------------------------------
    // Info Icon
    // ---------------------------------------------------------------------

    [Header("Info Icon")]
    [Tooltip("이 작품 옆에 뜨는 ⓘ 아이콘. 이 작품의 유일한 Interact 대상입니다. " +
             "(Setup 이 자동으로 만들고 연결합니다)")]
    [SerializeField] private ExhibitInfoIcon infoIcon;

    [Tooltip("아이콘이 작품의 어느 쪽에 붙을지. Default 면 ExhibitManager 의 값을 따릅니다.")]
    [SerializeField] private ExhibitIconPlacement iconPlacement = ExhibitIconPlacement.Default;

    [Tooltip("체크하면 아래 네 값을 이 작품만 따로 사용합니다. 끄면 ExhibitManager 기본값을 따릅니다.\n" +
             "iconHeightOffset 은 음수가 정당한 값이라 '-1 = 기본값' 같은 sentinel 을 쓸 수 없어 " +
             "bool 하나로 네 값을 함께 게이트합니다.")]
    [SerializeField] private bool overrideIconSettings = false;

    [Tooltip("작품 가장자리와 아이콘 사이 여백(m).")]
    [Min(0f)] [SerializeField] private float iconGap = 0.15f;

    [Tooltip("아이콘 높이 보정(m, 월드 Y). 음수도 정상 값입니다.")]
    [SerializeField] private float iconHeightOffset = 0f;

    [Tooltip("아이콘 한 변의 길이(m). Editor 의 Setup 이 아이콘 Scale/Collider 에 구워 넣습니다.")]
    [Min(0.01f)] [SerializeField] private float iconSize = 0.08f;

    [Tooltip("이 거리(m) 밖에서는 아이콘이 뜨지 않습니다.")]
    [Min(0.5f)] [SerializeField] private float gazeDistance = 6f;

    // ---------------------------------------------------------------------
    // 에디터가 굽는 기하 정보 (사람이 고칠 값이 아니므로 Inspector 에서 숨깁니다)
    //
    // Setup 이 실행될 때마다 무조건 덮어씁니다. 굽는 것이 배치 "결과" 가 아니라 "기하" 라서
    // 사용자의 수동 보정을 보존할 이유가 없고, 그래서 작품 Mesh 를 교체·이동·스케일해도
    // 아이콘이 자동으로 따라갑니다.
    //
    // iconSize 만 에디터가 함께 굽는 값입니다(아이콘 Scale/Collider). 나머지 아이콘 설정은
    // 어차피 매 프레임 위치 계산에 쓰이므로 런타임이 Manager fallback 과 함께 해석합니다.
    // ---------------------------------------------------------------------

    [HideInInspector] [SerializeField] private Vector3 boundsCenterLocal;
    [HideInInspector] [SerializeField] private Vector3 boundsExtentsLocal;

    /// <summary>0 = X, 1 = Y, 2 = Z. <b>부호는 없습니다.</b> 부호는 런타임이 플레이어 위치로 정합니다.</summary>
    [HideInInspector] [SerializeField] private int thinAxis;

    // ---------------------------------------------------------------------
    // Runtime
    // ---------------------------------------------------------------------

    private bool _started;
    private bool _warnedNoManager;

    // 아이콘 상태 (ExhibitManager 의 단일 Update 가 매 프레임 갱신합니다)
    private float _iconAlpha;
    private Vector3 _iconPosition;
    private Quaternion _iconRotation;
    private Vector3 _iconDirection;      // 작품 중심 -> 아이콘. Panel 을 밀어낼 방향도 이것입니다.
    private string _interactText = "";

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

    // ---------------------------------------------------------------------
    // Public API (버튼 / 다른 스크립트에서 호출 가능)
    // ---------------------------------------------------------------------

    public void _OpenOverlay()
    {
        if (!Utilities.IsValid(overlay)) return;

        // 응시형 작품은 아이콘 자리에서 펼칩니다.
        // (버튼이나 다른 스크립트가 이 함수를 직접 부르는 경우도 같은 위치를 씁니다)
        if (_HasInfoIcon()) { _OpenOverlayAtIcon(); return; }

        GameObject overlayObject = overlay.gameObject;
        if (!overlayObject.activeSelf) overlayObject.SetActive(true);

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

    // ---------------------------------------------------------------------
    // Info Icon - Public API (ExhibitManager / ExhibitInfoIcon 이 호출)
    // ---------------------------------------------------------------------

    /// <summary>
    /// 이 작품이 응시형 아이콘 방식인지. <b>신규/레거시 판별 기준은 이것 하나뿐입니다.</b>
    /// (모드 플래그를 따로 두지 않습니다 - Hierarchy 만 봐도 어느 방식인지 알 수 있습니다)
    ///
    /// 기하 값이 0 이면 아직 Setup 을 돌리지 않은 것이므로 아이콘 로직을 건너뜁니다.
    /// 그래야 아이콘을 손으로 붙여만 둔 중간 상태에서 아이콘이 작품 중심에 박히지 않습니다.
    /// </summary>
    public bool _HasInfoIcon()
    {
        if (!Utilities.IsValid(infoIcon)) return false;
        return boundsExtentsLocal.sqrMagnitude > 0f;
    }

    /// <summary>작품 Bounds 중심의 월드 좌표. 근접 스캔과 시선 판정의 기준점입니다.</summary>
    public Vector3 _GetIconCenter()
    {
        return transform.TransformPoint(boundsCenterLocal);
    }

    /// <summary>이 거리(m) 밖에서는 아이콘이 뜨지 않습니다.</summary>
    public float _GetGazeDistance()
    {
        if (overrideIconSettings) return gazeDistance;
        if (Utilities.IsValid(manager)) return manager.defaultGazeDistance;
        return gazeDistance;
    }

    public bool _IsOverlayOpen()
    {
        return Utilities.IsValid(overlay) && overlay.IsOpen;
    }

    /// <summary>
    /// 배치 방향을 int 로 돌려줍니다. (Default 면 Manager 값으로 치환)
    /// enum 이 sentinel(Default) 을 자연스럽게 표현하므로 방향만 override 와 무관하게 단독 상속됩니다.
    /// </summary>
    public int _GetIconPlacementIndex()
    {
        ExhibitIconPlacement resolved = iconPlacement;

        if (resolved == ExhibitIconPlacement.Default)
        {
            resolved = Utilities.IsValid(manager) ? manager.defaultIconPlacement : ExhibitIconPlacement.Right;
        }

        // Manager 쪽도 Default 로 남겨 둔 구성에서 아이콘이 사라지지 않도록 마지막 방어선을 둡니다.
        if (resolved == ExhibitIconPlacement.Default) resolved = ExhibitIconPlacement.Right;

        return (int)resolved;
    }

    /// <summary>
    /// ExhibitManager 의 단일 Update 가 <b>응시 후보(_near)</b> 에 든 작품에만 매 프레임 호출합니다.
    ///
    /// <b>이 함수가 이 설계의 핵심입니다.</b> 에디터는 기하(중심 / extents / 얇은 축)만 굽고,
    /// 배치의 부호(+/-)는 여기서 플레이어 머리 위치로 정합니다.
    ///
    ///   side = sign( dot(head - center, thinAxisWorld) )
    ///
    /// 시선 판정을 위해 어차피 머리 위치를 읽으므로 이 값은 공짜입니다. 벽에 걸린 액자는
    /// 플레이어가 뒤에 설 수 없으니 항상 정답이고, 좌대 위 조각처럼 사방에서 보는 작품은
    /// 관람자를 따라 아이콘이 옮겨 다닙니다.
    ///
    /// 이 결정 하나로 1.0.x 의 다음이 전부 사라졌습니다: 에디터의 정면 부호 추측
    /// (<c>GetFrontNormalAxis</c> 의 "+ 를 정면으로 봄"), 캔버스 반전 규약
    /// (<c>GetOverlayRotation</c>), <c>OverlayAnchor</c> 수동 180도 보정.
    ///
    /// <b>고개를 돌려도 아이콘은 움직이지 않습니다.</b> dir 을 머리 forward 가 아니라
    /// <i>작품 → 플레이어</i> 방향에서 뽑기 때문입니다. 제자리에서 고개만 돌릴 때는 반응하지 않고,
    /// 실제로 걸어서 이동해야 옆으로 옮겨 갑니다.
    /// </summary>
    public void _TickIcon(Vector3 headPosition, Vector3 headForward, float deltaTime,
                          float cosEnter, float cosExit)
    {
        if (!_HasInfoIcon()) return;

        Vector3 center = _GetIconCenter();
        Vector3 toHead = headPosition - center;

        // ---- 부호 결정: 얇은 축 중 플레이어가 서 있는 쪽 ---------------------
        Vector3 axis = transform.rotation * _AxisVector(thinAxis);
        float side = Vector3.Dot(toHead, axis) >= 0f ? 1f : -1f;
        Vector3 front = axis * side;                       // 작품 -> 플레이어

        // ---- 아이콘을 밀어낼 방향 -------------------------------------------
        Vector3 flatFront = new Vector3(front.x, 0f, front.z);

        // 바닥에 눕힌 작품(좌대 위 평면 작품 등)은 front 가 거의 수직이라 Right/Left 가
        // 불안정합니다. 그때만 머리 forward 를 XZ 평면에 투영한 값으로 대체합니다.
        if (flatFront.sqrMagnitude < 0.0001f)
        {
            flatFront = new Vector3(headForward.x, 0f, headForward.z);
        }
        // 똑바로 위/아래를 보고 있으면 그것마저 0 이 됩니다. 정규화 폭주(NaN)를 막습니다.
        if (flatFront.sqrMagnitude < 0.0001f) flatFront = Vector3.forward;
        flatFront = flatFront.normalized;

        int placement = _GetIconPlacementIndex();
        Vector3 direction;

        // Above / Below 는 월드 Y 고정이라 바닥에 눕힌 작품에도 영향을 받지 않습니다.
        if (placement == (int)ExhibitIconPlacement.Above) direction = Vector3.up;
        else if (placement == (int)ExhibitIconPlacement.Below) direction = Vector3.down;
        // 관람자 기준 오른쪽은 -Cross(up, flatFront) 입니다.
        // (flatFront = -관람자forward 이므로 Cross(up, flatFront) = -관람자right)
        // 이 부호와 아래 iconRotation 방향은 ClientSim 실기 스크린샷으로 확정한 값입니다.
        // 식만 보고 뒤집지 마세요 - 과거 Panel 좌우 반전 회귀가 있었던 지점입니다.
        else if (placement == (int)ExhibitIconPlacement.Left) direction = Vector3.Cross(Vector3.up, flatFront);
        else direction = -Vector3.Cross(Vector3.up, flatFront);

        // ---- 작품 OBB 의 direction 방향 반폭 ---------------------------------
        // extents 는 Exhibit Root 의 로컬 단위이고, 에디터 도구가 Root 의 월드 Scale 을 1 로
        // 맞춰 두므로(NeutralizeWorldScale) 그대로 m 로 씁니다.
        float halfExtent =
            Mathf.Abs(Vector3.Dot(direction, transform.right)) * boundsExtentsLocal.x +
            Mathf.Abs(Vector3.Dot(direction, transform.up)) * boundsExtentsLocal.y +
            Mathf.Abs(Vector3.Dot(direction, transform.forward)) * boundsExtentsLocal.z;

        Vector3 iconPosition = center
            + direction * (halfExtent + _IconGap())
            + Vector3.up * _IconHeightOffset()
            + front * 0.02f;               // 벽/작품 표면에 파묻히지 않도록 살짝 앞으로

        // World Space Canvas 는 자기 forward 의 <반대쪽>에 선 사람에게 글자가 정방향으로 읽힙니다.
        // 그래서 관람자를 "바라보게" 하지 않고, 관람자에게서 멀어지는 쪽을 forward 로 둡니다.
        // (판정식: dot(관람자 - 아이콘, 아이콘 forward) < 0.
        //  각도만 보는 검증(Vector3.Angle ≈ 0)은 180도 뒤집힌 캔버스도 통과시킵니다)
        Quaternion iconRotation = Quaternion.LookRotation(iconPosition - headPosition, Vector3.up);

        _iconPosition = iconPosition;
        _iconRotation = iconRotation;
        _iconDirection = direction;

        // ---- 시선 판정 (히스테리시스) ---------------------------------------
        // 아이콘을 조준하려고 고개를 돌리면 작품 중심이 시야에서 벗어나 아이콘이 사라지는
        // 자기모순이 생깁니다. 그래서 "나타남" 과 "유지" 의 조건을 분리합니다.
        GameObject iconObject = infoIcon.gameObject;
        bool showing = iconObject.activeSelf;
        bool hold;

        if (_IsOverlayOpen())
        {
            hold = true;                   // 읽는 중에는 시선이 벗어나도 유지합니다.
        }
        else if (showing)
        {
            hold = _WithinCone(center - headPosition, headForward, cosExit) ||
                   _WithinCone(iconPosition - headPosition, headForward, cosExit);
        }
        else
        {
            hold = _WithinCone(center - headPosition, headForward, cosEnter);
        }

        _ApplyIconVisual(hold, deltaTime);
    }

    /// <summary>
    /// 아이콘을 애니메이션 없이 즉시 숨깁니다. 근접 범위(_near)를 벗어났거나
    /// Manager 가 꺼져 Update 가 멈출 때 호출됩니다. (반투명한 중간 상태로 굳는 것을 막습니다)
    /// </summary>
    public void _HideIcon()
    {
        if (!Utilities.IsValid(infoIcon)) return;

        _iconAlpha = 0f;

        GameObject iconObject = infoIcon.gameObject;
        if (!iconObject.activeSelf) return;

        // 비활성화하기 전에 alpha 를 0 으로 만들어 둡니다. 꺼진 뒤에는 이벤트가 전달되지 않아
        // 다음에 켤 때 예전 alpha 로 한 프레임 번쩍입니다.
        infoIcon._SetAlpha(0f);
        iconObject.SetActive(false);
    }

    /// <summary>
    /// <see cref="ExhibitInfoIcon"/> 이 Interact 를 받았을 때 호출합니다.
    /// 같은 아이콘을 다시 누르면 Toggle 로 닫힙니다. (작품 Interact 와 같은 규칙)
    /// </summary>
    public void _OnIconInteract()
    {
        if (!Utilities.IsValid(overlay)) return;

        _EnsureManager();

        if (overlay.IsOpen) { _CloseOverlay(); return; }

        // _OpenOverlay 가 아이콘 자리 / 제자리 배치를 알아서 고릅니다. (분기를 한 곳에만 둡니다)
        _OpenOverlay();
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

    // ---------------------------------------------------------------------
    // Info Icon - Internal
    // ---------------------------------------------------------------------

    /// <summary>
    /// Overlay 를 <b>아이콘의 안쪽 가장자리가 맞닿는 위치</b>로 열어 "아이콘에서 펼쳐지는" 연출을 만듭니다.
    ///
    /// ExhibitOverlay 의 기존 scale 애니메이션(startScaleMultiplier 0.92 -> 1.0)이 그대로 그 연출이
    /// 되므로 애니메이션 코드는 손대지 않았습니다. 열려 있는 동안 위치는 고정입니다
    /// (플레이어를 따라다니지 않습니다).
    /// </summary>
    private void _OpenOverlayAtIcon()
    {
        GameObject overlayObject = overlay.gameObject;
        if (!overlayObject.activeSelf) overlayObject.SetActive(true);

        // Above / Below 는 direction 이 수직이므로 폭이 아니라 높이 반값을 씁니다.
        int placement = _GetIconPlacementIndex();
        bool vertical = placement == (int)ExhibitIconPlacement.Above ||
                        placement == (int)ExhibitIconPlacement.Below;

        float halfSpan = vertical ? overlay._GetWorldHalfHeight() : overlay._GetWorldHalfWidth();

        overlayObject.transform.SetPositionAndRotation(
            _iconPosition + _iconDirection * halfSpan, _iconRotation);

        _PushContent();
        overlay._Open(manager);
    }

    /// <summary>축 인덱스(0 = X, 1 = Y, 2 = Z)를 단위 벡터로. 부호는 붙이지 않습니다.</summary>
    private Vector3 _AxisVector(int axis)
    {
        if (axis == 0) return Vector3.right;
        if (axis == 1) return Vector3.up;
        return Vector3.forward;
    }

    /// <summary>
    /// <paramref name="toTarget"/> 이 <paramref name="headForward"/> 기준 원뿔 안에 있는지.
    /// 각도 대신 <c>dot(normalize(target - head), forward) &gt; cos(threshold)</c> 로 비교합니다.
    /// (Acos 을 매 프레임 부르지 않아도 되고, cos 값은 Manager 가 캐시합니다)
    /// </summary>
    private bool _WithinCone(Vector3 toTarget, Vector3 headForward, float cosThreshold)
    {
        // 머리와 겹칠 만큼 가까우면 방향이 의미가 없습니다. 사라지게 하지 않습니다.
        if (toTarget.sqrMagnitude < 0.000001f) return true;
        return Vector3.Dot(toTarget.normalized, headForward) > cosThreshold;
    }

    private void _ApplyIconVisual(bool hold, float deltaTime)
    {
        GameObject iconObject = infoIcon.gameObject;

        if (hold)
        {
            if (!iconObject.activeSelf)
            {
                _iconAlpha = 0f;
                iconObject.SetActive(true);

                // 꺼져 있는 동안에는 이벤트가 전달되지 않으므로 켜는 순간 현재 언어 문구를 다시 밀어 넣습니다.
                infoIcon._SetInteractText(_interactText);
            }

            _iconAlpha = _StepAlpha(_iconAlpha, 1f, deltaTime);
        }
        else
        {
            if (!iconObject.activeSelf) return;

            _iconAlpha = _StepAlpha(_iconAlpha, 0f, deltaTime);

            if (_iconAlpha <= 0f)
            {
                infoIcon._SetAlpha(0f);
                iconObject.SetActive(false);
                return;
            }
        }

        infoIcon._SetAlpha(_iconAlpha);
        infoIcon.transform.SetPositionAndRotation(_iconPosition, _iconRotation);
    }

    private float _StepAlpha(float current, float target, float deltaTime)
    {
        float duration = _IconFadeDuration();
        if (duration <= 0.0001f) return target;

        float step = deltaTime / duration;

        if (current < target)
        {
            current += step;
            if (current > target) current = target;
        }
        else if (current > target)
        {
            current -= step;
            if (current < target) current = target;
        }

        return current;
    }

    private float _IconGap()
    {
        if (overrideIconSettings) return iconGap;
        if (Utilities.IsValid(manager)) return manager.defaultIconGap;
        return iconGap;
    }

    private float _IconHeightOffset()
    {
        if (overrideIconSettings) return iconHeightOffset;
        if (Utilities.IsValid(manager)) return manager.defaultIconHeightOffset;
        return iconHeightOffset;
    }

    private float _IconFadeDuration()
    {
        if (Utilities.IsValid(manager)) return manager.iconFadeDuration;
        return 0.12f;
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

        // 아이콘이 꺼져 있는 동안에는 이벤트가 전달되지 않으므로, 켜는 순간 다시 밀어 넣도록 보관합니다.
        _interactText = text;

        if (Utilities.IsValid(infoIcon) && infoIcon.gameObject.activeInHierarchy)
        {
            infoIcon._SetInteractText(text);
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
