using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;

/// <summary>
/// 작품 1개에 종속되는 설명 Overlay.
///
/// - Fade(CanvasGroup.alpha) + Scale 로 Open/Close 애니메이션을 재생합니다.
/// - 애니메이션/스크롤이 필요한 동안에만 ExhibitManager 의 Tick 목록에 등록됩니다.
///   (자체 Update() 없음 -> 작품이 100개여도 유휴 상태에서는 비용이 0 입니다.)
/// - ScrollRect 대신 Content 의 anchoredPosition 을 직접 제어합니다.
///   (Udon 에서 100% 안전하고, Up/Down 버튼 스크롤이 부드럽게 보간됩니다.)
/// - 오브젝트가 비활성화되면 스스로 상태를 초기화합니다. (작품 Disable 시 자동 Close)
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ExhibitOverlay : UdonSharpBehaviour
{
    // 다른 UdonSharpBehaviour 에서 "필드 읽기" 로 안전하게 참조할 수 있는 상태값입니다.
    // (메서드 호출과 달리 비활성 상태에서도 안전하게 읽힙니다.)
    [HideInInspector] public bool IsOpen;

    // ---------------------------------------------------------------------
    // References
    // ---------------------------------------------------------------------

    [Header("References")]
    [Tooltip("Fade 에 사용할 CanvasGroup. 보통 Overlay Root(Canvas) 에 붙입니다.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("Scale 애니메이션 대상. 보통 Canvas 아래의 Panel 을 지정합니다.")]
    [SerializeField] private Transform scaleRoot;

    [Header("Texts")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Header("Scroll")]
    [Tooltip("마스킹되는 영역(Viewport)의 RectTransform.")]
    [SerializeField] private RectTransform scrollViewport;

    [Tooltip("실제로 움직이는 Content 의 RectTransform. (ContentSizeFitter 필요)")]
    [SerializeField] private RectTransform scrollContent;

    [Header("Buttons")]
    [Tooltip("이 Overlay 에 속한 버튼들. 언어 전환 시 Interact 문구를 갱신합니다.")]
    [SerializeField] private ExhibitOverlayButton[] buttons = new ExhibitOverlayButton[0];

    // ---------------------------------------------------------------------
    // Animation Settings
    // ---------------------------------------------------------------------

    [Header("Open / Close Animation")]
    [Tooltip("열릴 때 걸리는 시간(초). VR 에서는 0.15 ~ 0.25 를 권장합니다.")]
    [Min(0.01f)] [SerializeField] private float openDuration = 0.18f;

    [Tooltip("닫힐 때 걸리는 시간(초).")]
    [Min(0.01f)] [SerializeField] private float closeDuration = 0.12f;

    [Tooltip("애니메이션 시작 스케일 배율. 1 에 가까울수록 얌전합니다.")]
    [Range(0.5f, 1f)] [SerializeField] private float startScaleMultiplier = 0.92f;

    [Tooltip("닫힘 완료 시 GameObject 를 비활성화합니다. (렌더링/배칭 비용 제거)")]
    [SerializeField] private bool deactivateWhenClosed = true;

    // ---------------------------------------------------------------------
    // Scroll Settings
    // ---------------------------------------------------------------------

    [Header("Scroll Settings")]
    [Tooltip("Up/Down 버튼 1회당 스크롤 양(px, Canvas 로컬 단위). 정규화 비율이 아니라 픽셀이라 " +
             "글이 길든 짧든 한 번에 넘어가는 양이 일정합니다.")]
    [Min(1f)] [SerializeField] private float scrollStep = 120f;

    [Tooltip("스크롤 보간 속도. 클수록 즉각적입니다. 0 이면 즉시 이동.")]
    [Min(0f)] [SerializeField] private float scrollSmoothSpeed = 14f;

    // ---------------------------------------------------------------------
    // Runtime State
    // ---------------------------------------------------------------------

    private const int STATE_CLOSED = 0;
    private const int STATE_OPENING = 1;
    private const int STATE_OPEN = 2;
    private const int STATE_CLOSING = 3;

    private int _state;
    private float _t;              // 0 = 완전히 닫힘, 1 = 완전히 열림
    private bool _initialized;
    private bool _ticking;
    private bool _warnedNoManager;

    private Vector3 _baseScale = Vector3.one;

    private float _currentScroll;
    private float _targetScroll;

    private ExhibitManager _manager;

    // ---------------------------------------------------------------------
    // Unity / Udon Events
    // ---------------------------------------------------------------------

    void OnDisable()
    {
        // 작품(부모)이 꺼지면 여기도 함께 꺼집니다. 상태를 확실히 초기화해서
        // 다시 켰을 때 "열린 채로 남아 있는" 문제를 방지합니다.
        // (_baseScale 을 먼저 확보해야 축소된 스케일을 원본으로 오인하지 않습니다.)
        _EnsureInit();

        _state = STATE_CLOSED;
        _t = 0f;
        IsOpen = false;
        _ticking = false;
        _currentScroll = 0f;
        _targetScroll = 0f;

        _ApplyVisual();
        _ApplyScroll();

        // Manager 는 비활성 Overlay 를 Tick 목록에서 자동으로 제거하므로
        // 여기서 Manager 를 호출하지 않습니다. (재진입 방지)
    }

    // ---------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------

    /// <summary>Overlay 를 엽니다. GameObject 는 호출 전에 활성화되어 있어야 합니다.</summary>
    public void _Open(ExhibitManager exhibitManager)
    {
        _EnsureInit();

        if (Utilities.IsValid(exhibitManager)) _manager = exhibitManager;

        // 버튼 Interact 문구를 현재 언어로 맞춥니다.
        if (Utilities.IsValid(_manager)) _ApplyLanguage(_manager._GetLanguageIndex());

        // 열 때마다 스크롤을 맨 위로 되돌립니다.
        _currentScroll = 0f;
        _targetScroll = 0f;
        _ApplyScroll();

        if (_state == STATE_OPEN) return;

        _state = STATE_OPENING;
        IsOpen = true;

        if (Utilities.IsValid(canvasGroup))
        {
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        _ApplyVisual();
        _RequestTick();
    }

    /// <summary>Overlay 를 닫습니다. (역방향 Fade + Scale)</summary>
    public void _Close()
    {
        _EnsureInit();

        if (_state == STATE_CLOSED) return;

        _state = STATE_CLOSING;

        // 닫히는 중에는 "닫힌 것"으로 취급합니다.
        // 덕분에 닫히는 도중 작품을 다시 Interact 하면 현재 진행률에서 부드럽게 되열립니다.
        IsOpen = false;

        if (Utilities.IsValid(canvasGroup))
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        _RequestTick();
    }

    /// <summary>애니메이션 없이 즉시 닫습니다.</summary>
    public void _CloseImmediate()
    {
        _EnsureInit();

        _state = STATE_CLOSED;
        _t = 0f;
        IsOpen = false;
        _ticking = false;
        _currentScroll = 0f;
        _targetScroll = 0f;

        _ApplyVisual();
        _ApplyScroll();

        if (deactivateWhenClosed && gameObject.activeSelf) gameObject.SetActive(false);
    }

    /// <summary>
    /// Manager 가 비활성화되어 더 이상 Tick 을 받을 수 없을 때 호출됩니다.
    /// 진행 중이던 애니메이션/스크롤을 목표 상태로 즉시 확정합니다.
    /// (Manager 없이 _RequestTick() 이 호출됐을 때와 동일한 동작입니다.)
    /// </summary>
    public void _FinishTickImmediate()
    {
        _EnsureInit();

        // 재등록이 막히지 않도록 틱 플래그를 먼저 풉니다.
        // (Manager 가 다시 켜지면 다음 조작에서 새로 등록됩니다.)
        _ticking = false;

        _FinishImmediate();
    }

    /// <summary>표시할 텍스트를 설정합니다. 빈 문자열이면 해당 요소를 숨깁니다.</summary>
    public void _SetContent(string title, string subtitle, string body)
    {
        if (Utilities.IsValid(titleText))
        {
            bool visible = title != null && title.Length > 0;
            titleText.text = visible ? title : "";
            if (titleText.gameObject.activeSelf != visible) titleText.gameObject.SetActive(visible);
        }

        if (Utilities.IsValid(subtitleText))
        {
            bool visible = subtitle != null && subtitle.Length > 0;
            subtitleText.text = visible ? subtitle : "";
            if (subtitleText.gameObject.activeSelf != visible) subtitleText.gameObject.SetActive(visible);
        }

        if (Utilities.IsValid(descriptionText))
        {
            bool visible = body != null && body.Length > 0;
            descriptionText.text = visible ? body : "";
            if (descriptionText.gameObject.activeSelf != visible) descriptionText.gameObject.SetActive(visible);
        }
    }

    /// <summary>언어 전환 시 버튼의 Interact 문구를 갱신합니다.</summary>
    public void _ApplyLanguage(int languageIndex)
    {
        if (buttons == null) return;

        for (int i = 0; i < buttons.Length; i++)
        {
            ExhibitOverlayButton button = buttons[i];
            if (!Utilities.IsValid(button)) continue;
            if (!button.gameObject.activeInHierarchy) continue;
            button._ApplyLanguage(languageIndex);
        }
    }

    // ---------------------------------------------------------------------
    // Scroll
    // ---------------------------------------------------------------------

    public void _ScrollUp()
    {
        _EnsureInit();
        _SetTargetScroll(_targetScroll - scrollStep);
    }

    public void _ScrollDown()
    {
        _EnsureInit();
        _SetTargetScroll(_targetScroll + scrollStep);
    }

    public void _ScrollToTop()
    {
        _EnsureInit();
        _SetTargetScroll(0f);
    }

    private void _SetTargetScroll(float value)
    {
        float max = _MaxScroll();

        if (value < 0f) value = 0f;
        if (value > max) value = max;

        _targetScroll = value;

        if (scrollSmoothSpeed <= 0f)
        {
            _currentScroll = _targetScroll;
            _ApplyScroll();
            return;
        }

        _RequestTick();
    }

    /// <summary>Content 가 Viewport 보다 얼마나 더 긴지(px). 0 이면 스크롤 불필요.</summary>
    private float _MaxScroll()
    {
        if (!Utilities.IsValid(scrollContent) || !Utilities.IsValid(scrollViewport)) return 0f;

        float contentHeight = scrollContent.rect.height;
        float viewportHeight = scrollViewport.rect.height;
        float max = contentHeight - viewportHeight;

        if (max < 0f) max = 0f;
        return max;
    }

    private void _ApplyScroll()
    {
        if (!Utilities.IsValid(scrollContent)) return;

        Vector2 pos = scrollContent.anchoredPosition;
        pos.y = _currentScroll;
        scrollContent.anchoredPosition = pos;
    }

    // ---------------------------------------------------------------------
    // Tick (ExhibitManager 가 매 프레임 호출)
    // ---------------------------------------------------------------------

    /// <summary>true 를 반환하면 다음 프레임에도 계속 Tick 이 필요합니다.</summary>
    public bool _Tick()
    {
        float deltaTime = Time.deltaTime;
        bool busy = false;

        // --- Open / Close 애니메이션 ---
        if (_state == STATE_OPENING)
        {
            float duration = openDuration > 0.0001f ? openDuration : 0.0001f;
            _t += deltaTime / duration;

            if (_t >= 1f)
            {
                _t = 1f;
                _state = STATE_OPEN;
            }
            else
            {
                busy = true;
            }

            _ApplyVisual();
        }
        else if (_state == STATE_CLOSING)
        {
            float duration = closeDuration > 0.0001f ? closeDuration : 0.0001f;
            _t -= deltaTime / duration;

            if (_t <= 0f)
            {
                _t = 0f;
                _state = STATE_CLOSED;
                IsOpen = false;
                _ApplyVisual();

                _ticking = false;

                if (deactivateWhenClosed) gameObject.SetActive(false);
                return false;
            }

            busy = true;
            _ApplyVisual();
        }

        // --- 스크롤 보간 ---
        float diff = _targetScroll - _currentScroll;
        if (diff > 0.5f || diff < -0.5f)
        {
            float k = deltaTime * scrollSmoothSpeed;
            if (k > 1f) k = 1f;
            if (k < 0f) k = 0f;

            _currentScroll = _currentScroll + diff * k;
            _ApplyScroll();
            busy = true;
        }
        else if (_currentScroll != _targetScroll)
        {
            _currentScroll = _targetScroll;
            _ApplyScroll();
        }

        if (!busy) _ticking = false;
        return busy;
    }

    // ---------------------------------------------------------------------
    // Internal
    // ---------------------------------------------------------------------

    private void _EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;

        if (!Utilities.IsValid(scaleRoot)) scaleRoot = transform;
        _baseScale = scaleRoot.localScale;

        // 첫 프레임에 원본 크기로 번쩍이지 않도록 닫힌 상태로 초기화
        if (!IsOpen)
        {
            _t = 0f;
            _ApplyVisual();
        }
    }

    private void _RequestTick()
    {
        // Manager 가용성을 "이미 틱 중인지"보다 먼저 검사합니다.
        // 애니메이션/스크롤 도중에 Manager 가 꺼지면 _ticking 이 true 로 남아 있는데
        // Update() 는 더 이상 돌지 않으므로, 여기서 걸러내지 않으면 Overlay 가 중간 상태로 멈춥니다.
        if (!_ManagerCanTick())
        {
            if (!_warnedNoManager)
            {
                _warnedNoManager = true;
                Debug.LogWarning("[ExhibitOverlay] ExhibitManager 가 없거나 비활성이라 애니메이션 없이 동작합니다: " + gameObject.name);
            }

            // 이미 등록되어 있던 틱은 Manager 가 스스로 정리합니다. (비활성 Overlay/false 반환 시 제거)
            // 여기서는 재요청이 다시 등록될 수 있도록 상태만 풀어 둡니다.
            _ticking = false;

            _FinishImmediate();
            return;
        }

        if (_ticking) return;

        _ticking = true;
        _manager._RegisterTick(this);
    }

    /// <summary>Manager 가 실제로 Update 틱을 돌려줄 수 있는 상태인지 확인합니다.</summary>
    private bool _ManagerCanTick()
    {
        if (!Utilities.IsValid(_manager)) return false;
        if (!_manager.gameObject.activeInHierarchy) return false;

        // 컴포넌트만 비활성화(enabled = false)해도 Update() 는 호출되지 않습니다.
        if (!_manager.enabled) return false;

        return true;
    }

    /// <summary>애니메이션을 건너뛰고 현재 목표 상태로 즉시 확정합니다.</summary>
    private void _FinishImmediate()
    {
        if (_state == STATE_OPENING)
        {
            _t = 1f;
            _state = STATE_OPEN;
            _ApplyVisual();
        }
        else if (_state == STATE_CLOSING)
        {
            _t = 0f;
            _state = STATE_CLOSED;
            IsOpen = false;
            _ApplyVisual();

            if (deactivateWhenClosed) gameObject.SetActive(false);
            return;
        }

        _currentScroll = _targetScroll;
        _ApplyScroll();
    }

    private void _ApplyVisual()
    {
        float e = _Ease(_t);

        if (Utilities.IsValid(canvasGroup)) canvasGroup.alpha = e;

        if (Utilities.IsValid(scaleRoot))
        {
            float s = startScaleMultiplier + (1f - startScaleMultiplier) * e;
            scaleRoot.localScale = new Vector3(_baseScale.x * s, _baseScale.y * s, _baseScale.z * s);
        }
    }

    /// <summary>SmoothStep (ease in-out). 외부 라이브러리 없이 Udon 에서 안전하게 동작합니다.</summary>
    private float _Ease(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return t * t * (3f - 2f * t);
    }
}
