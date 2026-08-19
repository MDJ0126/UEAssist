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
- 초기 IntelliSense 오진 밑줄 억제
- 빠른 정의 탐색
  - `F12`: UEAssist 우선, 실패하면 Visual Studio 기본 기능 실행
  - `Alt+G`: UEAssist 빠른 탐색
- `도구 → UEAssist: Status`에서 감지 및 적용 상태 확인

## 작동 원리

Visual Studio IntelliSense는 Unreal의 대규모 헤더와 생성 코드를 분석하는 동안 초기 로딩이 느릴 수 있습니다. UEAssist는 편집 중인 문서를 먼저 가볍게 분석하여 매크로, 타입, 변수와 탐색 정보를 즉시 제공합니다.

```text
문서 열기/수정
 ├─ UEAssist가 즉시 분석 및 표시
 └─ Visual Studio IntelliSense는 백그라운드에서 계속 로드

F12
 ├─ UEAssist가 선언 발견 → 즉시 이동
 └─ 찾지 못함 → Visual Studio 기본 Go To Definition 실행
```

UEAssist는 아직 IntelliSense 전체를 대체하지 않습니다. 자동완성, 완전한 C++ 타입 추론, 템플릿 분석과 정밀 진단은 Visual Studio IntelliSense가 담당합니다.

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
