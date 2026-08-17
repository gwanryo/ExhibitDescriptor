using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Scene 당 1개만 존재하는 전시 시스템 관리자.
///
/// 역할
///  1) 현재 언어 상태 보관 및 언어 전환 브로드캐스트
///  2) 모든 Overlay 애니메이션/스크롤을 "단 하나의 Update()" 에서 처리 (Tick 등록 방식)
///  3) 작품 공통 기본값(Interact 문구 등) 제공
///
/// 중요
///  - Networking 을 전혀 사용하지 않습니다. BehaviourSyncMode.None 고정.
///  - 모든 상태는 Local Player 기준입니다.
///  - Update() 는 애니메이션 중인 Overlay 가 하나도 없으면 즉시 return 합니다.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ExhibitManager : UdonSharpBehaviour
{
    // ---------------------------------------------------------------------
    // Inspector
    // ---------------------------------------------------------------------

    [Header("Language")]
    [Tooltip("Scene 시작 시 사용할 기본 언어입니다.")]
    [SerializeField] private ExhibitLanguage defaultLanguage = ExhibitLanguage.KR;

    [Header("Default Interact Settings")]
    [Tooltip("작품에서 Interact 문구를 비워두면 이 값이 사용됩니다. (한국어)")]
    public string defaultInteractionTextKR = "설명";
    [Tooltip("작품에서 Interact 문구를 비워두면 이 값이 사용됩니다. (영어)")]
    public string defaultInteractionTextEN = "Description";
    [Tooltip("작품에서 Interact 문구를 비워두면 이 값이 사용됩니다. (일본어)")]
    public string defaultInteractionTextJP = "説明";

    [Tooltip("Editor Tool 이 '작품별 Proximity' 를 일괄 적용할 때 사용할 기본값(m).")]
    [Min(0.1f)] public float defaultProximity = 2f;

    // ---------------------------------------------------------------------
    // Info Icon (응시형 ⓘ 아이콘) - 전시 전체 기본값
    //
    // 작품이 매 프레임 읽으므로 public 필드입니다. (defaultInteractionTextKR 과 같은 이유)
    // 작품에서 override 를 켜지 않으면 여기 값이 그대로 쓰이므로, 전시 전체 배치를
    // 필드 하나 수정으로 바꿀 수 있습니다.
    // ---------------------------------------------------------------------

    [Header("Info Icon Defaults")]
    [Tooltip("작품이 Default 로 두었을 때 쓰는 아이콘 방향입니다.")]
    public ExhibitIconPlacement defaultIconPlacement = ExhibitIconPlacement.Right;

    [Tooltip("작품 가장자리와 아이콘 사이 여백(m).")]
    [Min(0f)] public float defaultIconGap = 0.15f;

    [Tooltip("아이콘 높이 보정(m, 월드 Y). 음수도 정상 값입니다.")]
    public float defaultIconHeightOffset = 0f;

    [Tooltip("아이콘 한 변의 길이(m). 이 값은 Editor 의 Setup 이 아이콘 Scale/Collider 에 구워 넣습니다.")]
    [Min(0.01f)] public float defaultIconSize = 0.08f;

    [Tooltip("이 거리(m) 밖에서는 아이콘이 뜨지 않습니다.")]
    [Min(0.5f)] public float defaultGazeDistance = 6f;

    [Header("Gaze")]
    [Tooltip("아이콘이 '나타나는' 시선 각도(도). 작품 중심이 이 안에 들어와야 합니다.")]
    [Range(1f, 89f)] [SerializeField] private float gazeEnterAngle = 25f;

    [Tooltip("아이콘이 '유지되는' 시선 각도(도). 아이콘을 조준하려 고개를 돌려도 사라지지 않도록 " +
             "나타나는 조건보다 넓게 둡니다.")]
    [Range(1f, 89f)] [SerializeField] private float gazeExitAngle = 45f;

    [Tooltip("아이콘 페이드 시간(초).")]
    [Min(0f)] public float iconFadeDuration = 0.12f;

    [Tooltip("프레임당 근접 스캔 개수. 작품 100개면 한 바퀴에 약 13프레임(0.2초)입니다.")]
    [Min(1)] [SerializeField] private int iconScanPerFrame = 8;

    // Overlay 폰트 슬롯(TMP_FontAsset)은 이 컴포넌트가 아니라 같은 GameObject 의
    // ExhibitDescriptorSettings(평범한 MonoBehaviour) 에 있습니다. 여기에 둘 수 없는 이유는
    // ExhibitDescriptorSettings 의 주석에 적어 두었습니다. (요약: Udon 타입 화이트리스트)

    [Header("Debug")]
    [Tooltip("등록/언어전환 로그를 콘솔에 출력합니다. 완성 후에는 꺼두세요.")]
    [SerializeField] private bool debugLog = false;

    // ---------------------------------------------------------------------
    // Runtime State
    // ---------------------------------------------------------------------

    private bool _initialized;
    private int _languageIndex;

    // 활성화된 작품 목록 (언어 전환 브로드캐스트 대상)
    private ExhibitInteractable[] _exhibits;
    private int _exhibitCount;

    // 활성화된 언어 전환 버튼 목록 (현재 언어 라벨 동기화 대상)
    private ExhibitLanguageSwitch[] _switches;
    private int _switchCount;

    // 현재 애니메이션/스크롤 중인 Overlay 목록 (매 프레임 Tick 대상)
    private ExhibitOverlay[] _ticks;
    private int _tickCount;

    // 근접 범위 안에 있어 매 프레임 시선 판정을 하는 작품 목록 (보통 0 ~ 2개)
    private ExhibitInteractable[] _near;
    private int _nearCount;

    // 라운드로빈 스캔 커서. _exhibits 는 스왑 제거 방식이라 순서가 바뀔 수 있지만,
    // 커서가 어긋나도 다음 사이클에 다시 훑으므로 문제되지 않습니다.
    private int _scanCursor;

    // 시선 각도의 cos 캐시. Acos 을 매 프레임 부르지 않기 위한 것으로,
    // 인스펙터 값이 바뀔 때만 다시 계산합니다.
    private float _cosGazeEnter;
    private float _cosGazeExit;
    private float _cachedEnterAngle = -1f;
    private float _cachedExitAngle = -1f;

    // ---------------------------------------------------------------------
    // Unity / Udon Events
    // ---------------------------------------------------------------------

    void Start()
    {
        _EnsureInit();
    }

    void Update()
    {
        _TickOverlays();
        _TickIcons();
    }

    /// <summary>Overlay 애니메이션 / 스크롤. 애니메이션 중인 Overlay 가 없으면 즉시 끝납니다.</summary>
    private void _TickOverlays()
    {
        // 애니메이션 중인 Overlay 가 없으면 아무 것도 하지 않습니다.
        if (_tickCount <= 0) return;

        // 역순 순회: 순회 중 제거되어도 안전합니다.
        for (int i = _tickCount - 1; i >= 0; i--)
        {
            if (i >= _tickCount) continue;

            ExhibitOverlay overlay = _ticks[i];

            if (!Utilities.IsValid(overlay))
            {
                _RemoveTickAt(i);
                continue;
            }

            // 비활성화된 Overlay 는 스스로 상태를 정리하므로 목록에서만 제거합니다.
            if (!overlay.gameObject.activeInHierarchy)
            {
                _RemoveTickAt(i);
                continue;
            }

            if (!overlay._Tick())
            {
                if (i < _tickCount && _ticks[i] == overlay) _RemoveTickAt(i);
            }
        }
    }

    /// <summary>
    /// 응시형 아이콘. 2단계로 나눠 유휴 비용을 거리 검사 <see cref="iconScanPerFrame"/> 회로 묶습니다.
    ///
    ///  1) 근접 스캔 (라운드로빈): 작품 100개면 한 바퀴 약 13프레임(0.2초)
    ///  2) 시선 판정: _near 목록만, 매 프레임 (보통 0 ~ 2개)
    ///
    /// 트리거 콜라이더를 100개 추가하지 않으므로 물리 비용은 0 이고, ClientSim 에서
    /// 결정적으로 재현됩니다. <see cref="ExhibitInfoIcon"/> 이 없는 레거시 작품은
    /// 스캔 대상에서 제외합니다.
    /// </summary>
    private void _TickIcons()
    {
        if (_exhibitCount <= 0 && _nearCount <= 0) return;

        VRCPlayerApi player = Networking.LocalPlayer;
        if (!Utilities.IsValid(player)) return;

        // 머리를 한 번만 읽어 모든 작품이 공유합니다. 시선 판정에 어차피 필요한 값이라
        // 아이콘 배치의 부호(side)를 정하는 비용은 공짜입니다.
        VRCPlayerApi.TrackingData head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 headPosition = head.position;
        Vector3 headForward = head.rotation * Vector3.forward;

        _RefreshGazeCos();
        _ScanNear(headPosition);

        float deltaTime = Time.deltaTime;

        // 역순 순회: 순회 중 제거되어도 안전합니다.
        for (int i = _nearCount - 1; i >= 0; i--)
        {
            if (i >= _nearCount) continue;

            ExhibitInteractable exhibit = _near[i];

            if (!Utilities.IsValid(exhibit))
            {
                _RemoveNearAt(i);
                continue;
            }

            if (!exhibit.gameObject.activeInHierarchy)
            {
                _RemoveNearAt(i);
                continue;
            }

            exhibit._TickIcon(headPosition, headForward, deltaTime, _cosGazeEnter, _cosGazeExit);
        }
    }

    /// <summary>각도 → cos 변환은 설정이 바뀔 때만 합니다. (매 프레임 Cos 호출 회피)</summary>
    private void _RefreshGazeCos()
    {
        if (gazeEnterAngle != _cachedEnterAngle)
        {
            _cachedEnterAngle = gazeEnterAngle;
            _cosGazeEnter = Mathf.Cos(gazeEnterAngle * Mathf.Deg2Rad);
        }

        if (gazeExitAngle != _cachedExitAngle)
        {
            _cachedExitAngle = gazeExitAngle;
            _cosGazeExit = Mathf.Cos(gazeExitAngle * Mathf.Deg2Rad);
        }
    }

    /// <summary>_exhibits 를 커서로 돌며 근접 목록을 갱신합니다. 프레임당 iconScanPerFrame 개.</summary>
    private void _ScanNear(Vector3 headPosition)
    {
        if (_exhibitCount <= 0) return;

        int budget = iconScanPerFrame;
        if (budget < 1) budget = 1;
        if (budget > _exhibitCount) budget = _exhibitCount;

        for (int n = 0; n < budget; n++)
        {
            if (_scanCursor >= _exhibitCount) _scanCursor = 0;

            ExhibitInteractable exhibit = _exhibits[_scanCursor];
            _scanCursor++;

            if (!Utilities.IsValid(exhibit)) continue;
            if (!exhibit.gameObject.activeInHierarchy) continue;
            if (!exhibit._HasInfoIcon()) continue;      // 레거시 작품은 대상이 아닙니다.

            float range = exhibit._GetGazeDistance();
            Vector3 toExhibit = exhibit._GetIconCenter() - headPosition;

            // Overlay 를 읽는 중이라면 거리를 벗어나도 유지합니다.
            // (Panel 만 남고 아이콘이 사라지는 어긋난 상태를 막습니다)
            bool near = toExhibit.sqrMagnitude <= range * range || exhibit._IsOverlayOpen();

            if (near) _AddNear(exhibit);
            else _RemoveNear(exhibit);
        }
    }

    private void _AddNear(ExhibitInteractable exhibit)
    {
        for (int i = 0; i < _nearCount; i++)
        {
            if (_near[i] == exhibit) return;
        }

        if (_nearCount >= _near.Length)
        {
            ExhibitInteractable[] grown = new ExhibitInteractable[_near.Length * 2];
            for (int i = 0; i < _nearCount; i++) grown[i] = _near[i];
            _near = grown;
        }

        _near[_nearCount] = exhibit;
        _nearCount++;
    }

    private void _RemoveNear(ExhibitInteractable exhibit)
    {
        for (int i = 0; i < _nearCount; i++)
        {
            if (_near[i] != exhibit) continue;
            _RemoveNearAt(i);
            return;
        }
    }

    /// <summary>목록에서 빼면서 아이콘도 즉시 숨깁니다. (페이드를 이어 줄 주체가 사라지므로)</summary>
    private void _RemoveNearAt(int index)
    {
        if (index < 0 || index >= _nearCount) return;

        ExhibitInteractable exhibit = _near[index];

        _near[index] = _near[_nearCount - 1];
        _near[_nearCount - 1] = null;
        _nearCount--;

        if (!Utilities.IsValid(exhibit)) return;
        if (!exhibit.gameObject.activeInHierarchy) return;
        exhibit._HideIcon();
    }

    /// <summary>
    /// Manager 가 꺼지면 Update() 가 멈춥니다. Overlay 에는 자체 Update() 가 없으므로,
    /// 틱 목록에 남아 있던 Overlay 는 열기/닫기/스크롤 중간 상태로 굳어 버립니다.
    /// (다시 Interact 하기 전까지 상태를 확정할 기회가 없습니다.)
    ///
    /// 그래서 여기서 남은 Overlay 를 목표 상태로 즉시 확정하고 목록을 비웁니다.
    /// 다시 켜지면 Overlay 가 _RequestTick() 으로 새로 등록하므로 목록을 남길 이유가 없습니다.
    /// </summary>
    void OnDisable()
    {
        // Start() 전에 꺼진 경우 배열 자체가 없습니다.
        if (_ticks == null)
        {
            _tickCount = 0;
            return;
        }

        for (int i = 0; i < _tickCount; i++)
        {
            ExhibitOverlay overlay = _ticks[i];

            // 재진입(Overlay 가 정리 도중 다시 등록을 시도하는 경우)에 대비해 먼저 비웁니다.
            _ticks[i] = null;

            // Scene 언로드 중에는 Udon 콜백 순서에 따라 이미 파괴된 Overlay 가 섞일 수 있습니다.
            if (!Utilities.IsValid(overlay)) continue;

            // 비활성 오브젝트/컴포넌트에는 이벤트가 전달되지 않습니다.
            // (비활성 Overlay 는 자신의 OnDisable 에서 이미 상태를 초기화했습니다.)
            if (!overlay.gameObject.activeInHierarchy) continue;
            if (!overlay.enabled) continue;

            overlay._FinishTickImmediate();
        }

        _tickCount = 0;

        // 아이콘도 같은 이유로 즉시 숨깁니다. Manager 가 꺼지면 페이드를 이어 줄 주체가 없어
        // 아이콘이 반투명한 중간 상태로 굳어 버립니다.
        if (_near == null)
        {
            _nearCount = 0;
            return;
        }

        for (int i = 0; i < _nearCount; i++)
        {
            ExhibitInteractable exhibit = _near[i];
            _near[i] = null;

            if (!Utilities.IsValid(exhibit)) continue;
            if (!exhibit.gameObject.activeInHierarchy) continue;

            exhibit._HideIcon();
        }

        _nearCount = 0;
    }

    // ---------------------------------------------------------------------
    // Init
    // ---------------------------------------------------------------------

    private void _EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;

        _languageIndex = (int)defaultLanguage;

        _exhibits = new ExhibitInteractable[16];
        _exhibitCount = 0;

        _switches = new ExhibitLanguageSwitch[4];
        _switchCount = 0;

        _ticks = new ExhibitOverlay[8];
        _tickCount = 0;

        _near = new ExhibitInteractable[8];
        _nearCount = 0;
        _scanCursor = 0;
    }

    // ---------------------------------------------------------------------
    // Language
    // ---------------------------------------------------------------------

    /// <summary>현재 언어 인덱스(0=KR, 1=EN, 2=JP)를 반환합니다.</summary>
    public int _GetLanguageIndex()
    {
        _EnsureInit();
        return _languageIndex;
    }

    /// <summary>언어를 전환하고 활성화된 모든 작품에 즉시 반영합니다.</summary>
    public void _SetLanguage(ExhibitLanguage language)
    {
        _SetLanguageIndex((int)language);
    }

    /// <summary>언어를 인덱스로 전환합니다. (0=KR, 1=EN, 2=JP)</summary>
    public void _SetLanguageIndex(int index)
    {
        _EnsureInit();

        if (index < 0) index = 0;
        if (index > 2) index = 2;
        if (index == _languageIndex) return;

        _languageIndex = index;

        for (int i = 0; i < _exhibitCount; i++)
        {
            ExhibitInteractable exhibit = _exhibits[i];
            if (!Utilities.IsValid(exhibit)) continue;
            if (!exhibit.gameObject.activeInHierarchy) continue;
            exhibit._OnLanguageChanged();
        }

        // 언어 전환 버튼이 여러 개여도 모든 라벨이 같은 언어를 표시하도록 함께 갱신합니다.
        // (비활성 버튼은 다시 활성화될 때 OnEnable 에서 스스로 갱신합니다.)
        for (int i = 0; i < _switchCount; i++)
        {
            ExhibitLanguageSwitch languageSwitch = _switches[i];
            if (!Utilities.IsValid(languageSwitch)) continue;
            if (!languageSwitch.gameObject.activeInHierarchy) continue;
            languageSwitch._OnLanguageChanged();
        }

        if (debugLog) Debug.Log("[ExhibitManager] Language -> " + index +
                                " (exhibits: " + _exhibitCount + ", switches: " + _switchCount + ")");
    }

    /// <summary>KR -> EN -> JP -> KR 순으로 순환합니다. (UI 버튼 1개로 전환할 때 사용)</summary>
    public void _CycleLanguage()
    {
        _EnsureInit();
        int next = _languageIndex + 1;
        if (next > 2) next = 0;
        _SetLanguageIndex(next);
    }

    // 언어 전환 버튼에서 SendCustomEvent 로 직접 부를 수 있는 헬퍼들
    public void _SetLanguageKR() { _SetLanguageIndex(0); }
    public void _SetLanguageEN() { _SetLanguageIndex(1); }
    public void _SetLanguageJP() { _SetLanguageIndex(2); }

    /// <summary>언어 인덱스에 맞는 문자열을 고릅니다. 비어 있으면 KR -> EN 순으로 fallback 합니다.</summary>
    public string _PickLocalized(string kr, string en, string jp, int languageIndex)
    {
        string result;
        if (languageIndex == 1) result = en;
        else if (languageIndex == 2) result = jp;
        else result = kr;

        if (result == null || result.Length == 0) result = kr;
        if (result == null || result.Length == 0) result = en;
        if (result == null || result.Length == 0) result = jp;
        if (result == null) result = "";
        return result;
    }

    // ---------------------------------------------------------------------
    // Exhibit Registry (언어 전환 대상)
    // ---------------------------------------------------------------------

    /// <summary>작품이 활성화될 때 스스로 등록합니다. Inspector 수동 연결이 필요 없습니다.</summary>
    public void _RegisterExhibit(ExhibitInteractable exhibit)
    {
        _EnsureInit();
        if (!Utilities.IsValid(exhibit)) return;

        for (int i = 0; i < _exhibitCount; i++)
        {
            if (_exhibits[i] == exhibit) return;
        }

        if (_exhibitCount >= _exhibits.Length)
        {
            ExhibitInteractable[] grown = new ExhibitInteractable[_exhibits.Length * 2];
            for (int i = 0; i < _exhibitCount; i++) grown[i] = _exhibits[i];
            _exhibits = grown;
        }

        _exhibits[_exhibitCount] = exhibit;
        _exhibitCount++;

        if (debugLog) Debug.Log("[ExhibitManager] Register exhibit: " + exhibit.gameObject.name + " (" + _exhibitCount + ")");
    }

    /// <summary>작품이 비활성화될 때 스스로 등록 해제합니다.</summary>
    public void _UnregisterExhibit(ExhibitInteractable exhibit)
    {
        _EnsureInit();
        if (!Utilities.IsValid(exhibit)) return;

        // 근접 목록에서도 빼고 아이콘을 숨깁니다. (남겨 두면 꺼진 작품의 아이콘이 떠 있게 됩니다)
        _RemoveNear(exhibit);

        for (int i = 0; i < _exhibitCount; i++)
        {
            if (_exhibits[i] != exhibit) continue;

            _exhibits[i] = _exhibits[_exhibitCount - 1];
            _exhibits[_exhibitCount - 1] = null;
            _exhibitCount--;
            return;
        }
    }

    // ---------------------------------------------------------------------
    // Language Switch Registry (현재 언어 라벨 동기화 대상)
    // ---------------------------------------------------------------------

    /// <summary>언어 전환 버튼이 활성화될 때 스스로 등록합니다.</summary>
    public void _RegisterLanguageSwitch(ExhibitLanguageSwitch languageSwitch)
    {
        _EnsureInit();
        if (!Utilities.IsValid(languageSwitch)) return;

        for (int i = 0; i < _switchCount; i++)
        {
            if (_switches[i] == languageSwitch) return;
        }

        if (_switchCount >= _switches.Length)
        {
            ExhibitLanguageSwitch[] grown = new ExhibitLanguageSwitch[_switches.Length * 2];
            for (int i = 0; i < _switchCount; i++) grown[i] = _switches[i];
            _switches = grown;
        }

        _switches[_switchCount] = languageSwitch;
        _switchCount++;

        if (debugLog) Debug.Log("[ExhibitManager] Register language switch: " + languageSwitch.gameObject.name + " (" + _switchCount + ")");
    }

    /// <summary>언어 전환 버튼이 비활성화될 때 스스로 등록 해제합니다.</summary>
    public void _UnregisterLanguageSwitch(ExhibitLanguageSwitch languageSwitch)
    {
        _EnsureInit();
        if (!Utilities.IsValid(languageSwitch)) return;

        for (int i = 0; i < _switchCount; i++)
        {
            if (_switches[i] != languageSwitch) continue;

            _switches[i] = _switches[_switchCount - 1];
            _switches[_switchCount - 1] = null;
            _switchCount--;
            return;
        }
    }

    // ---------------------------------------------------------------------
    // Tick Registry (Overlay 애니메이션 / 스크롤)
    // ---------------------------------------------------------------------

    /// <summary>Overlay 가 애니메이션/스크롤을 시작할 때 호출합니다.</summary>
    public void _RegisterTick(ExhibitOverlay overlay)
    {
        _EnsureInit();
        if (!Utilities.IsValid(overlay)) return;

        for (int i = 0; i < _tickCount; i++)
        {
            if (_ticks[i] == overlay) return;
        }

        if (_tickCount >= _ticks.Length)
        {
            ExhibitOverlay[] grown = new ExhibitOverlay[_ticks.Length * 2];
            for (int i = 0; i < _tickCount; i++) grown[i] = _ticks[i];
            _ticks = grown;
        }

        _ticks[_tickCount] = overlay;
        _tickCount++;
    }

    private void _RemoveTickAt(int index)
    {
        if (index < 0 || index >= _tickCount) return;

        _ticks[index] = _ticks[_tickCount - 1];
        _ticks[_tickCount - 1] = null;
        _tickCount--;
    }

    // ---------------------------------------------------------------------
    // Debug helpers
    // ---------------------------------------------------------------------

    /// <summary>ClientSim 확인용. 현재 등록된 작품 수를 로그로 출력합니다.</summary>
    public void _LogState()
    {
        _EnsureInit();
        Debug.Log("[ExhibitManager] lang=" + _languageIndex + " exhibits=" + _exhibitCount +
                  " switches=" + _switchCount + " ticking=" + _tickCount + " near=" + _nearCount);
    }
}
