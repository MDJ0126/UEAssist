# UEAssist for Visual Studio

**Visual Studio용 Unreal Engine C++ 생산성 확장입니다.** IntelliSense가 준비되는 동안 Unreal 매크로와 의미 색상을 먼저 표시하고, 빠른 정의 탐색을 제공합니다.

[![UEAssist 다운로드](https://img.shields.io/badge/UEAssist-VSIX%20다운로드-7B68EE?style=for-the-badge&logo=visualstudio)](https://github.com/MDJ0126/UEAssist/raw/refs/heads/main/UEAssist.vsix)

## 설치

1. 위 버튼으로 `UEAssist.vsix`를 다운로드합니다.
2. Visual Studio를 모두 종료합니다.
3. VSIX를 실행하고 Visual Studio 2022에 설치합니다.

## 주요 기능

- Unreal 프로젝트 자동 감지
- `UCLASS`, `UPROPERTY`, `GENERATED_BODY`, `<모듈명>_API` 등 Unreal 매크로 즉시 표시
- 클래스·타입·변수·함수 및 `Super` 의미 색상 즉시 표시
- Visual Studio C++ 사용자 색상 설정과 자동 동기화
- Visual Studio IntelliSense 진단 밑줄 유지
- 인덱스 심볼과 대소문자가 다른 명확한 오타는 UEAssist가 즉시 표시
- 사용자 프로젝트와 사용 중인 Unreal 모듈의 공개 API 백그라운드 인덱싱
- 디스크 캐시 기반 자동완성
  - 문자 입력, `.`, `->`에서 자동 표시
  - 사용자 클래스의 멤버와 상속 멤버 후보 제공
  - IntelliSense가 아직 결과를 제공하지 못할 때만 UEAssist 미리보기 표시
  - IntelliSense가 준비되면 기본 IntelliSense 목록만 사용
  - Visual Studio 기본 타입·메서드·필드 아이콘과 부분·퍼지 이름 검색 제공
  - 입력한 문자와 일치하는 후보 부분을 굵게 표시
  - 미리보기 항목 오른쪽에 `UEAssist` 출처 표시
  - 숫자 리터럴과 초기화 구문에서는 불필요한 미리보기를 자동으로 닫음
- 빠른 정의 탐색
  - `F12`: UEAssist 우선, 실패하면 Visual Studio 기본 기능 실행
  - `Alt+G`: UEAssist 빠른 탐색
- 빠른 참조 후보 검색
  - `Shift+F12`: UEAssist 우선, 실패하면 Visual Studio 기본 참조 찾기 실행
  - 결과는 `UEAssist References` 출력 창에 파일·줄 번호로 표시
- `도구 → UEAssist: Status`에서 감지 및 적용 상태 확인

## 작동 원리

Visual Studio IntelliSense는 Unreal의 대규모 헤더와 생성 코드를 분석하는 동안 초기 로딩이 느릴 수 있습니다. UEAssist는 편집 중인 문서를 먼저 가볍게 분석하여 매크로, 타입, 변수와 탐색 정보를 즉시 제공합니다.

```text
문서 열기/수정
 ├─ UEAssist가 즉시 분석 및 표시
 ├─ 로컬 심볼 캐시에서 자동완성·탐색 후보 제공
 └─ Visual Studio IntelliSense는 백그라운드에서 계속 로드

F12
 ├─ UEAssist가 선언 발견 → 즉시 이동
 └─ 찾지 못함 → Visual Studio 기본 Go To Definition 실행
```

최초 실행 시 프로젝트 `Source`와 `.Build.cs`에서 확인된 Unreal Runtime 모듈의 `Public/Classes` 헤더를 백그라운드에서 분석합니다. 인덱스는 `%LOCALAPPDATA%\UEAssist\Indexes`에 저장되며 소스가 변경되지 않았으면 다음 실행부터 즉시 재사용합니다.

UEAssist는 IntelliSense 전체를 대체하지 않습니다. 자동완성·참조 검색은 빠른 후보를 먼저 제공하며, 완전한 C++ 타입 추론, 템플릿·조건부 컴파일 분석과 정밀 진단은 Visual Studio IntelliSense가 담당합니다.

## 개발

요구 사항:

- Visual Studio 2022
- Visual Studio 확장 개발 워크로드
- C++를 사용한 데스크톱 개발 워크로드
- .NET 8 SDK

`UEAssist.sln`을 열고 `UEAssist.Extension`을 시작 프로젝트로 설정한 뒤 `F5`를 누르면 실험용 Visual Studio에서 테스트할 수 있습니다. Release 빌드 시 루트의 `UEAssist.vsix`가 갱신됩니다.

## 라이선스

[MIT License](./LICENSE)

UEAssist는 독립 프로젝트이며 Microsoft 또는 Epic Games와 제휴하거나 공식 승인을 받은 제품이 아닙니다.
