using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;

/// <summary>
/// 월드에 배치하는 언어 전환 버튼. (Interact 방식)
///
/// 사용법
///  A) 언어별 버튼 3개: cycleLanguage = false, targetLanguage 를 KR / EN / JP 로 각각 지정
///  B) 버튼 1개로 순환:  cycleLanguage = true
///
/// 필수 조건
///  - 이 컴포넌트가 붙은 GameObject 에 Collider 가 있어야 합니다. (Layer: Default)
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ExhibitLanguageSwitch : UdonSharpBehaviour
{
    [Header("References")]
    [Tooltip("비워두면 실행 시 이름으로 자동 탐색합니다.")]
    [SerializeField] private ExhibitManager manager;
    [SerializeField] private string managerObjectName = "ExhibitManager";

    [Tooltip("현재 언어를 표시할 라벨(선택).")]
    [SerializeField] private TextMeshProUGUI label;

    [Header("Behaviour")]
    [Tooltip("true 면 KR -> EN -> JP -> KR 로 순환합니다.")]
    [SerializeField] private bool cycleLanguage = true;

    [Tooltip("cycleLanguage 가 false 일 때 이 언어로 고정 전환합니다.")]
    [SerializeField] private ExhibitLanguage targetLanguage = ExhibitLanguage.KR;

    [Header("Interact Text")]
    [SerializeField] private string interactionText = "Language";

    private bool _registered;
    private bool _warnedNoManager;

    void Start()
    {
        _EnsureManager();

        InteractionText = interactionText;

        _Register();
        _RefreshLabel();
    }

    void OnEnable()
    {
        _EnsureManager();

        // 비활성 상태에서 언어가 바뀌었을 수 있으므로 다시 등록하고 라벨을 맞춥니다.
        _Register();
        _RefreshLabel();
    }

    void OnDisable()
    {
        if (Utilities.IsValid(manager)) manager._UnregisterLanguageSwitch(this);
        _registered = false;
    }

    public override void Interact()
    {
        _EnsureManager();
        if (!Utilities.IsValid(manager)) return;

        _Register();

        if (cycleLanguage) manager._CycleLanguage();
        else manager._SetLanguage(targetLanguage);

        // Manager 가 등록된 모든 전환 버튼을 갱신하지만,
        // 등록에 실패한 경우에도 자기 라벨만은 확실히 맞춥니다.
        _RefreshLabel();
    }

    /// <summary>ExhibitManager 가 언어를 바꿀 때 호출합니다. (모든 전환 버튼의 라벨 동기화)</summary>
    public void _OnLanguageChanged()
    {
        _RefreshLabel();
    }

    private void _Register()
    {
        if (_registered) return;
        if (!Utilities.IsValid(manager)) return;

        manager._RegisterLanguageSwitch(this);
        _registered = true;
    }

    /// <summary>
    /// ExhibitManager 를 찾아 연결합니다. Hierarchy Root 안 → 이름 순.
    /// 이름 검색은 Scene 을 가릴 수 없습니다 — 자세한 이유는
    /// <c>ExhibitInteractable._EnsureManager()</c> 의 주석을 보세요.
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
                manager = local;
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
                    manager = foundManager;
                    return;
                }
            }
        }

        if (!_warnedNoManager)
        {
            _warnedNoManager = true;
            Debug.LogWarning("[ExhibitLanguageSwitch] ExhibitManager 를 찾지 못했습니다: " + gameObject.name +
                             " (Tools > Exhibit Descriptor > Setup > All Exhibits In Scene 을 실행하면 " +
                             "같은 Scene 의 Manager 가 manager 필드에 연결됩니다)");
        }
    }

    private void _RefreshLabel()
    {
        if (!Utilities.IsValid(label)) return;
        if (!Utilities.IsValid(manager)) return;

        int index = manager._GetLanguageIndex();

        if (index == 1) label.text = "EN";
        else if (index == 2) label.text = "日本語";
        else label.text = "한국어";
    }
}
