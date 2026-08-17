using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 작품 옆에 뜨는 ⓘ 아이콘. World Space Canvas + BoxCollider 와 같은 GameObject 에 붙습니다.
///
/// 이 컴포넌트는 <b>판정과 표시만</b> 담당하는 얇은 릴레이입니다. 위치/회전/페이드는
/// <see cref="ExhibitInteractable"/> 이 계산해서 밀어 넣습니다. (Update() 없음)
///
/// 아이콘이 꺼져 있는 동안에는 이 GameObject 가 <c>SetActive(false)</c> 이므로
/// Collider 도 함께 죽습니다. 즉 Interact 대상이 <b>물리적으로 존재하지 않습니다</b> -
/// "툴팁이 안 뜬다" 가 아니라 뜰 수가 없습니다. 감상 중 화면 중앙이 깨끗한 이유입니다.
///
/// Interact 는 Collider 와 UdonBehaviour 가 같은 GameObject 일 때가 가장 안전하므로
/// (<see cref="ExhibitInteractRelay"/> 와 같은 이유) 아이콘에 직접 붙입니다.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ExhibitInfoIcon : UdonSharpBehaviour
{
    [Header("References")]
    [Tooltip("이 아이콘이 설명을 열어 줄 작품. 보통 부모의 ExhibitInteractable 입니다. (Setup 이 자동 연결)")]
    [SerializeField] private ExhibitInteractable target;

    [Tooltip("페이드에 사용할 CanvasGroup. 보통 이 아이콘 자신에게 붙습니다. (Setup 이 자동 연결)")]
    [SerializeField] private CanvasGroup canvasGroup;

    public override void Interact()
    {
        if (!Utilities.IsValid(target)) return;
        target._OnIconInteract();
    }

    /// <summary>ExhibitInteractable 이 페이드 진행률을 밀어 넣습니다. (0 = 투명, 1 = 불투명)</summary>
    public void _SetAlpha(float alpha)
    {
        if (!Utilities.IsValid(canvasGroup)) return;
        canvasGroup.alpha = alpha;
    }

    /// <summary>
    /// 현재 언어의 Interact 문구를 반영합니다.
    ///
    /// 비활성 오브젝트에는 이벤트가 전달되지 않으므로, 아이콘이 켜지는 순간
    /// <see cref="ExhibitInteractable"/> 이 다시 한 번 호출해 줍니다.
    /// (에디터 Setup 이 UdonBehaviour 에 구워 둔 값이 그 사이의 기본값입니다)
    /// </summary>
    public void _SetInteractText(string text)
    {
        if (text == null || text.Length == 0) return;
        InteractionText = text;
    }
}
