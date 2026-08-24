using TMPro;
using UnityEngine;

/// <summary>
/// Editor Tool 전용 설정 컴포넌트. <see cref="ExhibitManager"/> 와 같은 GameObject 에 붙습니다.
///
/// <b>UdonSharpBehaviour 가 아니라 평범한 MonoBehaviour 인 이유:</b> UdonSharpBehaviour 의 직렬화
/// 필드 타입은 Udon 타입 화이트리스트(NodeRegistry)를 탑니다. <c>TMP_FontAsset</c> / <c>LayerMask</c>
/// 는 거기 없어서, 필드로 두는 순간 프로젝트 전체 U# 컴파일이
/// <c>TypeResolverException: Type referenced by 'TMProTMP_FontAsset' could not be resolved</c> 로
/// 막힙니다. <c>[HideInInspector]</c> 나 <c>private</c> 로 바꿔도 힙에는 그대로 올라갑니다.
///
/// 두 값 모두 런타임에는 쓰이지 않습니다. Setup 이 읽어 TMP 텍스트와 Manager 의 int 에 구워 넣고,
/// 실행 중에는 그 구워진 값만 쓰입니다.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Exhibit Descriptor/Exhibit Descriptor Settings")]
public class ExhibitDescriptorSettings : MonoBehaviour
{
    [Header("Overlay Font")]
    // 폰트를 동봉하지 않고 슬롯만 두는 이유: CJK 폰트는 재배포 조건이 제각각이라, 패키지가 품으면
    // 이 패키지를 쓰는 월드까지 그 라이선스를 지게 됩니다. 비워 두면 TMP 기본값(글리프 없음)이라
    // 본문이 통째로 □ 로 보이고, Validate 가 그 상태를 경고합니다.
    [Tooltip("Overlay 와 언어 전환 버튼의 TMP 텍스트에 적용할 폰트입니다. " +
             "한글/일본어를 표시하려면 CJK 글리프를 포함한 TMP Font Asset 을 지정하세요. " +
             "Tools > Exhibit Descriptor > Exhibit Descriptor 창에서 지정하면 그 자리에서 반영되고, " +
             "여기서 직접 지정했다면 Setup > All Exhibits In Scene 을 한 번 실행하세요.")]
    public TMP_FontAsset overlayFont;

    [Header("Info Icon")]
    [Tooltip("아이콘과 Panel 이 '벽' 으로 취급할 Layer 입니다. 이 Layer 의 콜라이더를 재서 " +
             "아이콘/Panel 이 벽에 잠기지 않을 깊이를 정합니다. " +
             "비워 두면 Default + Environment 를 씁니다. (Setup 이 ExhibitManager 에 int 로 구워 넣습니다)")]
    public LayerMask iconProbeLayers = new LayerMask();
}
