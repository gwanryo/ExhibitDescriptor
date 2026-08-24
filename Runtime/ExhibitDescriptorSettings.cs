using TMPro;
using UnityEngine;

/// <summary>
/// Editor Tool 전용 설정 컴포넌트. <see cref="ExhibitManager"/> 와 같은 GameObject 에 붙습니다.
///
/// <b>왜 UdonSharpBehaviour 가 아니라 평범한 MonoBehaviour 인가 (방안 A 채택 이유)</b>
///
/// 이 슬롯은 원래 <c>ExhibitManager</c> 에 <c>public TMP_FontAsset overlayFont;</c> 로 있었는데,
/// 그러면 U# 컴파일 자체가 실패합니다.
///   VRC.Udon.UAssembly.Assembler.TypeResolverException:
///   Type referenced by 'TMProTMP_FontAsset' could not be resolved.
/// UdonSharpBehaviour 의 직렬화 필드는 전부 Udon 힙에 <c>%TMProTMP_FontAsset</c> 으로 선언되고,
/// 그 선언은 Udon 타입 화이트리스트(NodeRegistry)를 타고 해석됩니다. TMPro 계열 중
/// <c>TextMeshProUGUI</c> / <c>TMP_Text</c> 등은 화이트리스트에 있지만 <b>폰트 애셋 계열
/// (<c>TMP_FontAsset</c>, <c>TMP_SpriteAsset</c>, <c>UnityEngine.Font</c>)은 통째로 빠져 있습니다.</b>
/// <c>[HideInInspector]</c> 나 <c>[SerializeField] private</c> 로 바꿔도 힙에는 그대로 올라가므로
/// 똑같이 실패하고, <c>[System.NonSerialized]</c> 로 막으면 값이 저장되지 않아 기능이 죽습니다.
///
/// 그래서 이 필드만 Udon 과 무관한 MonoBehaviour 로 분리했습니다. 화이트리스트 제약이 없고
/// 인스펙터의 오브젝트 슬롯도 그대로 유지됩니다. (대안이던 GUID 문자열 보관 방식은
/// 인스펙터 슬롯을 살리려면 PropertyDrawer 를 따로 써야 해서 택하지 않았습니다)
///
/// 애초에 이 값은 <b>런타임에 쓰이지 않습니다.</b> Editor 의 Setup 이 읽어 Overlay·언어 전환 버튼의
/// TMP 텍스트에 폰트를 구워 넣고, 실행 중에는 그 구워진 값만 쓰입니다. Editor 전용 데이터를
/// UdonSharpBehaviour 에 올린 것이 원래 설계 실수였습니다.
/// (VRChat 월드 빌드는 Udon 이 아닌 MonoBehaviour 를 씬에서 제거하므로 월드에는 올라가지 않습니다)
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Exhibit Descriptor/Exhibit Descriptor Settings")]
public class ExhibitDescriptorSettings : MonoBehaviour
{
    [Header("Overlay Font")]
    // 폰트를 패키지에 동봉하지 않고 슬롯만 두는 이유
    //  - 한글/일본어를 그리려면 CJK 글리프를 담은 TMP Font Asset 이 필요한데, 폰트마다 재배포
    //    조건이 다릅니다. 패키지가 임의로 폰트를 품으면 이 패키지를 쓰는 월드까지 그 라이선스를
    //    따라가게 되므로, 폰트 선택은 프로젝트에 맡기고 여기서는 지정할 자리만 제공합니다.
    //  - 지정하지 않으면 TMP 기본값(LiberationSans SDF)이 쓰이는데 여기에는 한글/일본어 글리프가
    //    없어 '작품 설명' 이 통째로 □ 로 보입니다. Validate Scene 이 이 상태를 경고합니다.
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

    // LayerMask 도 overlayFont 와 같은 이유로 여기 있습니다 — UdonSharpBehaviour 의 직렬화 필드
    // 타입은 Udon 타입 화이트리스트(NodeRegistry)를 타므로, 검증되지 않은 타입을 올리면
    // 프로젝트 전체 U# 컴파일이 막힙니다. Manager 가 받는 값은 int 입니다.
}
