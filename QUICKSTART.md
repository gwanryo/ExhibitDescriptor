# 빠른 시작 (10분)

README 는 레퍼런스입니다. **처음에는 이 문서만 보세요.**
작품 1개를 클릭해서 설명 Overlay 를 띄우는 데까지가 목표입니다.

---

## 0. 준비 (1회만)

VCC 로 만든 VRChat World 프로젝트에 다음이 들어 있어야 합니다.

- `VRChat SDK - Worlds`
- `UdonSharp`
- `ClientSim` (Play 모드 테스트용)

이 폴더를 통째로 `Assets/ExhibitDescriptor/` 에 복사합니다.
Unity 가 컴파일을 끝내면 상단에 **`Tools > Exhibit Descriptor`** 메뉴가 생깁니다.
→ 이 메뉴가 안 보이면 Console 에 컴파일 에러가 있는 겁니다. 거기서 멈추고 에러부터 보세요.

---

## 1. 만들기 — 메뉴 2번

```
Tools > Exhibit Descriptor > Create Exhibition Root
Tools > Exhibit Descriptor > Create Exhibit (Template)
```

Hierarchy 에 이렇게 생깁니다.

```
ExhibitionRoot
├─ ExhibitManager      ← Scene 당 1개. 언어 상태만 관리
└─ Exhibit_New         ← 작품 1개. 여기에 제목/설명을 입력
```

`Exhibit_New` 안의 Overlay·버튼·Collider·참조 연결은 **전부 자동으로 만들어져 있습니다.**
수동으로 드래그해서 연결할 것은 없습니다.

---

## 2. 채우기 — Inspector 3칸

Hierarchy 에서 **`Exhibit_New`** 를 선택하고 `ExhibitInteractable` 컴포넌트에서:

| 칸 | 넣을 값 |
|---|---|
| `Title KR` | 작품 제목 |
| `Description KR` | 설명 본문 |
| (선택) `Subtitle KR` | 작가·연도 |

EN / JP 는 **비워 둬도 됩니다.** 비어 있으면 자동으로 KR 로 대체됩니다.
나머지 필드는 지금 건드리지 마세요. 기본값이 정답입니다.

---

## 3. 작품 Mesh 넣기

`Exhibit_New/Artwork` 가 **투명한 placeholder Cube** 입니다. 여기가 작품 자리입니다.

- `Artwork` 의 Mesh/Material 을 실제 작품 것으로 교체하거나
- `Artwork` 아래에 작품 fbx/prefab 을 자식으로 드래그 (placeholder 는 삭제해도 됨)

> **Collider 는 절대 붙이지 마세요.**
> 클릭 판정은 옆의 `InteractionArea` 가 전담합니다.
> 작품 Mesh 에 Collider 가 있으면 Interact 가 그쪽으로 새서 오히려 안 눌립니다.

작품 크기를 크게 바꿨다면 두 개만 맞춰 주세요.

- `InteractionArea` 의 BoxCollider 크기 → 작품을 덮도록
- `OverlayAnchor` 위치 → 설명이 뜰 자리 (기본: 오른쪽 0.9m, 높이 1.5m)

---

## 4. 테스트

1. Scene 에 `VRCWorld` Prefab (Scene Descriptor + SpawnPoint) 이 있는지 확인
2. Play
3. 작품 쪽으로 걸어가면 화면 중앙에 **`설명`** 툴팁 → **좌클릭**
4. Overlay 가 작품 옆에 뜨면 성공. 다시 클릭하면 닫힙니다.

---

## 작품 늘리기

### 이미 작품 Mesh 를 Scene 에 배치해 뒀다면 (가장 빠름)

Hierarchy 에서 **작품 Mesh 들을 전부 선택**하고:

```
Tools > Exhibit Descriptor > Create Exhibits From Selected Meshes
```

선택한 Mesh 하나당 Exhibit 이 하나씩 만들어지고, 각 Mesh 는 World 위치를 유지한 채
자기 Exhibit 안으로 들어갑니다. 100개를 한 번에 선택해도 됩니다.

자동으로 계산되는 것:

| 항목 | 계산 방식 |
|---|---|
| 이름 | `Exhibit_001`, `Exhibit_002` … (Scene 에 있는 마지막 번호 다음부터) |
| `InteractionArea` 크기 | 작품 Bounds + 사방 0.15m (깊이는 최소 0.3m) |
| `OverlayAnchor` 위치 | 작품 오른쪽 끝 + 0.15m 여백, 높이는 작품 중심 (바닥에 묻히지 않게 보정) |
| `Title KR` | 원본 오브젝트 이름 (EN/JP 는 비워 두어 KR 로 fallback) |
| 참조 연결 / Interact 값 | Setup 까지 자동 실행 |

남는 일은 **Description 입력**뿐입니다.

> 원본 오브젝트의 **이름은 바꾸지 않습니다.** Hierarchy 에서 그대로 찾을 수 있습니다.
> 작품 Mesh 에 Collider 가 붙어 있으면 Console 에 경고가 뜹니다. Interact 가 막히는 원인이니
> 물리 충돌 용도가 아니라면 지우세요.

건너뛰는 것: Renderer 가 없는 오브젝트, 이미 Exhibit 안에 있는 오브젝트,
선택 목록 안의 다른 오브젝트의 자식. 전부 이유와 함께 Console 에 남습니다.

### 아직 Mesh 가 없다면

1. `Exhibit_New` 를 `Ctrl+D` 로 복제 (또는 Prefab 으로 저장해 두고 드래그)
2. 위치를 옮기고 Title / Description 만 바꾸기

> `Create Exhibit (Template)` 은 **원본 1개를 만드는 도구**이고,
> **선택한 오브젝트를 부모로 삼습니다.** `Room_1F` 을 선택하고 실행하면 그 아래에 생깁니다.

---

## Setup 은 언제 눌러야 하나

### 그냥 저장하세요

```
Tools > Exhibit Descriptor > Auto Setup On Save   ← 기본 ON (체크 표시)
```

켜져 있으면 **Scene 을 저장할 때(Ctrl+S) 그 Scene 의 작품 전체에 Setup 이 자동으로 돕니다.**
평소에는 Setup 메뉴를 누를 일이 없습니다. 저장 직전에 처리하므로 결과가 그대로 파일에 들어갑니다.

작품이 수백 개라 저장이 느려지면 이 메뉴를 눌러 끄고 필요할 때만 수동으로 돌리세요.
(설정은 프로젝트가 아니라 이 PC 의 Unity 에 저장됩니다)

### 수동으로 누를 때도 하나씩이 아닙니다

`Setup Selected Exhibits` 는

- Ctrl 로 **여러 개 선택**해도 한 번에 처리하고
- **부모 하나만** 선택해도 그 아래 자식 작품을 전부 처리합니다 (`ExhibitionRoot` 선택 → 전부)
- 선택조차 귀찮으면 `Setup All Exhibits In Scene` (열린 Scene 전체)

그리고 `Create Exhibit (Template)` 과 `Create Exhibits From Selected Meshes` 는
**생성할 때 Setup 을 이미 자동 실행**합니다. 갓 만든 작품에 또 누를 필요 없습니다.

### 수동 Setup 이 실제로 필요한 경우

Auto Setup 을 꺼 뒀다면 이 둘만 기억하면 됩니다.

| 상황 | 이유 |
|---|---|
| `Interaction Proximity` / `Interaction Text` 를 바꿨을 때 | 이 둘만 UdonBehaviour 직렬화 필드에 **굽는** 값입니다 |
| Prefab 인스턴스를 Scene 에 새로 배치했을 때 | `manager` 는 Scene 참조라 Prefab Asset 에 저장되지 않습니다 |

**Title / Description 만 바꿨다면 Setup 은 필요 없습니다.** 런타임에 필드에서 바로 읽습니다.

---

## 그 밖에

- **한글이 □ 로 보임** — 폰트 문제입니다. README 의 `1단계 > CJK 폰트 준비` 만 보세요
- **뭔가 이상함** — `Tools > Exhibit Descriptor > Validate Scene` 이 원인을 Console 에 찍어 줍니다

---

## 증상별 응급처치

| 증상 | 원인 |
|---|---|
| 툴팁이 안 뜬다 | `Setup Selected Exhibits` 를 안 돌렸거나, 2m 밖에 서 있음 |
| 한글이 □ | TMP 폰트에 CJK 글리프 없음 (README 1단계) |
| 월드 입장하자마자 Overlay 가 다 켜져 있음 | `Overlay` 오브젝트가 활성 상태로 저장됨 → 체크 해제 |
| 버튼이 안 눌린다 | 버튼 Layer 가 `Default` 가 아님 |
| Tools 메뉴가 없다 | UdonSharp 컴파일 에러 (Console 확인) |

> README 는 "왜 이렇게 만들었나" 와 100개 이상 확장·최적화를 다루는 레퍼런스입니다.
> 위 4단계가 돌아간 다음에 필요할 때만 찾아보세요.
