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
