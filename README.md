# UEAssist

UEAssist는 Visual Studio에서 Unreal Engine C++ 코드를 더 빠르고 편하게 편집하기 위한 개인용 확장 프로젝트입니다.

Visual Studio IntelliSense를 완전히 대체하기보다 Unreal 코드에서 느리거나 부족한 부분을 자체 분석으로 먼저 처리하고, UEAssist가 처리하지 못한 작업은 기존 Visual Studio 기능으로 넘기는 것을 목표로 합니다.

## 현재 기능

### Unreal 프로젝트 자동 감지

- 현재 문서와 솔루션 경로를 기준으로 `.uproject` 탐색
- `Source` 하위에서 파일을 열어도 상위 폴더를 검색하여 프로젝트 감지
- `.sln` 및 `.uproject` 직접 열기 방식 대응
- 문서를 열 때마다 감지 상태 갱신

### Unreal 매크로 실시간 인식

Visual Studio IntelliSense의 분석 완료를 기다리지 않고 편집 중인 문서를 즉시 분석합니다.

현재 인식하는 주요 매크로:

```text
UCLASS
USTRUCT
UENUM
UINTERFACE
UPROPERTY
UFUNCTION
UPARAM
UMETA
GENERATED_BODY 계열
DECLARE_*
DEFINE_*
IMPLEMENT_*
```

### C++ 의미 색상

- Unreal/C++ 클래스와 타입 표시
- 선언된 멤버 및 지역 변수와 사용처 표시
- 문서가 변경되면 즉시 다시 분석
- 동일한 문서 버전의 분석 결과 캐시

UEAssist는 색상을 직접 지정하지 않고 Visual Studio의 기존 C++ 색상 설정을 상속합니다.

```text
Unreal 매크로  → C++ Macros
클래스와 타입  → C++ User Types
변수           → C++ Variables
```

Visual Studio 테마를 바꾸거나 `도구 → 옵션 → 환경 → 글꼴 및 색`에서 해당 항목을 변경하면 UEAssist도 같은 색상을 사용합니다.

### IntelliSense 오진 밑줄 억제

Unreal 프로젝트가 감지되면 C++ IntelliSense가 편집기에 표시하는 잘못된 빨간 밑줄을 숨깁니다.

- 실제 컴파일과 빌드 오류는 그대로 유지
- Unreal 프로젝트를 닫으면 기존 설정 복원
- `UEAssist: Status`에서 적용 여부와 감지 경로 확인 가능

이 기능은 Visual Studio 내부의 잘못된 분석 상태를 수정하는 것이 아니라 편집기의 IntelliSense 진단 밑줄을 억제하는 기능입니다.

### 빠른 심볼 탐색

```text
F12
 ├─ UEAssist가 선언을 찾음   → 즉시 이동
 └─ UEAssist가 찾지 못함     → Visual Studio 기본 정의로 이동 실행
```

- `F12`: UEAssist 우선, 실패하면 Visual Studio 기본 기능 사용
- `Alt+G`: UEAssist 빠른 심볼 이동
- C++ 이외의 문서에서는 즉시 Visual Studio 기본 기능으로 전달
- 클래스, 구조체, 열거형, 함수 및 변수의 기본 선언 검색
- 헤더의 선언을 우선 선택

검색에서 제외하는 폴더:

```text
.git
.vs
Binaries
DerivedDataCache
Intermediate
Saved
```

### 상태 확인

Visual Studio의 `도구 → UEAssist: Status`에서 다음 내용을 확인할 수 있습니다.

- 감지한 `.uproject` 경로
- IntelliSense 빨간 밑줄 숨김 적용 여부
- 프로젝트 감지 또는 설정 적용 실패 원인

## 프로젝트 구성

```text
UEAssist.Core        편집기와 독립적인 파서 및 심볼 모델
UEAssist.Indexing    C/C++ 소스 검색과 심볼 탐색
UEAssist.Extension   Visual Studio VSIX 연동
UEAssist.Core.Tests  파서와 식별자 자동 테스트
```

## 개발 환경

- Visual Studio 2022
- .NET Framework 4.7.2
- .NET 8 SDK
- Visual Studio 확장 개발 워크로드
- C++를 사용한 데스크톱 개발 워크로드
- Visual Studio Tools for Unreal Engine

## 빌드 및 테스트

1. Visual Studio 2022에서 `UEAssist.sln`을 엽니다.
2. `UEAssist.Extension`을 시작 프로젝트로 설정합니다.
3. 솔루션을 빌드합니다.
4. `F5`를 눌러 실험용 Visual Studio를 실행합니다.
5. Unreal 프로젝트를 열고 `도구 → UEAssist: Status`를 확인합니다.

빌드가 완료되면 설치용 VSIX가 프로젝트 루트에 자동으로 복사됩니다.

```text
UEAssist.vsix
```

원본 빌드 결과는 `UEAssist.Extension/bin/<Configuration>/UEAssist.Extension.vsix`에 유지됩니다.

일반 Visual Studio에서 사용하려면 Visual Studio를 모두 종료한 뒤 VSIX 파일을 실행하여 설치합니다.

이미 같은 버전이 설치되어 있으면 VSIX 설치 관리자가 중복 설치를 허용하지 않습니다. 새 설치본을 배포할 때는 `source.extension.vsixmanifest`의 버전을 올려야 하며, 개발 중 빠른 반복 테스트에는 실험용 Visual Studio(`F5`)를 사용합니다.

## 현재 제한 사항

UEAssist는 현재 개인용 초기 개발 버전입니다. 의미 분석은 가벼운 Unreal/C++ 파서를 기반으로 하며 완전한 C++ 컴파일러나 언어 서버가 아닙니다.

아직 완성되지 않은 기능:

- 완전한 C++ 의미 분석
- Unreal 생성 코드 및 `generated.h` 해석
- UEAssist 자체 자동완성 목록
- 정확한 오버로드, 템플릿 및 네임스페이스 해석
- 멤버 타입과 전체 상속 관계 분석
- Find References 및 Rename
- 디스크에 저장되는 프로젝트 전체 심볼 인덱스
- 여러 검색 결과를 선택하는 탐색 화면
- 정상 오류와 모든 IntelliSense 오진을 구분하는 자체 진단

## 개발 방향

탐색, 자동완성, 참조 검색, 이름 변경 및 의미 색상이 함께 사용하는 Unreal 전용 경량 의미 인덱스를 구축하는 것이 목표입니다. 실제 Unreal 프로젝트에서 검증하면서 기능을 단계적으로 추가합니다.

## 프로젝트 상태

개인용 실험 프로젝트로 개발 중입니다. 공개 배포, 판매 및 라이선스 정책은 아직 결정하지 않았습니다.
