# Exhibit Descriptor for VRChat (UdonSharp)

작품을 클릭하면 **작품 옆에** 설명 Overlay 가 뜨는 전시 시스템입니다.
Local Only · 다중 Overlay 동시 표시 · KR/EN/JP 다국어 · 100개 이상 확장 대응.

---

## 설치

**VCC (권장)**

1. [저장소 추가 페이지](https://gwanryo.github.io/ExhibitDescriptor/) 에서 *VCC 에 저장소 추가* 를 누릅니다.
   (또는 VCC 의 `Settings > Packages > Add Repository` 에 `https://gwanryo.github.io/ExhibitDescriptor/index.json` 을 직접 입력)
2. World 프로젝트를 열고 `Manage Project` 에서 **Exhibit Descriptor** 를 `+` 로 추가합니다.

VRChat Worlds SDK 3.10.4 이상이 함께 필요하며, VCC 가 자동으로 잡아 줍니다.

**수동 설치**

[Releases](https://github.com/gwanryo/ExhibitDescriptor/releases) 에서 zip 을 받아
프로젝트의 `Packages/com.gwanryo.exhibit-descriptor/` 에 풀어 넣습니다.

---

## 0. 포함된 파일

```
Packages/com.gwanryo.exhibit-descriptor/
├─ package.json
├─ Runtime/
│  ├─ ExhibitEnums.cs              // ExhibitLanguage, ExhibitButtonAction
│  ├─ ExhibitManager.cs            // Scene 당 1개. 언어 상태 + 단일 Update 틱
│  ├─ ExhibitInteractable.cs       // 작품 1개. 데이터 보유 + Overlay 토글
│  ├─ ExhibitInteractRelay.cs      // Collider 와 같은 GameObject 에서 Interact 판정
│  ├─ ExhibitOverlay.cs            // 작품별 Overlay. Fade+Scale, 스크롤
│  ├─ ExhibitOverlayButton.cs      // Close / ScrollUp / ScrollDown 버튼
│  ├─ ExhibitLanguageSwitch.cs     // 월드용 언어 전환 버튼 (선택)
│  └─ ExhibitDescriptor.asmdef     // 런타임 어셈블리 정의
├─ Editor/
│  ├─ ExhibitDescriptorTools.cs         // 자동 생성 / 자동 연결 / 검증 도구
│  ├─ ExhibitDescriptorBatchTools.cs    // Mesh 일괄 변환 / 저장 시 자동 Setup
│  └─ ExhibitDescriptor.Editor.asmdef   // 에디터 어셈블리 정의
├─ QUICKSTART.md                     // 처음 쓰는 사람용 1장 요약
└─ README.md
```

> **처음이라면 `QUICKSTART.md` 부터 읽으세요.** 이 문서는 레퍼런스입니다.

> 요구사항의 "6. ExhibitManager.cs 전체 코드", "7. ExhibitInteractable.cs 전체 코드",
> "8. Overlay Controller 전체 코드" 는 위 파일들이 **완성된 전체 코드**입니다.
> 잘라낸 예시가 아니라 그대로 컴파일되는 상태입니다.

---

# PART A. 따라 하기 (13단계)

## 1단계. 프로젝트 사전 준비

1. **Unity 2022.3.22f1** (VRChat 권장 버전) 프로젝트를 준비합니다.
2. **VRChat Creator Companion (VCC)** 에서 World 프로젝트를 만들고 다음을 추가합니다.
   - `VRChat SDK - Worlds`
   - `UdonSharp`
   - `ClientSim` (테스트용, 권장)
3. `TextMeshPro` 는 SDK 의존성으로 이미 들어 있습니다. 없다면
   `Window > TextMeshPro > Import TMP Essential Resources` 를 1회 실행합니다.
4. 이 폴더(`Assets/ExhibitDescriptor`)를 프로젝트의 `Assets` 아래에 복사합니다.
5. Unity 가 컴파일을 끝내면 상단 메뉴에 **`Tools > Exhibit Descriptor`** 가 나타납니다.

### CJK 폰트 준비 (필수)

한국어/영어/일본어를 모두 표시하려면 **CJK 글리프를 포함한 TMP Font Asset** 이 필요합니다.

1. `Noto Sans CJK KR` 또는 `Source Han Sans` (OFL 라이선스) `.otf/.ttf` 를 `Assets/Fonts` 에 넣습니다.
2. `Window > TextMeshPro > Font Asset Creator` 실행
   - Source Font File: 위 폰트
   - Sampling Point Size: `Auto Sizing`
   - Padding: `5`
   - Packing Method: `Fast` (최종본은 `Optimum`)
   - Atlas Resolution: `4096 x 4096`
   - Character Set: `Characters from File` 또는 `Unicode Range (Hex)`
     - 권장 Unicode Range:
       `20-7E,A1-FF,3000-303F,3040-309F,30A0-30FF,4E00-9FFF,AC00-D7A3,FF00-FFEF`
     - (ASCII + 일본어 かな + CJK 통합한자 + 한글 완성형 + 전각기호)
   - Render Mode: `SDFAA`
3. **Generate Font Atlas → Save as** `NotoSansCJK SDF`
4. 아틀라스가 너무 커지면 **Atlas Population Mode = Dynamic** 으로 두고
   자주 쓰는 글자만 Static 으로 굽는 방식도 가능합니다. (PC 전용이므로 Dynamic 도 무방)
5. 생성한 Font Asset 을 이후 모든 TMP 텍스트의 `Font Asset` 에 지정합니다.

> **주의:** 기본 `LiberationSans SDF` 에는 한글/일본어 글리프가 없어 □ 로 표시됩니다.

---

## 2단계. Hierarchy 생성

메뉴에서 실행:

```
Tools > Exhibit Descriptor > Create Exhibition Root
```

생성 결과:

```
ExhibitionRoot
└─ ExhibitManager      (UdonBehaviour: ExhibitManager)
```

이어서:

```
Tools > Exhibit Descriptor > Create Exhibit (Template)
```

생성 결과 (권장 Hierarchy):

```
ExhibitionRoot
├─ ExhibitManager
│
└─ Exhibit_New                      ← ExhibitInteractable (작품 데이터 보유)
   ├─ Artwork                       ← 실제 작품 Mesh (Collider 없음. 템플릿은 투명 Placeholder)
   ├─ InteractionArea               ← BoxCollider + ExhibitInteractRelay (Interact 판정)
   ├─ OverlayAnchor                 ← Overlay 표시 위치/방향 (빈 Transform)
   └─ Overlay                       ← Canvas(World Space) + CanvasGroup + ExhibitOverlay
      └─ Panel                      ← 배경 Image, Scale 애니메이션 대상
         ├─ TitleText               ← TextMeshProUGUI
         ├─ SubtitleText            ← TextMeshProUGUI (선택)
         ├─ DescriptionScrollView
         │  └─ Viewport             ← RectMask2D
         │     └─ Content           ← VerticalLayoutGroup + ContentSizeFitter
         │        └─ DescriptionText← TextMeshProUGUI
         ├─ ScrollUpButton          ← Image + BoxCollider + ExhibitOverlayButton
         ├─ ScrollDownButton        ← Image + BoxCollider + ExhibitOverlayButton
         └─ CloseButton             ← Image + BoxCollider + ExhibitOverlayButton
```

원안 대비 개선점 3가지:

- **`Panel` 을 한 겹 추가**했습니다. Canvas 자체는 `localScale = 0.001` 이라
  Scale 애니메이션 대상으로 쓰면 계산이 지저분해집니다. Panel(scale=1)을 흔드는 편이 안전합니다.
- **데이터는 작품 Root, Interact 판정은 `InteractionArea`** 로 나눴습니다.
  VRChat 의 Interact 는 **Collider 와 UdonBehaviour 가 같은 GameObject** 일 때가 가장 안전합니다.
  그래서 `InteractionArea` 에 `BoxCollider` + `ExhibitInteractRelay` 를 함께 두고,
  릴레이가 작품 Root 의 `ExhibitInteractable._ToggleOverlay()` 를 호출합니다.
  → 작품 데이터는 Root 한 곳에 모이고, 클릭 판정은 Mesh 와 완전히 분리됩니다 (요구사항 12).
- 한 작품에 **Interact 영역을 여러 개** 둘 수도 있습니다 (정면/측면 등).
  `InteractionArea` 를 복제하기만 하면 되고, Setup 도구가 전부 수집해 연결합니다.

> Collider 를 작품 Root 에 직접 붙이는 방식도 그대로 동작합니다.
> `ExhibitInteractable` 자체가 `Interact()` 를 구현하고 있어 릴레이 없이도 됩니다.

---

## 3단계. UI 생성 (자동 생성된 것을 다듬기)

템플릿이 이미 만들어 주지만, 수동으로 만들거나 디자인을 바꿀 때 기준값입니다.

| 오브젝트 | 설정 |
|---|---|
| `Overlay` (Canvas) | Render Mode = **World Space**, Rect = `600 x 440`, Scale = `0.001` → 실제 0.6m x 0.44m |
| `Overlay` (CanvasGroup) | Alpha = 0 (런타임에 제어), Interactable/BlocksRaycasts = 자동 제어 |
| `Panel` | Anchors 전체 Stretch, Image color `#0A0A0F` alpha `0.86` |
| `TitleText` | Anchor Top-Stretch, Pivot(0.5,1), Size `(-56, 56)`, Pos `(0,-20)`, FontSize 40, Bold |
| `SubtitleText` | Anchor Top-Stretch, Pivot(0.5,1), Size `(-56, 34)`, Pos `(0,-80)`, FontSize 24 |
| `DescriptionScrollView` | Anchor 전체 Stretch, Left/Right `28`, Top `122`, Bottom `96` |
| `Viewport` | 전체 Stretch + **RectMask2D** |
| `Content` | Anchor Top-Stretch, Pivot(0.5,1), `VerticalLayoutGroup` + `ContentSizeFitter(Vertical=Preferred)` |
| `DescriptionText` | FontSize 26, Word Wrapping ON, Auto Size OFF |
| `ScrollUpButton` | Anchor Bottom-Left, Pos `(28,24)`, Size `64x60` |
| `ScrollDownButton` | Anchor Bottom-Left, Pos `(100,24)`, Size `64x60` |
| `CloseButton` | Anchor Bottom-Right, Pos `(-28,24)`, Size `140x60` |

**세 버튼 공통 필수 조건**

- `BoxCollider` 필수 (Size = `가로, 세로, 10`)
- **Layer = `Default`** ← UI Layer 에 두면 Udon Interact 가 잡지 못할 수 있습니다.
- `Image.raycastTarget` 은 꺼도 됩니다 (uGUI EventSystem 을 쓰지 않으므로).

> **왜 uGUI Button 이 아니라 Interact 버튼인가?**
> uGUI `Button` 은 EventSystem + GraphicRaycaster + Canvas Collider 조합이 정확해야 하고
> 설정 실수가 잦습니다. 반면 Udon `Interact()` 는 VRChat 기본 조작(Desktop 클릭 / VR 트리거)과
> 완전히 동일하게 동작하고, 요구사항 22("VRChat 기본 Interact 표시 사용")와도 일치합니다.
> uGUI Button 을 쓰고 싶다면 PART B 의 13번 항목 하단을 참고하세요.

---

## 4단계. Component 설정

### 필요한 Component 전체 목록

| 위치 | Component | 비고 |
|---|---|---|
| ExhibitManager | `ExhibitManager` (U#) | Scene 당 1개 |
| Exhibit Root | `ExhibitInteractable` (U#) | 작품 데이터 보유 |
| InteractionArea | `BoxCollider` + `ExhibitInteractRelay` (U#) | `isTrigger = true`, Layer = Default |
| Overlay | `Canvas` / `CanvasGroup` / `ExhibitOverlay` (U#) | World Space |
| Panel | `Image` | Scale 애니메이션 대상 |
| Title/Subtitle/Description | `TextMeshProUGUI` | CJK Font Asset 지정 |
| Viewport | `RectMask2D` | |
| Content | `VerticalLayoutGroup`, `ContentSizeFitter` | Vertical = Preferred Size |
| 각 Button | `Image`, `BoxCollider`, `ExhibitOverlayButton` (U#) | Layer = Default |
| (선택) 언어 전환 오브젝트 | `Collider`, `ExhibitLanguageSwitch` (U#) | |

### Inspector 기본값

**ExhibitManager**

| 필드 | 기본값 |
|---|---|
| Default Language | `KR` |
| Default Interaction Text KR / EN / JP | `설명` / `Description` / `説明` |
| Default Proximity | `2` |
| Debug Log | `false` |

**ExhibitInteractable**

| 필드 | 기본값 / 설명 |
|---|---|
| Manager | 자동 연결 (비우면 런타임에 이름으로 탐색) |
| Overlay | 자기 Prefab 안의 Overlay |
| Overlay Anchor | `OverlayAnchor` |
| Manager Object Name | `ExhibitManager` |
| Interact Relays | 자식 `InteractionArea` 들 (자동 수집) |
| Title KR / EN / JP | 작품 제목 |
| Subtitle KR / EN / JP | 작가·연도 등 (선택) |
| Description KR / EN / JP | 본문 |
| Extra Labels/Values KR·EN·JP | 배열, 같은 순서로 채움 (선택) |
| Show Title / Description | `true` |
| Show Subtitle / Extra Info | `false` |
| Interaction Text KR/EN/JP | 비움 → Manager 기본값(`설명`) 사용 |
| **Interaction Proximity** | **`2`** (m) |
| Snap To Anchor On Open | `true` |

**ExhibitOverlay**

| 필드 | 기본값 |
|---|---|
| Canvas Group | Overlay 자신 |
| Scale Root | `Panel` |
| Title / Subtitle / Description Text | 각 TMP |
| Scroll Viewport / Scroll Content | `Viewport` / `Content` |
| Buttons | 자식 버튼 3개 (자동 수집) |
| Open Duration | `0.18` |
| Close Duration | `0.12` |
| Start Scale Multiplier | `0.92` |
| Deactivate When Closed | `true` |
| Scroll Step | `120` |
| Scroll Smooth Speed | `14` |

**ExhibitOverlayButton**

| 필드 | Close | ScrollUp | ScrollDown |
|---|---|---|---|
| Action | `Close` | `ScrollUp` | `ScrollDown` |
| Interaction Text KR/EN/JP | `닫기`/`Close`/`閉じる` | `위로`/`Up`/`上へ` | `아래로`/`Down`/`下へ` |
| Interaction Proximity | `3` | `3` | `3` |

---

## 5단계. UdonSharp Script 작성

이미 `Runtime/` 폴더에 전부 들어 있습니다. 새로 작성할 것은 없습니다.

컴파일 확인 포인트:

- 모든 클래스에 `[UdonBehaviourSyncMode(BehaviourSyncMode.None)]` 이 붙어 있는지
- Console 에 UdonSharp 컴파일 에러가 없는지 (`U# Compile` 로그가 성공으로 끝나는지)

---

## 6단계. Inspector 연결

수동으로 하나씩 끌어다 놓을 필요 없습니다.

```
Exhibit_New 선택 → Tools > Exhibit Descriptor > Setup Selected Exhibits
```

이 명령이 하는 일:

1. `manager` / `overlay` / `overlayAnchor` 자동 연결
2. Overlay 의 `canvasGroup` / `scaleRoot` / 3개 TMP / `viewport` / `content` / `buttons[]` 자동 연결
3. 각 버튼의 `overlay` 참조 자동 연결
4. **UdonBehaviour 의 `Interact Text` / `Proximity` 값을 실제로 구워 넣기**
5. 버튼에 Collider 가 없으면 자동 추가하고 Layer 를 Default 로 교정

마지막으로 데이터만 채웁니다.

- `Title KR/EN/JP`
- `Description KR/EN/JP`
- (선택) `Subtitle`, `Extra Info`

---

## 7단계. Prefab 생성

1. Hierarchy 의 `Exhibit_New` 이름을 `Exhibit_Base` 로 변경합니다.
2. `Overlay` 오브젝트가 **비활성(체크 해제)** 상태인지 확인합니다.
   (템플릿 생성기가 자동으로 꺼 둡니다. 켜진 채 저장하면 월드 입장 시 전부 떠 있습니다.)
3. `Assets/ExhibitDescriptor/Prefabs/` 폴더를 만들고 `Exhibit_Base` 를 드래그해 Prefab 으로 저장합니다.
4. Prefab 안에서 닫히는 참조(overlay, anchor, buttons)는 그대로 유지됩니다.
   **`manager` 만 Scene 참조**라 Prefab Asset 에는 저장되지 않지만,
   - Scene 에 배치된 인스턴스에서는 Setup 도구가 자동으로 채워 주고,
   - 비어 있어도 런타임에 1회 탐색해 복구합니다. 탐색은 2단계입니다.
     1) 자기 Hierarchy Root 아래 (Transform Root 는 항상 같은 Scene → Additive 안전)
     2) `GameObject.Find("ExhibitManager")` (빠른 경로)

> **Additive Scene 주의**
> Udon 은 `UnityEngine.SceneManagement.Scene` 타입을 전혀 노출하지 않습니다.
> `GameObject.scene` 도, `Scene.GetRootGameObjects()` 도 화이트리스트에 없어
> UdonSharp 로 컴파일할 수 없습니다. (Worlds SDK 3.10.4 의 Udon extern 목록 기준)
> 그래서 위 2단계는 찾은 오브젝트가 **같은 Scene 인지 런타임에서 확인할 수 없습니다.**
> Additive 로 여러 Scene 을 띄우고 각 Scene 에 동명의 Manager 를 두는 구성이라면,
> 반드시 `Tools > Exhibit Descriptor > Setup All Exhibits In Scene` 을 실행하세요.
> **Editor 도구는 같은 Scene 의 Manager 만 연결**하므로 런타임 탐색이 아예 실행되지 않습니다.
> (Manager 를 못 찾아도 Overlay 는 애니메이션만 생략하고 열림/닫힘은 정상 동작합니다.)

---

## 8단계. 작품 하나 테스트 (ClientSim)

1. `VRChat SDK > Utilities > ClientSim` 활성화 확인
2. Scene 에 `VRCWorld` Prefab(Scene Descriptor + SpawnPoint)이 있는지 확인
3. Play 버튼
4. 작품 앞으로 이동 → 화면 중앙에 **`설명`** 툴팁 확인 → 좌클릭
5. 체크리스트
   - [ ] Overlay 가 작품 옆(`OverlayAnchor` 위치)에서 Fade+Scale 로 나타남
   - [ ] 플레이어가 움직여도 Overlay 가 회전하지 않음 (고정 방향)
   - [ ] 같은 작품을 다시 클릭 → 역방향 애니메이션으로 닫힘
   - [ ] `✕` 버튼 클릭 → 닫힘
   - [ ] 긴 설명에서 `▲` `▼` 로 스크롤
   - [ ] Hierarchy 에서 작품 Root 를 비활성화 → Overlay 즉시 사라짐

---

## 9단계. 작품 여러 개 테스트

1. `Exhibit_Base` Prefab 을 Hierarchy 에 2~3개 더 드래그합니다.
2. 이름을 `Exhibit_A`, `Exhibit_B`, `Exhibit_C` 로 바꾸고 위치를 벌립니다.
3. 각각의 Title / Description 을 다르게 입력합니다.
4. 체크리스트
   - [ ] A, B, C 를 순서대로 클릭 → **3개 Overlay 가 동시에 열려 있음**
   - [ ] 각 Overlay 는 자기 작품 옆에 있음 (플레이어 주변이 아님)
   - [ ] B 를 다시 클릭 → B 만 닫히고 A, C 는 유지
   - [ ] 작품 하나당 Overlay 는 항상 1개

---

## 10단계. 다국어 테스트

1. 빈 오브젝트 `LanguageSwitch` 를 만들고 `BoxCollider` + `ExhibitLanguageSwitch` 를 붙입니다.
   (Layer = Default)
2. `Cycle Language = true` 로 둡니다.
3. Play → 버튼 Interact → `KR → EN → JP → KR` 순환
4. 체크리스트
   - [ ] **열려 있는 Overlay 의 텍스트가 즉시 바뀜**
   - [ ] 닫혀 있던 작품도 다음에 열면 바뀐 언어로 표시
   - [ ] 작품 조준 시 툴팁이 `설명 / Description / 説明` 로 바뀜
   - [ ] 번역이 비어 있는 항목은 KR 로 fallback

---

## 11단계. Desktop 테스트 (Build & Test)

1. `VRChat SDK > Show Control Panel > Builder`
2. `Build & Test` (Local Test)
3. 확인
   - [ ] 2m 밖에서는 툴팁이 안 뜨고, 2m 안에서 뜬다
   - [ ] 한글/일본어가 □ 없이 렌더링된다
   - [ ] Overlay 가 벽/작품에 파묻히지 않는다 (Anchor 를 살짝 앞으로 빼기)

---

## 12단계. VR 테스트

1. 같은 `Build & Test` 빌드를 VR 모드로 실행 (또는 `Number of Clients = 1` 로 VR 진입)
2. 확인
   - [ ] 손 레이저로 작품을 조준하면 Interact 표시가 뜬다
   - [ ] `▲ ▼ ✕` 버튼이 트리거로 눌린다 (버튼 크기 0.06m 이상 권장)
   - [ ] Overlay 애니메이션이 0.2초 내로 끝나 멀미를 유발하지 않는다
   - [ ] Overlay 가 눈높이(약 1.5m) 근처에 있다

버튼이 VR 에서 누르기 어렵다면 `Interaction Proximity` 를 `3 → 4` 로 올리거나
버튼 `BoxCollider` 의 Z 두께를 `10 → 30` 으로 키웁니다.

---

## 13단계. 100개 이상으로 확장할 때 최적화

| 항목 | 처리 |
|---|---|
| Update 비용 | Update 는 **ExhibitManager 1개뿐**이며, 애니메이션 중인 Overlay 가 0개면 즉시 return |
| Overlay 렌더링 | 닫히면 `SetActive(false)` → Canvas 리빌드/드로우콜 0 |
| Canvas 분리 | Overlay 마다 Canvas 가 독립이라 한 곳의 갱신이 다른 Overlay 를 리빌드시키지 않음 |
| Find 호출 | `manager` 를 Setup 도구로 미리 연결하면 런타임 `GameObject.Find` 가 **0회** |
| GetComponent | 런타임 중 호출 없음 (모두 Inspector 참조) |
| 폰트 | Font Asset 1개를 모든 작품이 공유 → 아틀라스 텍스처 1장 |
| 배치 | 전시실/층 단위로 부모 오브젝트를 나누고, 멀리 있는 구역은 통째로 비활성화 |
| Occlusion | Overlay 는 닫혀 있으면 비활성이라 Occlusion Culling 부담 없음 |

추가로 권장:

- 작품을 `ExhibitionRoot/Room_1F`, `Room_2F` 처럼 그룹화
- Static Batching 은 Artwork Mesh 에만 적용 (Canvas 는 제외)
- 완성 후 `ExhibitManager.Debug Log = false`

---

# PART B. 레퍼런스 (요구 항목 1~28)

## 1. 전체 Architecture

```
                    ┌──────────────────────────────┐
                    │        ExhibitManager        │  Scene 당 1개
                    │  - 언어 상태 (KR/EN/JP)      │
                    │  - 공통 기본값                │
                    │  - 단일 Update() 틱 루프      │
                    └───────┬──────────────┬───────┘
             _RegisterExhibit│              │_RegisterTick
                             │              │
     ┌───────────────────────▼──┐     ┌─────▼─────────────────────┐
     │   ExhibitInteractable    │     │      ExhibitOverlay       │
     │  - KR/EN/JP 데이터 보유   │────▶│  - Fade + Scale 상태머신   │
     │  - Interact() 토글        │     │  - 스크롤 위치 보간         │
     │  - Anchor 로 위치 스냅    │     │  - IsOpen (public 필드)    │
     └──────────────────────────┘     └─────▲─────────────────────┘
                                            │
                                   ┌────────┴──────────┐
                                   │ ExhibitOverlayButton │ × 3
                                   │  Close / Up / Down   │
                                   └──────────────────────┘
```

설계 원칙 4가지:

1. **데이터는 작품이, 정책은 Manager 가.**
   작품 텍스트는 전부 `ExhibitInteractable` 이 갖고, Manager 는 "지금 언어가 뭔지"만 압니다.
   덕분에 작품 추가 시 Manager 를 건드릴 일이 전혀 없습니다.

2. **Update 는 딱 한 곳.**
   Overlay 는 자기 Update 를 갖지 않고, 애니메이션이 필요한 순간에만
   `manager._RegisterTick(this)` 로 등록됩니다. 애니메이션이 끝나면 `_Tick()` 이 `false` 를
   반환하고 목록에서 빠집니다. 작품 100개 × 유휴 상태 = **연산 0**.

3. **자기 등록(self-registration).**
   작품은 `OnEnable` 에서 스스로 Manager 에 등록하고 `OnDisable` 에서 해제합니다.
   Manager 의 Inspector 에 100개를 끌어다 놓는 작업이 없습니다.

4. **비활성 오브젝트에는 메서드를 호출하지 않는다.**
   Udon 에서 비활성 UdonBehaviour 로의 이벤트 호출은 실행되지 않을 수 있습니다.
   그래서 상태 조회는 `public bool IsOpen` **필드 읽기**로 하고(항상 안전),
   상태 정리는 각 오브젝트가 자기 `OnDisable` 에서 직접 합니다.

5. **Manager 가 꺼지면 진행 중인 Overlay 를 확정한다.**
   Manager 가 비활성화되면 `Update()` 가 멈추는데, Overlay 에는 자체 `Update()` 가 없어
   열기/닫기/스크롤 도중이던 Overlay 가 중간 상태로 굳어 버립니다.
   그래서 `ExhibitManager.OnDisable()` 이 틱 목록에 남은 Overlay 를
   `_FinishTickImmediate()` 로 목표 상태에 즉시 스냅시키고 목록을 비웁니다.
   (Scene 언로드 중 콜백 순서를 고려해 **파괴되었거나 비활성인 대상은 건너뜁니다.**
   그런 Overlay 는 이미 자기 `OnDisable` 에서 상태를 정리했습니다.)
   Manager 가 다시 켜지면 Overlay 가 다음 조작에서 새로 등록되므로 애니메이션도 복귀합니다.

## 2. 추천 Unity Hierarchy

→ PART A 2단계 참조.

## 3. Prefab 구조

`Exhibit_Base.prefab` 하나만 있으면 됩니다.
Prefab 내부에서 모든 참조가 닫히므로, 복제해도 연결이 깨지지 않습니다.

| 참조 | Prefab 내부에서 해결? |
|---|---|
| `overlay`, `overlayAnchor` | ✅ |
| Overlay 의 TMP/Viewport/Content/buttons | ✅ |
| 버튼의 `overlay` | ✅ |
| `manager` | ❌ Scene 참조 → Setup 도구가 채우거나 런타임 Find 로 복구 |

## 4. 필요한 Component 목록

→ PART A 4단계 표 참조.

## 5. 각 Component 의 Inspector 설정값

→ PART A 4단계 표 참조.

## 6. ExhibitManager.cs

`Runtime/ExhibitManager.cs` — 전체 코드.
핵심: `_GetLanguageIndex()`, `_SetLanguage()`, `_CycleLanguage()`,
`_RegisterExhibit()/_UnregisterExhibit()`, `_RegisterTick()`, 단일 `Update()`.

## 7. ExhibitInteractable.cs

`Runtime/ExhibitInteractable.cs` — 전체 코드.
핵심: `Interact()` 토글, `_PushContent()` 로 현재 언어 문자열 생성,
`_ApplyInteractSettings()` 로 Interact 문구/거리 적용.

## 8. Overlay Controller

`Runtime/ExhibitOverlay.cs` — 전체 코드.
상태머신(`CLOSED / OPENING / OPEN / CLOSING`) + `_Tick()` 이 핵심입니다.
버튼은 `Runtime/ExhibitOverlayButton.cs`.

## 9. KR / EN / JP 다국어 데이터 구조

작품마다 아래 필드를 Inspector 에서 직접 입력합니다.

```
Title       : titleKR        titleEN        titleJP
Subtitle    : subtitleKR     subtitleEN     subtitleJP
Description : descriptionKR  descriptionEN  descriptionJP
Extra       : extraLabelsKR[] / extraValuesKR[]   (EN, JP 동일 구조)
```

- 언어 인덱스는 `0=KR, 1=EN, 2=JP` 로 고정입니다 (`ExhibitLanguage` enum).
- **번역이 비어 있으면 자동으로 KR → EN 순으로 fallback** 하므로,
  일단 KR 만 채워도 월드가 정상 동작합니다.
- Extra Info 는 Label/Value 를 같은 인덱스로 짝지어 `재료 : 캔버스에 유채` 형태로 출력됩니다.
  `Show Extra Info` 를 켜면 설명 본문 위에 붙습니다.
  fallback 은 **배열이 아니라 칸 단위**입니다. EN Label 만 채우고 EN Value 를 비워 두거나
  EN 배열이 더 짧아도 줄이 사라지지 않고, 비어 있는 칸만 KR → EN → JP 로 대체됩니다.

> **언어를 추가하려면?**
> `ExhibitEnums.cs` 의 `ExhibitLanguage` 에 `CN = 3` 을 추가하고,
> `_Pick()` / `_PickLocalized()` 의 분기와 `_SetLanguageIndex()` 의 상한(`> 2`)만 늘리면 됩니다.
> ScriptableObject 는 Udon 에서 못 쓰므로 이 방식이 가장 단순합니다.

## 10. 언어 전환 방법

**방법 A — 월드 버튼 (권장)**
`ExhibitLanguageSwitch` 를 붙인 오브젝트(Collider 필요)를 배치.
- `Cycle Language = true` → 버튼 1개로 KR→EN→JP 순환
- `Cycle Language = false` + `Target Language` 지정 → 언어별 버튼 3개

**방법 B — 다른 Udon 에서 호출**
```csharp
manager._SetLanguage(ExhibitLanguage.EN);
manager._SetLanguageIndex(1);
manager._CycleLanguage();
```
`SendCustomEvent` 로도 가능합니다: `_SetLanguageKR` / `_SetLanguageEN` / `_SetLanguageJP`.

**동작 흐름**
`_SetLanguageIndex()` → 등록된(=활성) 작품 전체에 `_OnLanguageChanged()` 호출 →
각 작품이 Interact 문구를 갱신하고, **열려 있는 Overlay 만** 텍스트를 다시 씁니다.
닫힌 Overlay 는 다음에 열릴 때 자연스럽게 새 언어로 채워지므로 낭비가 없습니다.

이어서 **등록된 모든 `ExhibitLanguageSwitch`** 에도 `_OnLanguageChanged()` 가 전달됩니다.
그래서 전환 버튼을 여러 개 배치해도 각 버튼의 현재 언어 라벨(`한국어 / EN / 日本語`)이
항상 같은 값을 가리킵니다. 버튼은 `OnEnable` 에서 스스로 등록하고 `OnDisable` 에서 해제하며,
꺼져 있는 동안 언어가 바뀌었다면 다시 켜질 때 라벨을 스스로 맞춥니다.

## 11. TextMeshProUGUI 연결 방법

1. `TitleText` / `SubtitleText` / `DescriptionText` 오브젝트에 `TextMeshProUGUI` 부착
2. 각 컴포넌트의 `Font Asset` 에 1단계에서 만든 **CJK Font Asset** 지정
3. `ExhibitOverlay` 의 `titleText` / `subtitleText` / `descriptionText` 슬롯에 연결
   → `Setup Selected Exhibits` 가 이름(`TitleText` 등)으로 자동 연결합니다.
4. 코드에서는 `text.text = "..."` 만 사용합니다. (Udon 에서 안전하게 노출된 API)
5. 빈 문자열이 들어오면 해당 텍스트 오브젝트를 자동으로 `SetActive(false)` 합니다.

> 여러 폰트를 섞고 싶다면 TMP 의 **Fallback Font Asset** 기능을 쓰세요.
> (Project Settings > TextMeshPro > Settings > Fallback Font Assets)

## 12. ScrollView 구성 방법

```
DescriptionScrollView        (RectTransform, 크기만 담당)
└─ Viewport                  RectMask2D          ← 잘라내기
   └─ Content                VerticalLayoutGroup ← 세로 정렬
                             ContentSizeFitter   ← Vertical: Preferred Size
      └─ DescriptionText     TextMeshProUGUI     ← Word Wrapping ON
```

- `Content` 의 Anchor 는 **Top-Stretch**, Pivot 은 **(0.5, 1)** 이어야 합니다.
  이 상태에서 `Content.anchoredPosition.y` 가 0이면 맨 위, 값이 커질수록 아래로 스크롤됩니다.
- **`ScrollRect` 컴포넌트는 일부러 사용하지 않습니다.**
  Udon 에서 `ScrollRect` API 사용은 버전에 따라 불안정하고,
  코드로 위치를 지정해도 `ScrollRect` 가 되돌리려 하면서 충돌합니다.
  대신 `RectTransform.anchoredPosition` 을 직접 제어합니다 — 100% 화이트리스트 API 이고
  버튼 스크롤 보간도 우리가 완전히 통제할 수 있습니다.
- 스크롤 가능한 최대치는 `Content.rect.height - Viewport.rect.height` 로 매번 계산하므로,
  설명 길이가 달라져도 자동으로 맞습니다. 내용이 짧으면 스크롤이 아예 잠깁니다.

## 13. Up / Down 버튼 구현 방법

- `ExhibitOverlayButton` 의 `Action` 을 `ScrollUp` / `ScrollDown` 으로 설정
- `Interact()` → `overlay._ScrollUp()` / `_ScrollDown()`
- `_SetTargetScroll()` 이 목표 위치를 `[0, maxScroll]` 로 클램프하고 Manager 에 틱을 요청
- `_Tick()` 이 `Lerp` 로 부드럽게 이동 → 도착하면 자동으로 틱 해제

VR 사용성 튜닝:

| 증상 | 조정 |
|---|---|
| 누르기 어렵다 | `Interaction Proximity` ↑ (3 → 4), 버튼 Collider Z 두께 ↑ |
| 한 번에 너무 많이 넘어간다 | `Scroll Step` ↓ (120 → 80) |
| 너무 느리다 | `Scroll Smooth Speed` ↑ (14 → 20) 또는 `0` 으로 두면 즉시 이동 |

**uGUI Button 을 쓰고 싶다면**
1. `Overlay`(Canvas) 에 `Graphic Raycaster` 추가
2. Canvas 크기와 같은 `BoxCollider` 를 Canvas 오브젝트에 추가
3. 버튼을 `UI > Button - TextMeshPro` 로 생성하고 `Image.raycastTarget = true`
4. `Button.OnClick` 에 Overlay 의 UdonBehaviour 를 넣고
   `SendCustomEvent` / 이벤트명 `_ScrollUp` (`_` 시작이라 네트워크로 전파되지 않음)
5. Scene 안의 `EventSystem` 은 **삭제**합니다 (VRChat 이 자체 EventSystem 을 제공)

## 14. Close 버튼 구현 방법

- `Action = Close` → `overlay._Close()`
- `_Close()` 는 상태를 `CLOSING` 으로 바꾸고 `CanvasGroup.interactable/blocksRaycasts` 를 끕니다.
- 애니메이션이 끝나면 `IsOpen = false`, `SetActive(false)`.
- 이미 닫힌 상태에서 눌러도 `if (!overlay.IsOpen) return;` 으로 무시되어 안전합니다.

## 15. 같은 작품을 다시 클릭했을 때 Toggle 처리

```csharp
public override void Interact()
{
    if (!Utilities.IsValid(overlay)) return;
    _EnsureManager();

    if (overlay.IsOpen) _CloseOverlay();
    else _OpenOverlay();
}
```

- `IsOpen` 은 Overlay 의 **public 필드**입니다. 메서드 호출이 아니라 필드 읽기라서
  Overlay 가 비활성이어도 안전하게 읽힙니다.
- 열리는 중(`OPENING`)에 다시 누르면 → `IsOpen == true` 이므로 닫힘으로 전환되고
  현재 진행률(`_t`)에서 역방향으로 재생됩니다. 튀지 않습니다.
- 닫히는 중(`CLOSING`)에 다시 누르면 → `_Close()` 진입 시점에 `IsOpen = false` 로 두므로
  다시 열림으로 전환되어 **현재 진행률에서 그대로 되돌아옵니다.**
- 즉 `_t` 하나로 양방향을 표현하므로, 아무리 빠르게 연타해도 스케일/알파가 튀지 않습니다.

## 16. 여러 작품 Overlay 동시 표시

Manager 는 "현재 열린 Overlay" 를 **강제하지 않습니다**. 각 작품이 자기 Overlay 만 제어합니다.

- Exhibit_A 클릭 → Overlay_A 열림
- Exhibit_B 클릭 → Overlay_B 열림 (A 는 그대로)
- Exhibit_C 클릭 → Overlay_C 열림 (A, B 그대로)

> **"한 번에 하나만" 로 바꾸고 싶다면?**
> Manager 에 `_lastOpened` 필드를 두고 `_RegisterTick` 대신 별도의
> `_NotifyOpened(ExhibitOverlay)` 를 만들어 이전 것을 `_Close()` 하면 됩니다.
> 현재 요구사항(9번)은 동시 표시이므로 구현하지 않았습니다.

## 17. 작품이 Disable 될 때 자동 Close

2중 안전장치:

1. **Overlay 가 작품의 자식인 경우 (기본 구조)**
   부모가 꺼지면 `ExhibitOverlay.OnDisable()` 이 실행되어
   상태/알파/스케일/스크롤을 전부 초기화합니다. 다시 켜도 "열린 채로" 남지 않습니다.

2. **Overlay 를 작품 밖에 배치한 경우**
   `ExhibitInteractable.OnDisable()` 이 `overlay.gameObject.activeInHierarchy` 를 확인하고
   활성 상태일 때만 `_CloseImmediate()` 를 호출합니다.

또한 Manager 의 틱 루프는 매 프레임 `activeInHierarchy` 를 확인해
비활성 Overlay 를 목록에서 스스로 제거합니다. (재진입/유령 참조 방지)

## 18. Fade + Scale Open/Close 애니메이션

```csharp
float e = t * t * (3f - 2f * t);              // SmoothStep
canvasGroup.alpha = e;
scaleRoot.localScale = baseScale * (0.92f + 0.08f * e);
```

- `_t` 는 0(닫힘) ↔ 1(열림) 사이의 진행률. Open 이면 증가, Close 면 감소.
  **같은 변수를 양방향으로 쓰기 때문에 중간에 방향이 바뀌어도 끊김이 없습니다.**
- 기본 0.18s / 0.12s. VR 에서 과하지 않은 값입니다.
- `baseScale` 은 첫 초기화 때 캡처하므로, Panel 크기를 바꿔도 애니메이션이 깨지지 않습니다.

> **VRCTween / DOTween 은?**
> DOTween 은 Udon 화이트리스트 밖이라 사용할 수 없고,
> World SDK 에는 Udon 에서 호출 가능한 범용 Tween API 가 없습니다.
> Animator 로도 가능하지만 작품 100개 × Animator = 상시 비용이 발생합니다.
> **Manager 단일 Update 에서 직접 보간하는 지금 방식이 VRChat 에 가장 적합합니다.**

## 19. Interaction Proximity (기본 2m, 작품별 변경)

`Setup ...` 실행 시 Editor 도구가 `interactionProximity` 값을
UdonBehaviour 의 `proximity` 직렬화 필드에 직접 기록합니다. (SDK 기본값도 2m)

작품별로 바꾸려면 해당 작품의 `Interaction Proximity` 를 수정한 뒤
`Setup Selected Exhibits` 또는 `Setup All Exhibits In Scene` 을 다시 실행하세요.
큰 조각상은 `4`, 작은 액자는 `1.5` 처럼 자유롭게 설정하면 됩니다.

> **런타임에는 바꿀 수 없습니다.**
> `proximity` 는 `UdonBehaviour` 쪽 필드이고 `UdonSharpBehaviour` 에는 해당 멤버가 없습니다.
> (Worlds SDK 3.10.4 기준. `InteractionText` 는 있지만 `proximity` 는 없습니다.)
> 그래서 거리 설정은 **Editor 단계 굽기 전용**입니다. Interact 문구만 언어 전환에 맞춰
> 런타임에 갱신됩니다.

## 20. Interact 표시 문구 (기본 "설명", 작품별 변경)

우선순위:

```
작품의 interactionText(현재 언어)
  → 비어 있으면 Manager 의 defaultInteractionText(현재 언어)   ← 기본 "설명"
    → 그래도 비어 있으면 "설명"
```

- 전체를 한 번에 바꾸려면 **Manager 의 값만** 수정하면 됩니다.
- 특정 작품만 `체험하기`, `Play Audio` 처럼 바꾸고 싶으면 그 작품의 필드만 채웁니다.
- 언어 전환 시 `_OnLanguageChanged()` 가 툴팁도 함께 갱신합니다.

## 21. Local Only 동작 보장

| 보장 수단 | 내용 |
|---|---|
| `[UdonBehaviourSyncMode(BehaviourSyncMode.None)]` | 모든 스크립트에 적용. 동기화 변수 자체가 생성되지 않음 |
| `[UdonSynced]` 없음 | 동기화 필드 0개 |
| `RequestSerialization()` 없음 | 네트워크 전송 호출 0회 |
| `Networking.SetOwner()` 없음 | 오너십 이동 없음 |
| `SendCustomNetworkEvent()` 없음 | 네트워크 이벤트 0회 |
| public 메서드 `_` 접두사 | `_Open`, `_Close` 등은 VRChat 규약상 **네트워크로 호출 불가** |
| `Interact()` | 누른 플레이어의 클라이언트에서만 실행되는 로컬 이벤트 |

결과: A 플레이어가 Overlay 를 열어도 B 플레이어 화면에는 아무 변화가 없습니다.

## 22. Exhibit Prefab 제작 순서

1. `Tools > Exhibit Descriptor > Create Exhibition Root`
2. `Tools > Exhibit Descriptor > Create Exhibit (Template)`
3. `Artwork` 를 실제 작품 Mesh 로 교체 (Collider 는 붙이지 않음)
   템플릿의 `Artwork` 는 **완전히 투명한 Placeholder** 입니다.
   (`Assets/ExhibitDescriptor/Materials/ExhibitPlaceholder.mat` — 프로젝트에 1개만 생성되어 공유됩니다.
   Unity 기본 머티리얼은 불투명 흰색이라 흰 상자가 Overlay 배치를 가립니다.)
   Mesh 는 남아 있으므로 Hierarchy 에서 `Artwork` 를 선택하면 Scene View 에 Bounds 가 보여
   위치·크기는 그대로 조절할 수 있습니다. 눈에 보이지 않아 교체를 잊기 쉬우므로
   `Validate Scene` 이 아직 Placeholder 인 작품을 **경고**로 알려 줍니다.
4. `InteractionArea` 의 BoxCollider 크기를 작품에 맞춤
5. `OverlayAnchor` 를 작품 옆 원하는 위치/각도로 배치
6. 모든 TMP 의 Font Asset 을 CJK Font Asset 으로 교체
7. `Tools > Exhibit Descriptor > Setup Selected Exhibits`
8. `Overlay` 를 **비활성**으로 두고 Prefab 으로 저장

## 23. 새 작품을 추가하는 실제 작업 순서

1. `Exhibit_Base` Prefab 을 Scene 에 드래그
2. 이름 변경 (`Exhibit_042`)
3. 위치/회전 배치
4. `Artwork` 의 Mesh/Material 교체 (투명 Placeholder 라 화면에는 아무것도 보이지 않습니다)
5. Inspector 에서 `Title KR/EN/JP`, `Description KR/EN/JP` 입력
6. (필요 시) `Interaction Proximity`, `OverlayAnchor` 위치 조정
7. 끝. **Manager 에 등록하는 작업은 없습니다.**

전부 배치한 뒤 마지막에 한 번:
```
Tools > Exhibit Descriptor > Setup All Exhibits In Scene
Tools > Exhibit Descriptor > Validate Scene
```

> `Auto Setup On Save` 가 켜져 있으면 첫 줄은 저장할 때 자동으로 돕니다.

**작품 Mesh 를 먼저 배치하는 방식이라면** 위 1~4번 대신 이렇게 하세요.

1. 작품 Mesh 들을 원하는 위치에 전부 배치
2. Hierarchy 에서 전부 선택 → `Create Exhibits From Selected Meshes`
3. 각 작품의 `Description KR` 만 입력 (Title 은 오브젝트 이름으로 채워져 있음)

## 24. 100개 이상 관리 권장 구조

```
ExhibitionRoot
├─ ExhibitManager
├─ Room_1F
│  ├─ Exhibit_001 ... Exhibit_040
├─ Room_2F
│  ├─ Exhibit_041 ... Exhibit_080
└─ Room_3F
   └─ Exhibit_081 ... Exhibit_120
```

- 층/구역 단위 부모를 두면 **구역 전체를 통째로 비활성화**할 수 있고,
  이때 열려 있던 Overlay 는 17번 로직으로 자동 정리됩니다.
- 이름은 `Exhibit_001` 처럼 3자리 0패딩 → Hierarchy 정렬이 안정적입니다.
- 텍스트를 외부(CSV/스프레드시트)에서 관리한다면,
  Editor 스크립트로 CSV 를 읽어 `titleKR/descriptionKR...` 필드에 써 넣는 임포터를 추가하면 됩니다.
  (`SerializedObject.FindProperty("titleKR").stringValue = ...` 패턴 — 25번 참조)
- **런타임 성능은 작품 수와 거의 무관합니다.** 비용은 "동시에 열린 Overlay 수"에만 비례합니다.

## 25. Editor 자동화 방안

`Editor/ExhibitDescriptorTools.cs` 가 제공하는 메뉴:

| 메뉴 | 기능 |
|---|---|
| Create Exhibition Root | Root + Manager 생성 (**활성 Scene** 기준으로 중복 검사) |
| Create Exhibit (Template) | Overlay/버튼/Collider 포함 작품 1개 전체 생성 + 자동 연결 |
| **Create Exhibits From Selected Meshes** | 선택한 Mesh 를 작품으로 **일괄 변환** (아래 참조) |
| Setup Selected Exhibits | 선택 작품/언어 전환 버튼의 참조 자동 연결 + Interact 값 베이크 |
| Setup All Exhibits In Scene | 열려 있는 모든 Scene 일괄 처리 (연결은 **각자의 Scene** 안에서만) |
| **Auto Setup On Save** | Scene 저장 시 그 Scene 에 Setup 자동 실행 (토글, 기본 ON) |
| Validate Scene | 누락 참조 / Collider 없음 / Canvas 모드 오류 등을 콘솔에 보고 |

**Create Exhibits From Selected Meshes**

이미 Scene 에 배치해 둔 작품 Mesh 를 골라 한 번에 Exhibit 으로 감쌉니다.
Mesh 하나당 Exhibit 하나가 생기고, Mesh 는 **World 위치를 유지한 채** 그 아래로 들어갑니다.
(이름은 바꾸지 않으므로 Hierarchy 에서 그대로 찾을 수 있습니다.)

| 항목 | 자동 계산 |
|---|---|
| 이름 | `Exhibit_###` — Scene 에 있는 마지막 번호 다음부터 (기존 작품을 덮지 않음) |
| `InteractionArea` BoxCollider | 작품 Bounds + 사방 `0.15m`, 깊이는 최소 `0.3m` |
| `OverlayAnchor` | 작품 오른쪽 끝 + `0.15m` + Panel 절반폭(`0.3m`), 높이는 작품 중심 |
| `Title KR` | 원본 오브젝트 이름. EN/JP 는 **비움** → KR 로 fallback |
| 참조 연결 / Interact 베이크 | `SetupExhibitFull` 까지 자동 실행 |

- Bounds 는 자식 Renderer 를 전부 감싸는 AABB 를 **Exhibit Root 로컬 좌표**로 다시 계산합니다.
  (World AABB 의 8개 꼭짓점을 각각 변환 → 회전된 작품에서도 어긋나지 않음)
- Exhibit Root 의 `localScale` 은 항상 `1` 입니다. 부모 Scale 이 딸려 들어오면
  World Space Canvas(`0.001`) 까지 함께 찌그러지기 때문입니다.
- 낮은 좌대/조각은 Anchor 높이를 `bounds.min.y + Panel 절반높이` 로 올려 Panel 이 바닥에 묻히지 않게 합니다.
- 건너뛰는 대상: Scene 밖 오브젝트, Renderer 없음, 이미 Exhibit 안, 선택된 다른 오브젝트의 자식.
  전부 이유를 콘솔에 남깁니다.
- 작품 Mesh 에 Collider 가 남아 있으면 **경고**합니다. Interact 레이가 거기서 막히면
  `InteractionArea` 가 반응하지 않습니다. (지우지는 않습니다 — 물리 충돌용일 수 있으므로)

**Auto Setup On Save**

`EditorSceneManager.sceneSaving` (디스크에 쓰기 **직전**) 에 걸려 있어,
여기서 채운 참조와 구운 Interact 값이 그대로 저장 파일에 들어갑니다.

- **저장하는 Scene 만** 처리합니다. Additive 로 열어 둔 다른 Scene 은 건드리지 않습니다.
- Play 중 저장은 건너뜁니다.
- 설정은 `EditorPrefs` 라 프로젝트가 아니라 이 PC 의 Unity 에 저장됩니다.
- 작품 수가 많아 저장이 느려지면 메뉴에서 끄고 `Setup All Exhibits In Scene` 을 필요할 때만 쓰세요.

**Additive Scene 규칙**

- Manager 는 "Scene 당 1개" 입니다. 검색·생성·연결·검증이 전부 Scene 단위로 동작합니다.
- 다른 Scene 에 Manager 가 있어도 활성 Scene 에 새로 만들 수 있습니다.
- 같은 Scene 에 Manager 가 없으면 **다른 Scene 것을 대신 연결하지 않고** 누락으로 보고합니다.
  (교차 Scene 참조는 그 Scene 이 언로드되는 순간 깨집니다.)
- 오브젝트를 다른 Scene 으로 복사/이동해서 `manager` 가 예전 Scene 을 가리키게 되면,
  Setup 이 **같은 Scene 의 Manager 로 교체**합니다. 같은 Scene 에 Manager 가 없으면
  깨진 참조를 그대로 두지 않고 **비우면서 경고**합니다. (이미 같은 Scene 을 가리키는
  올바른 참조는 건드리지 않습니다)
- `Validate Scene` 은 Scene 별로 Manager 개수를 세므로,
  Additive 로 3개 Scene 을 열고 각 Scene 에 1개씩 둔 정상 구성은 그대로 통과합니다.
  대신 한 Scene 에 2개가 있거나, 작품만 있고 Manager 가 없는 Scene 이 있으면 오류로 잡습니다.

확장 아이디어:

- **CSV 임포터**: `작품ID, titleKR, titleEN, titleJP, descKR, ...` 를 읽어 일괄 주입
- **일괄 Proximity 변경**: 선택한 작품 전체의 `interactionProximity` 를 한 번에 수정

> `Anchor 자동 배치` 는 `Create Exhibits From Selected Meshes` 에 구현되어 있습니다.

## 26. ClientSim 테스트 방법

1. `Project Settings > VRChat SDK > ClientSim` 에서 **Enable ClientSim** 체크
2. Scene 에 `VRCWorld` Prefab (Scene Descriptor + Spawn) 배치
3. Play
4. 조작
   - `WASD` 이동, 마우스 시선
   - 작품을 조준하면 손 아이콘 + `설명` 표시 → **좌클릭** 으로 Interact
   - `Esc` 로 마우스 잠금 해제
5. 확인할 로그: `ExhibitManager.Debug Log = true` 로 켜면
   `[ExhibitManager] Register exhibit: ...` 가 작품 수만큼 출력됩니다.
   이 숫자가 실제 작품 수와 다르면 비활성 작품이 있거나 Manager 탐색이 실패한 것입니다.

## 27. VRChat Build & Test 검증 체크리스트

**빌드 전**
- [ ] `Tools > Exhibit Descriptor > Validate Scene` 통과
- [ ] Console 에 UdonSharp 컴파일 에러 없음
- [ ] Scene 에 `EventSystem` 이 없음 (uGUI Button 을 안 쓰는 경우)
- [ ] 모든 Overlay 가 **비활성** 상태로 저장됨
- [ ] 모든 TMP 가 CJK Font Asset 사용
- [ ] 버튼 Layer = Default, Collider 존재
- [ ] VRCSceneDescriptor + SpawnPoint 존재

**빌드 후 (Desktop)**
- [ ] 2m 안에서만 Interact 표시가 뜬다
- [ ] Overlay Open/Close 애니메이션 정상
- [ ] 동일 작품 재클릭 → 닫힘
- [ ] 여러 작품 동시 오픈
- [ ] 스크롤 Up/Down 정상, 짧은 글에서는 안 움직임
- [ ] 언어 전환 KR/EN/JP 전부 정상, 글자 깨짐 없음
- [ ] 작품 비활성화 시 Overlay 자동 소멸

**빌드 후 (VR)**
- [ ] 레이저로 작품/버튼 조준 가능
- [ ] 애니메이션이 짧고 부드러움
- [ ] Overlay 가 눈높이·읽기 좋은 거리

**멀티플레이어 (Local Test 2인)**
- [ ] A 가 연 Overlay 가 **B 화면에는 보이지 않음**
- [ ] B 가 다른 작품을 열어도 A 에 영향 없음
- [ ] Debug 창에 네트워크 전송 트래픽 증가 없음

## 28. 자주 발생하는 VRChat / UdonSharp 오류와 해결

| 증상 | 원인 | 해결 |
|---|---|---|
| 작품을 봐도 Interact 표시가 안 뜬다 | **Collider 와 UdonBehaviour 가 다른 GameObject** | `InteractionArea` 에 `ExhibitInteractRelay` 를 붙이거나 Collider 를 작품 Root 로 이동 (Validate 가 잡아냅니다) |
| 〃 | 거리/Layer 문제 | `Proximity` 확인, Layer 를 Default 로 |
| `VRC Ui Shape` 컴포넌트를 못 찾겠다 | **SDK2 전용 컴포넌트** | SDK3(Udon) 월드에는 존재하지 않습니다. 추가하지 마세요 |
| 버튼이 안 눌린다 | 버튼 Layer 가 `UI` | 버튼 GameObject Layer 를 **Default** 로 변경 (Validate 가 경고함) |
| Overlay 가 월드 입장 시부터 떠 있다 | Overlay 가 활성 상태로 저장됨 | Prefab 에서 Overlay 를 비활성으로 저장 |
| 한글/일본어가 □ 로 나온다 | LiberationSans SDF 사용 중 | CJK Font Asset 지정 (1단계) |
| 텍스트가 잘려서 안 보인다 | Content 의 ContentSizeFitter 누락 | `Vertical Fit = Preferred Size` 설정 |
| 스크롤이 안 움직인다 | Content Pivot/Anchor 가 Top 이 아님 | Anchor Top-Stretch, Pivot (0.5, 1) |
| 스크롤이 끝까지 안 간다 | Viewport 높이 계산 대상이 잘못 연결됨 | `scrollViewport` = `Viewport`, `scrollContent` = `Content` |
| `The type or namespace 'ScrollRect'...` | Udon 미지원 API 사용 | 본 구현은 ScrollRect 를 쓰지 않습니다 (12번 참조) |
| `proximity` 컴파일 에러 | `UdonSharpBehaviour` 에 없는 멤버 (`UdonBehaviour` 쪽 필드) | 런타임 코드에서 `proximity` 대입을 제거. 값은 Editor 도구가 굽습니다 (19번 참조) |
| `GameObject.scene` / `GetRootGameObjects` 컴파일 에러 | Udon 이 `SceneManagement.Scene` 타입을 미노출 | 런타임에서 Scene 비교를 제거하고 `Setup All Exhibits In Scene` 으로 `manager` 를 연결 (7단계 참조) |
| `Cannot find method _Open` | Overlay 가 비활성인데 메서드 호출 | 먼저 `SetActive(true)` 후 호출 — 본 구현은 이미 그렇게 되어 있음 |
| 언어 전환이 일부 작품에만 적용 | 해당 작품이 비활성이었음 | 정상 동작. 다시 활성화되면 새 언어로 표시됨 |
| Manager 를 못 찾는다 | Manager 오브젝트 이름이 다름 | 이름을 `ExhibitManager` 로 하거나 작품의 `Manager Object Name` 을 맞춤 |
| `ExhibitManager 를 찾지 못했습니다` 경고 | 작품과 같은 Scene 에 Manager 가 없거나 이름이 다름 | 그 Scene 에 `Create Exhibition Root` 실행 후 `Setup All Exhibits In Scene` |
| 언어 전환 버튼마다 표시 언어가 다름 | (구버전) 라벨이 각자 갱신됨 | 현재 구현은 Manager 가 모든 버튼에 브로드캐스트합니다. `Setup` 을 실행해 `manager` 를 연결하세요 |
| `Multiple EventSystems in scene` 경고 | 씬에 EventSystem 이 있음 | 삭제 (VRChat 이 자체 제공) |
| Prefab 복제 후 Manager 참조가 비어 있음 | Scene 참조는 Prefab 에 저장 안 됨 | 정상. `Setup All Exhibits In Scene` 실행 or 런타임 자동 Find |
| Overlay 가 벽에 파묻힌다 | Anchor 가 벽 안쪽 | `OverlayAnchor` 를 벽에서 3~5cm 앞으로 |
| 빌드 시 `Udon Behaviour serialization` 에러 | U# 프록시와 UdonBehaviour 불일치 | `VRChat SDK > Utilities > Reserialize All Udon Assets` 실행 |
| Overlay 가 다른 오브젝트 뒤에 그려진다 | World Space Canvas 정렬 | Canvas 의 `Sorting Layer`/`Order in Layer` 조정 또는 Anchor 를 앞으로 |

---

# PART C. 다른 구현(`ExhibitionSystem`)과의 비교 및 반영 내역

같은 요구사항으로 만들어진 별도 구현을 검토하고, 더 나은 부분은 가져오고
위험한 부분은 반면교사로 삼았습니다.

## C-1. 가져온 것 (개선 반영 완료)

| # | 배운 점 | 반영 내용 |
|---|---|---|
| 1 | **Interact 는 Collider 와 UdonBehaviour 가 같은 GameObject 여야 안전** | `ExhibitInteractRelay` 신규 추가. `InteractionArea` 에 Collider + 릴레이를 함께 두고 작품 Root 의 데이터와 분리. Validate 에서 "Collider 가 자식에만 있고 UdonBehaviour 가 없음" 을 **에러로 검출** |
| 2 | `Start()` 에서 Overlay 를 강제로 닫기 | 편집 중 Overlay 를 켜 둔 채 저장해도 월드 입장 시 반드시 닫힘. 기존에는 "비활성으로 저장하세요" 경고에만 의존했음 |
| 3 | 언어 전환 시 스크롤을 맨 위로 | 언어마다 본문 길이가 달라 스크롤 위치가 어긋나던 문제 해결 (`_ScrollToTop()`) |
| 4 | fallback 을 KR → EN → **JP** 까지 | 기존엔 KR → EN 까지만. JP 만 채운 작품도 정상 표시 |
| 5 | `[Min]` / `[Range]` / `[TextArea]` 인스펙터 제약 | `openDuration`, `closeDuration`, `startScaleMultiplier`, `scrollStep`, `proximity` 등에 적용. 0초·음수 입력으로 인한 사고 방지 |
| 6 | Editor 도구에 **Undo 지원** | `ApplyModifiedPropertiesWithoutUndo` → `ApplyModifiedProperties`. 작품 100개 일괄 Setup 후 Ctrl+Z 가능 |
| 7 | `UdonSharpEditorUtility.CopyProxyToUdon()` 명시 호출 | 에디터 스크립트가 바꾼 값이 UdonBehaviour 에 확실히 반영되도록 |
| 8 | 같은 **Scene** 의 Manager 만 연결 | Additive Scene 환경에서 다른 씬의 Manager 를 물어버리는 문제 방지 (`FindManagerForScene`) |
| 9 | 참조 누락 시 런타임 경고 | `overlay` 미연결, Manager 없음/비활성 시 `Debug.LogWarning` 으로 원인 표시 |
| 10 | Manager 오브젝트 비활성 검사 | Manager 가 꺼져 있으면 Update 틱이 안 돌아 애니메이션이 멈춤 → Validate 에러 + 런타임엔 애니메이션 생략하고 즉시 열림/닫힘으로 폴백 |
| 11 | Overlay 를 Anchor 아래에 두면 Scene View 와 런타임 위치가 일치 | 계층은 그대로 두되, Setup 도구가 **에디터에서 Overlay 를 Anchor 로 미리 스냅**하도록 추가. WYSIWYG 확보 |

## C-2. 반면교사 (의도적으로 다르게 간 부분)

### ① public 메서드에 `_` 접두사가 없음 → Local Only 가 깨질 수 있음

상대 구현은 `CloseOverlay()`, `ScrollUp()`, `SetLanguageKr()` 등이 전부 `_` 없는 public 입니다.
VRChat 규약상 **`_` 로 시작하지 않는 public 메서드는 `SendCustomNetworkEvent` 로 원격 호출이 가능**합니다.
"Local Only" 가 핵심 요구사항(14번)인데 원격 호출 창구를 열어 둔 셈입니다.

- 본 구현은 모든 공개 메서드가 `_Open`, `_Close`, `_ScrollUp`, `_SetLanguage` 처럼 `_` 로 시작합니다.
- `_` 접두사는 **로컬** `SendCustomEvent` 는 그대로 허용하므로 uGUI 버튼 연결에도 지장이 없습니다.
  즉 안전성을 포기할 이유가 없습니다.

### ② `ScrollRect.verticalNormalizedPosition` 사용

- Udon 에서 `ScrollRect` API 는 버전 의존성이 있어 위험합니다.
- 더 큰 문제는 **정규화 비율(0~1)** 이라 `scrollStep = 0.2` 가
  10줄짜리 글에서는 2줄, 200줄짜리 글에서는 40줄을 넘깁니다. 글마다 조작감이 달라집니다.
- 본 구현은 `RectTransform.anchoredPosition` 을 **픽셀 단위**로 움직입니다.
  글 길이와 무관하게 한 번에 넘어가는 양이 일정하고, 내용이 짧으면 스크롤이 자동으로 잠깁니다.

### ③ Manager 가 `exhibits[]` 배열을 직접 보유

- 작품을 추가/삭제할 때마다 `Auto Wire Scene` 을 **다시 실행해야** 합니다.
  잊으면 그 작품만 언어 전환이 안 됩니다 (상대 구현의 트러블슈팅에도 이 항목이 있습니다).
- 배열에 파괴된 참조가 남을 수도 있습니다.
- 본 구현은 작품이 `OnEnable` 에서 **스스로 등록**하고 `OnDisable` 에서 해제합니다.
  요구사항 24("반복적인 수동 연결 작업 최소화")에 직접 대응합니다.
  Editor 도구는 "있으면 더 좋은" 보조 수단이지, 실행하지 않으면 깨지는 필수 단계가 아닙니다.

### ④ 언어 전환 시 닫힌 Overlay 까지 전부 갱신

- 상대 구현은 모든 작품의 TMP `text` 를 다시 씁니다.
  TMP 텍스트 대입은 메시 재생성을 유발하므로 작품 100개면 한 프레임에 100회 리빌드입니다.
- 본 구현은 **열려 있는 Overlay 만** 갱신하고, 닫힌 작품은 다음에 열릴 때 자연스럽게 채웁니다.

### ⑤ `VRC Ui Shape` 안내

- 상대 가이드는 Canvas 에 `VRC Ui Shape` 를 추가하라고 안내하지만,
  이는 **SDK2 전용 컴포넌트로 SDK3(Udon) 월드에는 존재하지 않습니다.**
  그대로 따라 하면 "컴포넌트를 찾을 수 없음" 에서 막힙니다.
- 본 구현은 uGUI EventSystem 경로 자체를 쓰지 않고 Udon Interact 로 처리하므로
  이 문제가 발생하지 않습니다.

### ⑥ 애니메이션을 `SendCustomEventDelayedFrames` 로 자가 예약

이건 **우열이 아니라 트레이드오프**라 판단해 기존 방식을 유지했습니다.

| | 상대 구현 (지연 이벤트) | 본 구현 (Manager 단일 틱) |
|---|---|---|
| Update 개수 | 0개 | 1개 (유휴 시 즉시 return) |
| Manager 의존 | 없음 | 있음 → **비활성 시 즉시 확정으로 폴백 추가** |
| 동시 5개 애니메이션 시 | 프레임당 이벤트 디스패치 5회(문자열 조회 포함) | Update 1회 + 직접 호출 5회 |

동시에 여러 Overlay 가 열리는 것이 기본 시나리오(요구사항 9)이므로 중앙 틱이 유리하다고 봤고,
대신 상대 구현의 장점(Manager 없이도 동작)은 폴백 경로로 흡수했습니다.

## C-3. 검증

반영 후 `Tools > Exhibit Descriptor > Validate Scene` 이 추가로 검출하는 항목:

- Collider 가 자식에만 있고 그 GameObject 에 UdonBehaviour 가 없음 (**에러**)
- `ExhibitInteractRelay.target` 미연결 (**에러**)
- `ExhibitManager` 오브젝트가 비활성 (**에러**)
- 작품/언어 전환 버튼의 `manager` 가 **다른 Scene** 을 가리킴 (**에러**)
- 작품은 있는데 그 Scene 에 `ExhibitManager` 가 없음 (**에러**)
- 한 Scene 안에 `ExhibitManager` 가 2개 이상 (**에러**, 다른 Scene 의 Manager 는 세지 않음)
- `Artwork` 가 아직 투명 Placeholder 머티리얼 (**경고** — 일부러 비워 두는 구성도 있으므로 에러는 아님)

---

## 요구사항 대응 요약

| # | 요구사항 | 구현 |
|---|---|---|
| 1 | UdonSharp | ✅ 전 스크립트 U# |
| 2 | 작품이 데이터 직접 보유 | ✅ `ExhibitInteractable` Inspector |
| 3 | Overlay 위치 Inspector 지정 | ✅ `OverlayAnchor` Transform |
| 4 | 고정 방향 | ✅ Anchor rotation 사용, 회전 로직 없음 |
| 5 | TextMeshProUGUI | ✅ |
| 6 | 표시 정보 Inspector 설정 | ✅ Show Title/Subtitle/Description/ExtraInfo |
| 7 | 텍스트 전용 | ✅ 이미지 없음 |
| 8 | Close 버튼 + 재클릭 토글 | ✅ |
| 9 | 다중 Overlay 동시 표시 | ✅ 작품별 독립 |
| 10 | Fade + Scale 애니메이션 | ✅ SmoothStep, 0.18/0.12s |
| 11 | 거리 작품별 설정 (기본 2m) | ✅ |
| 12 | BoxCollider, Mesh 분리 | ✅ `InteractionArea` + `ExhibitInteractRelay` (영역 다중 배치 가능) |
| 13 | Interact 문구 (기본 "설명") | ✅ 3개 국어 지원 |
| 14 | Local Only | ✅ SyncMode.None, 동기화 0 |
| 15 | 작품당 Overlay 1개 | ✅ |
| 16 | Disable 시 자동 Close | ✅ 2중 안전장치 |
| 17 | ScrollView | ✅ RectMask2D 기반 |
| 18 | Up/Down 버튼 | ✅ Interact 버튼 + 보간 |
| 19 | CJK Font | ✅ 생성 가이드 포함 |
| 20 | KR/EN/JP + 전환 + 기본 언어 | ✅ |
| 21 | PC Desktop / PC VR | ✅ |
| 22 | 기본 Interact 표시 | ✅ Highlight 없음 |
| 23 | 열린 작품 Highlight 없음 | ✅ |
| 24 | 100개 이상 | ✅ 자기 등록 + 단일 Update |
| 25 | Prefab 기반 | ✅ |
| 26 | Manager 1개 | ✅ |
