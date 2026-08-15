using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Overlay 안의 Close / ScrollUp / ScrollDown 버튼.
///
/// uGUI Button + EventSystem 대신 VRChat 기본 Interact 를 사용합니다.
///  - Desktop, PC VR 모두에서 추가 설정 없이 확실하게 동작합니다.
///  - Canvas 의 Graphic Raycaster / EventSystem 설정 실수로 인한 문제가 없습니다.
///
/// 필수 조건
///  - 이 컴포넌트가 붙은 GameObject 에 Collider(BoxCollider) 가 있어야 합니다.
///  - 해당 GameObject 의 Layer 는 Default 여야 합니다. (UI Layer 는 Interact 가 잡지 못할 수 있음)
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ExhibitOverlayButton : UdonSharpBehaviour
{
    [Header("References")]
    [Tooltip("이 버튼이 조작할 Overlay. 같은 Prefab 안에서 연결됩니다.")]
    [SerializeField] private ExhibitOverlay overlay;

    [Header("Action")]
    [SerializeField] private ExhibitButtonAction action = ExhibitButtonAction.Close;

    [Header("Interact Text (KR / EN / JP)")]
    [SerializeField] private string interactionTextKR = "닫기";
    [SerializeField] private string interactionTextEN = "Close";
    [SerializeField] private string interactionTextJP = "閉じる";

    // Editor 도구가 SerializedObject 로만 읽는 값이라 C# 코드에서는 참조되지 않습니다. (CS0414 억제)
#pragma warning disable 0414
    [Tooltip("버튼의 Interact 가능 거리(m). Overlay 를 보는 거리에서 누를 수 있어야 합니다.\n" +
             "Editor 도구(Setup)가 이 값을 UdonBehaviour 에 구워 넣습니다. 런타임 변경은 불가합니다.")]
    [SerializeField] private float interactionProximity = 3f;
#pragma warning restore 0414

    private bool _languageApplied;

    void Start()
    {
        // Overlay 가 먼저 _ApplyLanguage() 를 호출했다면 덮어쓰지 않습니다.
        // (SetActive(true) 직후 _Open() 이 Start() 보다 먼저 실행되기 때문)
        if (!_languageApplied) _ApplyLanguage(0);
    }

    public override void Interact()
    {
        if (!Utilities.IsValid(overlay)) return;

        // 애니메이션이 끝나 완전히 닫힌 상태에서는 무시합니다.
        if (!overlay.IsOpen) return;

        if (action == ExhibitButtonAction.Close) overlay._Close();
        else if (action == ExhibitButtonAction.ScrollUp) overlay._ScrollUp();
        else if (action == ExhibitButtonAction.ScrollDown) overlay._ScrollDown();
    }

    /// <summary>Overlay 가 언어 전환 시 호출합니다.</summary>
    public void _ApplyLanguage(int languageIndex)
    {
        _languageApplied = true;

        string text;
        if (languageIndex == 1) text = interactionTextEN;
        else if (languageIndex == 2) text = interactionTextJP;
        else text = interactionTextKR;

        if (text == null || text.Length == 0) text = interactionTextKR;
        if (text == null) text = "";

        InteractionText = text;
    }
}
