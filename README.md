# UEAssist

UEAssist is a personal Visual Studio extension for a faster and less distracting Unreal Engine C++ editing experience.

Visual Studio의 Unreal Engine C++ 편집 환경을 빠르고 덜 방해받게 만들기 위한 개인용 확장 프로젝트입니다. Visual Studio IntelliSense를 완전히 대체하기보다, Unreal 코드에서 느리거나 부족한 부분을 자체 분석으로 보완하는 것을 목표로 합니다.

## Current features

- Unreal 프로젝트 자동 감지 (`.uproject`)
- 편집 중인 C++ 문서의 Unreal 매크로 즉시 인식
- `UCLASS`, `UPROPERTY`, `UFUNCTION`, `GENERATED_BODY` 및 주요 매크로 계열 강조
- Unreal/C++ 타입과 변수의 실시간 의미 색상 표시
- Unreal 프로젝트에서 IntelliSense 오진 밑줄 억제
- 빠른 심볼 탐색
  - `F12`: UEAssist 우선, 실패하면 Visual Studio 기본 정의로 이동
  - `Alt+G`: UEAssist 빠른 이동
- `UEAssist: Status`를 통한 프로젝트 감지 및 적용 상태 확인

## Project structure

```text
UEAssist.Core        Editor-independent parsing and symbol models
UEAssist.Indexing    C/C++ source scanning and symbol lookup
UEAssist.Extension   Visual Studio VSIX integration
UEAssist.Core.Tests  Parser and identifier tests
```

## Development requirements

- Visual Studio 2022
- .NET Framework 4.7.2
- .NET 8 SDK
- Visual Studio extension development workload
- Desktop development with C++ workload
- Visual Studio Tools for Unreal Engine

## Build and test

1. Open `UEAssist.sln` in Visual Studio 2022.
2. Set `UEAssist.Extension` as the startup project.
3. Build the solution.
4. Press `F5` to launch the Visual Studio Experimental Instance.
5. Open an Unreal project and check `Tools > UEAssist: Status`.

The generated VSIX is located at:

```text
UEAssist.Extension/bin/Debug/UEAssist.Extension.vsix
```

## Current limitations

UEAssist is an early personal development build. Its semantic analysis is currently based on lightweight Unreal/C++ parsing and is not a complete C++ compiler or language server.

Not yet complete:

- Full C++ semantic analysis
- Complete handling of Unreal generated code and `generated.h`
- UEAssist-owned completion lists
- Precise overload, template, and namespace resolution
- Find References and Rename
- Persistent project-wide symbol database
- Multiple-result navigation UI
- Distinguishing every valid compiler error from IntelliSense false positives

IntelliSense squiggle suppression hides editor diagnostics for detected Unreal projects; it does not fix Visual Studio's internal parse state. Compiler and build errors remain available through the normal build output.

## Direction

The planned architecture is a lightweight Unreal-aware semantic index shared by navigation, completion, references, rename, and editor colorization. Features will be added incrementally and verified against real Unreal projects.

## Status

Personal, experimental, and under active development. Distribution and licensing have not been decided.
