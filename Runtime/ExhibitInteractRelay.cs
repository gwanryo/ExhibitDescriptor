using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Interact 판정 전용 릴레이.
///
/// VRChat 의 Interact 는 **Collider 와 UdonBehaviour 가 같은 GameObject** 에 있을 때가
/// 가장 안전하게 동작합니다. (자식 Collider 는 SDK 버전에 따라 인식되지 않을 수 있습니다.)
///
/// 그래서 구조를 이렇게 나눕니다.
///   Exhibit_A            ← ExhibitInteractable (데이터 보유)
///   └─ InteractionArea   ← BoxCollider + ExhibitInteractRelay (클릭 판정만 담당)
///
/// 이렇게 하면
///   - 작품 데이터는 Root 에 모여 관리가 쉽고
///   - Interact 판정 영역은 Mesh 와 완전히 분리되며 (요구사항 12)
///   - 한 작품에 여러 개의 Interact 영역(정면/측면 등)을 둘 수도 있습니다.
///
/// Collider 를 작품 Root 에 직접 붙였다면 이 컴포넌트는 필요 없습니다.
/// (ExhibitInteractable 자체도 Interact() 를 구현하고 있습니다.)
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ExhibitInteractRelay : UdonSharpBehaviour
{
    [Header("References")]
    [Tooltip("이 영역을 클릭했을 때 Overlay 를 토글할 작품. 보통 부모의 ExhibitInteractable 입니다.")]
    [SerializeField] private ExhibitInteractable target;

    public override void Interact()
    {
        if (!Utilities.IsValid(target)) return;
        target._ToggleOverlay();
    }

    /// <summary>
    /// ExhibitInteractable 이 현재 언어의 Interact 문구를 밀어 넣습니다.
    ///
    /// Interact 거리(proximity)는 여기서 다루지 않습니다. UdonBehaviour 쪽 필드라
    /// UdonSharp 코드에서 접근할 수 없고, Editor Tool 이 이미 구워 넣습니다.
    /// </summary>
    public void _SetInteractText(string text)
    {
        if (text == null || text.Length == 0) return;
        InteractionText = text;
    }
}
