# DiskSpaceAnalyzer

Everything의 실시간 인덱스와 SpaceSniffer 스타일의 밀도 높은 트리맵을 결합한 Windows 디스크 용량 분석기입니다. 큰 파일과 폴더를 빠르게 찾고, Everything 검색 결과도 시각화할 수 있습니다.

Windows disk space analyzer combining Everything's real-time index with a dense, SpaceSniffer-style treemap. Quickly find large files and folders, or visualize Everything search results.

## 다운로드 / Downloads

**Windows 10 1809 이상 / Windows 11 · x64 · 한국어 / English**

| 배포 / Package | 다운로드 / Download | 사용 방법 / Instructions |
|---|---|---|
| 설치 마법사 / Installer | [DiskSpaceAnalyzer-Setup.exe](https://github.com/NAS306/DiskSpaceAnalyzer/releases/latest/download/DiskSpaceAnalyzer-Setup.exe) | 실행 후 안내를 따르세요. / Run and follow the setup wizard. |
| 포터블 / Portable | [DiskSpaceAnalyzer-win-x64.zip](https://github.com/NAS306/DiskSpaceAnalyzer/releases/latest/download/DiskSpaceAnalyzer-win-x64.zip) | 전체 압축을 풀고 `DiskSpaceAnalyzer.exe`를 실행하세요. / Extract all files, then run `DiskSpaceAnalyzer.exe`. |

[전체 릴리스 / All releases](https://github.com/NAS306/DiskSpaceAnalyzer/releases)

설치 마법사는 Everything을 자동 감지하며, 없으면 WinGet 또는 포함된 공식 설치 프로그램으로 설치합니다. .NET 런타임이 포함되어 별도 설치가 필요하지 않습니다. Everything 서비스 준비에 관리자 승인이 필요할 수 있으며, 최초 인덱싱은 최대 3분 정도 걸릴 수 있습니다. 포터블도 Everything 인덱싱 엔진을 사용하며 설정과 서비스가 PC에 남을 수 있습니다.

The installer detects Everything and installs it through WinGet or the bundled official installer when needed. The .NET runtime is included. Preparing the Everything service may require administrator approval, and initial indexing can take up to about three minutes. The portable package also uses the Everything indexing engine and can leave settings and a service on the PC.

## 주요 기능 / Features

- 밀도 높은 중첩 트리맵, 하위 폴더 바로 탐색, 즉시 표시되는 파일·폴더 툴팁 / Dense nested treemap, direct folder navigation, instant item tooltips.
- Everything 검색식 및 고급 검색 도우미: 예를 들어 `*.zip size:>500mb` / Everything queries and an advanced search builder, such as `*.zip size:>500mb`.
- 실시간 갱신, 작은 항목 묶음, `C:\Windows` 제외 옵션 / Live updates, small-item grouping, optional `C:\Windows` exclusion.
- 사용 용량·전체 용량·사용률과 회색 여유 공간 표시 / Used and total capacity, usage percentage, and gray free-space display.
- 파일 탐색기 연동, 우클릭으로 열기·경로 복사·속성 보기 / Explorer integration and context actions for opening, copying paths, and viewing properties.
- 한국어·영어 전환 및 전체화면 / Korean and English UI, fullscreen mode.

분석할 드라이브·폴더는 Everything 인덱스에 포함되어 있어야 합니다. 용량은 파일의 논리적 크기를 기준으로 하므로 실제 디스크 사용량과 차이가 날 수 있습니다.

The selected drive or folder must be indexed by Everything. File sizes are logical sizes and may differ from actual disk usage.

## 개발 / Development

C# / WPF, .NET 10. [구조·빌드·검증 문서 / Architecture, build, and verification](docs/DEVELOPMENT.md) · [설치 마법사 빌드 / Installer build](installer/README.md)

Everything과 SDK는 [voidtools](https://www.voidtools.com/)에서 제공합니다. 포함된 제3자 소프트웨어의 라이선스는 [`vendor/Everything/License.txt`](vendor/Everything/License.txt)와 [`vendor/SDK/License.txt`](vendor/SDK/License.txt)를 참고하세요.

Everything and its SDK are provided by voidtools. Third-party license notices are included in the files linked above.
