// ExhibitEnums.cs
// UdonSharpBehaviour 가 아닌 순수 enum 정의 파일입니다.
// UdonSharp 는 사용자 정의 enum 을 지원하므로 Runtime 폴더에 그대로 두면 됩니다.

/// <summary>
/// 시스템이 지원하는 언어. 정수값이 곧 배열/분기 인덱스로 사용되므로 순서를 바꾸지 마세요.
/// 언어를 추가하려면 맨 뒤에 추가하고, 각 스크립트의 _Pick() 계열 함수를 확장하면 됩니다.
/// </summary>
public enum ExhibitLanguage
{
    KR = 0,
    EN = 1,
    JP = 2,
}

/// <summary>
/// Overlay 내부 버튼이 수행할 동작.
/// </summary>
public enum ExhibitButtonAction
{
    Close = 0,
    ScrollUp = 1,
    ScrollDown = 2,
}

/// <summary>
/// ⓘ 아이콘이 작품의 어느 쪽에 붙을지. 방향은 전부 <b>관람자 기준</b>입니다.
///
/// <c>Default = 0</c> 인 이유: 이미 만들어 둔 Exhibit 에 이 필드가 새로 추가돼도
/// Unity 는 없는 필드를 0 으로 채우므로 자동으로 "Manager 기본값을 따름" 이 됩니다.
/// </summary>
public enum ExhibitIconPlacement
{
    Default = 0,   // ExhibitManager 의 defaultIconPlacement 를 따름
    Right = 1,     // 관람자 기준 오른쪽
    Left = 2,      // 관람자 기준 왼쪽
    Above = 3,     // 작품 위 (월드 +Y)
    Below = 4,     // 작품 아래 (월드 -Y)
}
