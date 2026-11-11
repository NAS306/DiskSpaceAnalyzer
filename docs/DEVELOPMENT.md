# DiskSpace — Everything 디스크 용량 분석기

Everything의 로컬 IPC 인덱스로 드라이브·폴더의 파일 용량을 집계하고, SpaceSniffer 형태의 중첩 트리맵으로 보여주는 Windows x64 네이티브 앱입니다. C# / WPF로 작성했으며 Electron, WebView, Node.js를 실행 환경에 요구하지 않습니다.

## 실행

1.0.1부터 Everything 서비스가 실제로 **실행 중이며 자동 시작**인지 확인합니다. 서비스 누락·중지 또는 관리자 실행 설정이 남아 있으면 한 번의 Windows 관리자 승인으로 복구하고 Everything을 백그라운드로 다시 시작합니다. 이후 분석 앱과 Everything 검색 프로세스는 일반 권한으로 동작합니다. 빈 데이터베이스의 응답은 연결 성공으로 인정하지 않으며 실제 파일이 검색되어야 준비가 완료됩니다. 앱을 직접 실행하는 경우에도 같은 검사를 수행합니다.

일반 배포는 **`DiskSpaceAnalyzer-Setup.exe`**를 실행합니다. 설치 마법사에서 한국어·영어를 선택하며, 기존 Everything을 자동 감지합니다. 없으면 WinGet 설치를 시도하고, 사용할 수 없거나 실패하면 포함된 공식 설치 프로그램을 사용합니다. Everything 서비스 설치에는 Windows 관리자 권한 확인이 필요할 수 있습니다. 최초 인덱스 준비와 실제 검색 연결을 확인한 뒤 설치가 완료됩니다. 앱을 제거해도 Everything과 사용자 데이터은 보존합니다.

포터블 배포는 `DiskSpaceAnalyzer-win-x64.zip` 전체를 풀고 `DiskSpaceAnalyzer.exe`를 실행합니다. EXE만 따로 복사하지 않습니다. 개발 중에는 `dist/DiskSpaceAnalyzer.exe`를 실행하고 드라이브 또는 **폴더 선택**을 누릅니다. .NET 10 런타임이 포함되어 별도 설치가 필요하지 않습니다. HTML 프로젝트가 아니므로 단일 HTML 규칙은 적용되지 않습니다.

지원 언어는 한국어·영어입니다. 한국어 Windows에서는 한국어, 그 외에는 영어로 시작합니다. 상단 **KO/EN** 메뉴에서 즉시 변경하며 설정은 저장됩니다. 사용자 파일명·경로·Everything 검색식은 그대로 유지합니다. 설치 및 번역 유지보수 방법은 [installer/README.md](../installer/README.md)를 참고하세요.

시작 창은 마우스가 있는 모니터의 **작업 표시줄을 제외한 사용 가능 영역** 안에 맞춰 가운데 배치합니다. 화면이 작거나 배율이 높으면 창 크기와 최소 크기를 해당 모니터에 맞게 줄여 제목 표시줄과 창 테두리가 화면 밖으로 나가지 않게 합니다. Windows가 창을 만든 직후와 최초 배치가 끝난 뒤에 확인하고, 모니터·배율·작업 영역 변경 및 전체화면 복귀 시에도 일반 창을 다시 확인합니다. 사용자가 지정한 크기·위치가 화면 안에 있으면 유지하며, 최대화와 테두리 없는 전체화면은 별도로 유지합니다.

창 배치는 물리 픽셀의 [모니터별 작업 영역](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo)과 [창이 있는 모니터의 DPI](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)를 조회해 계산합니다. 기존 1440×920 중앙 배치는 실제 1600×852 작업 영역에서 위쪽 좌표 -34px로 재현됐으며, 수정 후 1440×852 / 위쪽 0px로 확인했습니다. 전후 측정은 `artifacts/window-placement-before.json`, `artifacts/window-placement-after.json`에 있습니다. 여러 배율·음수 좌표·작업 표시줄 위치는 배치 계산 검사이며, 실제 창 생성·전체화면 복귀는 현재 모니터에서 검사합니다.

화면은 트리맵 중심으로 구성합니다. 타일 사이와 트리맵 외곽의 여백 및 둥근 모서리를 없애고, 16px 폴더 제목과 얇은 경계선으로 계층을 구분합니다. 타일에 마우스를 올리면 기다림 없이 이름과 용량 툴팁이 열리며, 다른 타일로 옮기면 즉시 갱신됩니다. 작은 타일도 툴팁으로 이름을 확인할 수 있습니다. 도구 모음·통계·파일 목록은 간격을 줄여 표시합니다. 파일 목록은 기본으로 접혀 있으며 **항목 목록 / F9**으로 열거나 다시 접습니다. 목록을 열었을 때는 경계선을 끌어 너비를 조절할 수 있습니다. 드라이브의 여유 공간은 해당 버튼에 마우스를 올리면 표시됩니다.

- Everything이 실행 중이면 기존 인덱스에 연결합니다. 실행 중이 아니면 설치된 Everything을 `-startup -first-instance`로 자동 시작합니다.
- 설치된 엔진이 없으면 함께 제공하는 `Everything/Everything.exe`를 자동 사용합니다. 서비스도 없는 PC에서는 NTFS 인덱싱 권한을 얻기 위해 Windows 관리자 승인 창이 한 번 나타날 수 있습니다. 검색창을 열거나 Everything을 직접 조작할 필요는 없습니다.
- 기본 배포에는 .NET 런타임을 포함합니다. 개발 도구 설치 없이 Windows x64에서 실행할 수 있습니다. 인터넷은 빌드 시 의존성 복원에만 사용하며, 앱 실행과 분석은 로컬에서 이루어집니다.
- Everything은 앱 종료 후에도 인덱스를 유지하도록 백그라운드에 남겨 둡니다. 서비스가 정상인 기존 인스턴스는 재사용합니다. 서비스 복구가 필요한 경우에만 Everything을 다시 시작하고 서비스 자동 시작 및 관리자 실행 해제 설정을 적용합니다.

## 조작

드라이브 최상위에서는 용량을 `사용 용량/전체 용량 (사용률%)`로 표시합니다(예: `399GB/475GB (84%)`). 사용률은 반올림 전 실제 바이트 기준으로 계산합니다. **여유 공간 보이기**는 기본 ON이며, 드라이브 전체 용량 중 실제 여유 공간의 비율만큼 오른쪽에 회색 영역을 표시합니다. 회색은 항상 유지되며, 파일 목록·개수·분석 합계에는 포함되지 않습니다. 폴더·검색 결과·이름 필터에서는 여유 공간 표시를 잠시 비활성화하고, 드라이브 전체 보기로 돌아오면 선택 상태를 복원합니다. 드라이브 용량과 파일 논리 용량의 차이는 용량 툴팁에 안내합니다. C:\Windows 제외 시에는 제외한 파일 용량을 사용·전체 용량에서 함께 빼고 실제 여유 공간은 유지합니다.


기능에 마우스를 잠시 올리면 하는 일·사용 방법·필요한 조건·단축키를 설명하는 툴팁이 표시됩니다. 메인 화면, 탐색기 메뉴, 우클릭 작업, 고급 검색, 파일 목록에 안내를 제공합니다. 비활성화된 버튼도 설명을 확인할 수 있으며, 긴 문장은 줄바꿈하여 표시합니다. 트리맵 항목의 이름·용량 툴팁은 기존처럼 즉시 표시되고, 클릭·우클릭 안내도 함께 나옵니다. 일반 기능 설명은 `src/Controls/FeatureHelp.cs`에서 관리하며, 기록 시각·전체 경로 같은 현재 값은 각 기능에서 덧붙입니다.

### Everything 검색 결과 시각화

검색창 옆 **고급 검색**은 검색식을 모르는 사용자를 위한 조건 작성 창입니다. 확장자(직접 입력 또는 ZIP·이미지·동영상 등의 묶음), 크기 비교(제한 없음·초과·이상·미만·이하·같음), 용량 단위(B~TB), 범위(전체 인덱스·드라이브·특정 폴더와 하위 폴더)를 선택하면 검색식 미리보기가 갱신됩니다. **검색하고 시각화**를 누르면 완성된 식이 상단에 적용되고 검색이 실행됩니다. 음수·잘못된 단위·검색 연산자를 섞은 확장자 등은 오류로 안내합니다. 고급 검색의 선택값은 현재 앱 창에서 기억하며, 기존 검색식을 직접 편집하는 기능도 사용할 수 있습니다.

상단 **Everything 검색식** 입력란에 `*.zip size:>500mb`를 입력하고 **검색 시각화 / Enter**를 누릅니다. **Ctrl+E**로 입력란에 이동합니다. 선택한 폴더와 관계없이 Everything 전체 인덱스에 검색식을 그대로 전달합니다. 특정 폴더로 제한하려면 `"D:\자료\" *.zip size:>500mb`처럼 경로를 검색식에 넣습니다. 와일드카드, `size:`, `ext:`, OR 그룹 등은 연결된 Everything 엔진이 지원하는 문법을 따릅니다.

검색 결과에 일치한 **파일 하나가 타일 하나**가 되며, 크기가 작은 결과는 기존 묶음 규칙을 사용합니다. 서로 다른 폴더·드라이브의 같은 파일명도 전체 경로로 구분합니다. 우클릭 메뉴는 원래 파일을 대상으로 동작합니다. 검색에 맞지 않는 파일까지 합산하지 않도록 **폴더 결과와 그 하위 전체 용량은 제외**하고, 제외한 폴더 수는 상태 표시줄에 알립니다. 0 B·용량 확인 불가 파일은 항목 목록에서 확인할 수 있습니다.

실시간 갱신이 켜져 있으면 검색 완료 후 **5초마다 동일 검색식을 다시 조회**합니다. **F5**는 현재 적용된 검색식으로 즉시 갱신합니다. 입력란에서 수정 중인 검색식은 Enter를 누르기 전까지 적용하지 않습니다. 결과가 같으면 트리맵을 다시 만들지 않습니다. 검색 취소·실패 시 기존 결과를 유지하며, 결과가 없으면 이전 타일을 지웁니다. 드라이브나 폴더를 선택하면 일반 폴더 분석으로 돌아갑니다. 기존 ‘현재 폴더에서 찾기’ 입력란은 검색 모드에서 결과 이름을 추가로 필터합니다.

### C:\Windows 제외 옵션

통계 줄의 **C:\Windows 제외** 또는 고급 검색 창의 동일 옵션을 켜면 `C:\Windows`와 모든 하위 폴더를 일반 분석·폴더 탐색·검색·자동 갱신에서 제외합니다. 파일/폴더 수와 논리적 용량 합계도 제외된 데이터 없이 계산합니다. Windows 폴더를 직접 분석하더라도 결과와 합계는 0이 됩니다. `C:\Windows.old`, `D:\Windows` 등 다른 경로는 그대로 포함합니다. 이 옵션은 Everything의 인덱스 설정이나 파일 자체를 변경하지 않습니다.

옵션을 바꾸면 이전 타일과 합계를 지우고 현재 범위를 재집계합니다. 해제하면 포함 상태로 다시 조회하며, 현재 앱 창에서 이후 분석·검색에도 같은 옵션을 적용합니다. 제외 옵션을 변경한 직후 조회를 취소하면 기존 포함 결과를 되살리지 않으며 F5로 다시 집계할 수 있습니다. 드라이브 버튼 툴팁의 실제 디스크 총 크기·여유 공간은 하드웨어 수치이므로 변하지 않습니다.

### 파일 탐색기에서 바로 분석

앱 상단의 **탐색기 메뉴 ▾ → 탐색기 메뉴에 추가**를 한 번 누릅니다. 이후 파일 탐색기에서 폴더 자체 또는 폴더 안 빈 공간을 우클릭해 **이 폴더의 디스크 사용량 분석**을 선택하면 해당 폴더를 루트로 새 앱 창에서 자동 분석합니다. Windows 11에서는 **더 많은 옵션 표시** 안에 나타날 수 있습니다. 여러 폴더 동시 선택은 지원하지 않습니다.

등록은 관리자 권한 없이 현재 사용자의 `HKCU\Software\Classes\Directory\shell\DiskSpaceAnalyzer.Analyze`와 `Directory\Background\shell\DiskSpaceAnalyzer.Analyze`에만 적용합니다. 앱을 둘 최종 폴더에서 등록하세요. 폴더를 이동했다면 이동한 앱에서 **현재 위치로 등록 갱신**을 누릅니다. 앱을 삭제하기 전에는 **탐색기 메뉴에서 제거**를 누르세요. 앱 실행만으로 자동 등록하지 않습니다.

명령행에서도 `DiskSpaceAnalyzer.exe --root "D:\자료"`로 시작할 수 있습니다. 공백·한글·드라이브 루트 경로를 처리하며 존재하지 않거나 접근할 수 없는 시작 폴더는 오류로 안내합니다. 분석할 폴더가 Everything 인덱스에 포함되어 있어야 한다는 기존 조건은 동일합니다.

| 동작 | 결과 |
|---|---|
| 트리맵 폴더 제목·바깥 영역 클릭 | 해당 폴더로 바로 이동 |
| 중첩 미리보기 안쪽 폴더 클릭 | 클릭한 하위 폴더로 바로 이동, 여러 단계도 한 번에 이동 |
| 폴더 내 파일·기타 미리보기 영역 클릭 | 그 영역을 포함하는 폴더로 이동 |
| 오른쪽 목록의 폴더 한 번 클릭 | 한 단계 이동 |
| 파일 클릭 | 세부 정보 선택, 파일 실행하지 않음 |
| 트리맵·오른쪽 목록의 폴더/파일 우클릭 | 파일 탐색기에 표시, 경로 복사, 파일/폴더 열기, Windows 속성 보기 |
| 작은 항목 묶음 클릭 | 소속 폴더의 전체 파일 목록 열기, 개별 항목은 목록에서 우클릭 가능 |
| 타일에 마우스 올리기 | 이름·용량 툴팁 즉시 표시, 다른 타일로 옮기면 즉시 갱신 |
| 상위 / Backspace | 한 단계 위로 이동, 선택한 분석 범위 밖으로 나가지 않음 |
| 이전 / Alt+왼쪽 | 탐색 기록의 이전 위치 |
| 경로 표시줄 | 선택한 상위 경로로 직접 이동 |
| 1·2·3단계 | 표시 깊이 변경, 표시된 각 폴더를 직접 클릭해 이동 가능 |
| 검색 입력 | 현재 폴더의 항목 이름 필터, 하위 전체 검색은 아님 |
| 새로고침 / F5 | 분석 범위 전체를 Everything 인덱스에서 다시 집계 |
| 취소 / Esc | 진행 중인 집계 취소 |
| 실시간 갱신 | 자동 갱신 활성화·일시정지 |
| 항목 목록 / F9 | 오른쪽 파일 목록 열기·접기, 기본은 접힌 상태 |
| 전체화면 / F11 | 제목 표시줄·창 테두리를 숨겨 화면 전체로 전환, 다시 누르면 이전 창 크기·위치·최대화 상태로 복귀 |
| 전체화면에서 Esc | 창 모드 복귀, 집계·검색 중에는 우선 해당 작업 취소 |

0 B 파일과 빈 폴더는 면적이 없어 트리맵에 나오지 않지만 오른쪽 목록에서 탐색할 수 있습니다. 화면상 면적이 4×4 물리 픽셀보다 작은 항목은 ‘작은 항목 N개’로 합쳐 개수와 용량을 표시합니다. 화면 크기·DPI에 따라 묶음을 다시 계산하며, 단계별 최대 1,024개 타일 및 전체 미리보기 확장 예산을 적용합니다. 묶음은 파일 시스템의 실제 항목이 아니므로 우클릭 작업을 제공하지 않습니다. 클릭하면 소속 폴더의 전체 파일 목록을 열어 원래 항목을 확인할 수 있습니다. 모든 항목의 용량과 개수는 집계에 포함합니다.

## 구조와 성능 설계

| 파일 | 역할 |
|---|---|
| `src/App.xaml`, `App.xaml.cs` | 색상·공통 스타일, 시작점, 검증 모드 |
| `src/Controls/FeatureHelp.cs` | 한국어·영어 리소스를 사용하는 기능별 도움말·사용 조건·단축키 |
| `src/MainWindow.xaml`, `MainWindow.xaml.cs` | 드라이브·폴더 선택, 탐색, 상태, 자동 갱신 연결 |
| `src/MainWindow.Window.cs` | 시작 창 배치 연결·전체화면 전환 및 복귀 |
| `src/Interop/WindowPlacement.cs` | 모니터별 작업 영역·DPI 조회, 최초 표시·환경 변경 시 창 배치 보정 |
| `src/Core/WindowBounds.cs` | 물리 픽셀 좌표에서 창 크기·위치를 작업 영역 안으로 제한 |
| `src/MainWindow.Search.cs` | 검색 입력·파일별 트리맵 모드·동일 검색식 재조회 연결 |
| `src/MainWindow.SearchOptions.cs` | 고급 검색 창 연결·제외 옵션 변경·현재 범위 재집계 |
| `src/Controls/AdvancedSearchWindow.xaml`, `.xaml.cs` | 조건 선택·미리보기·폴더 선택·검색 실행 대화상자 |
| `src/Core/SearchQueryBuilder.cs` | 확장자·용량·범위를 검증하여 Everything 검색식 생성 |
| `src/Core/AnalysisFilter.cs` | C:\Windows 경계 판별, 일반/검색/직계/증분 조회 공통 제외 규칙 |
| `src/MainWindow.Window.cs` | 전체화면 전환 및 이전 창 상태 복원 |
| `src/Core/SearchResults.cs` | 검색 파일 결과·용량 합계·변경 비교 |
| `src/Interop/EverythingNative.cs` | 공식 Everything64.dll P/Invoke 선언 |
| `src/Core/EverythingClient.cs` | 전용 STA·숨김 IPC 창, 비동기 조회, 취소, 일괄·직계 파일 조회 |
| `src/Core/DirectoryIndex.cs` | 폴더별 집계, 하위 트리 교체, 증분 갱신, 용량 표시 |
| `src/Core/LiveMonitor.cs` | 파일 시스템 변경 알림, 이벤트 병합, 버퍼 오류 감지 |
| `src/Core/TreemapLayout.cs` | 순수 squarified treemap 면적 계산 |
| `src/Core/TreemapGrouping.cs` | 픽셀 면적·렌더링 예산에 따른 작은 항목 묶음, 원래 합계 보존 |
| `src/Controls/TreemapView.cs` | WPF 그리기 명령 기반 트리맵, 중첩 영역에서 가장 안쪽 폴더의 클릭·강조 처리 |
| `src/Controls/EntryContextMenu.cs` | 우클릭한 경로를 유지하는 탐색기·복사·열기·속성 메뉴 |
| `src/Interop/WindowsShell.cs` | Windows Shell 속성 대화 상자 호출 |
| `src/Interop/ExplorerIntegration.cs` | 사용자별 탐색기 정적 메뉴 등록·제거·경로 갱신 |
| `src/Core/StartupOptions.cs` | `--root` 시작 경로 해석 및 유효성 확인 |
| `src/Core/Verification.cs` | 실제 IPC 집계 대조, 성능 측정, 격리된 실시간 변경 테스트 |
| `tests/Program.cs` | 집계·경로·증분 갱신·면적·182만 항목 합성 검증 |
| `src/Core/Localization.cs`, `Controls/TrExtension.cs`, `Resources/Strings*.resx` | 언어 선택·저장 및 한국어·영어 번역 |
| `build-installer.ps1`, `installer/DiskSpaceAnalyzer.iss` | Everything 의존성 설치·검증, 한국어·영어 설치 마법사 |
| `build.ps1` | 배포 빌드 및 변경 전 `_BU` 백업 |
| `vendor/` | 공식 Everything 엔진·SDK·라이선스 |

첫 집계는 Everything에서 범위 전체의 이름·경로·크기를 한 번의 일괄 조회로 가져옵니다. 페이지 사이의 인덱스 변경에 따른 중복·누락을 피합니다. 파일별 객체를 영구 저장하지 않고 폴더별 직계 용량·파일 수와 하위 폴더만 유지합니다. 파일 경로 조회는 .NET 10의 span 기반 딕셔너리 조회를 사용해 반복 문자열 할당을 줄입니다. IPC 결과 버퍼는 처리가 끝나면 해제합니다.

현재 폴더에 들어갈 때만 그 폴더의 직계 파일 목록을 추가 조회합니다. 목록은 가상화하며, 트리맵은 파일별 UI 컨트롤을 만들지 않고 DrawingContext로 그립니다. 계산·정렬·그리기 결과는 캐시해 마우스 이동 때 재사용하고, 데이터·표시 깊이·창 크기·DPI가 바뀔 때 갱신합니다. SDK 상태는 하나의 전용 스레드에서 직렬화하여 조회 간 섞임을 방지합니다.

실시간 갱신은 파일 시스템 알림을 **변경 위치를 찾는 용도**로만 사용합니다. 용량과 목록은 항상 Everything 인덱스에서 읽습니다. 변경 이벤트가 잠잠해진 뒤 약 2.5초 후 직계 항목을 다시 읽고 조상 용량을 갱신합니다. 이벤트가 계속 발생하면 약 5초 간격으로 묶어 처리합니다. 새 폴더는 해당 하위 트리를 조회합니다. 감시 실패·이벤트 버퍼 넘침 또는 60초 주기에는 범위 전체를 재동기화합니다. 전체 재동기화 중에도 기존 화면은 유지됩니다.

## 빌드

개발 환경: Windows x64, .NET SDK 10.x. `build.ps1`은 `tools/dotnet/dotnet.exe`가 있으면 우선 사용합니다. 외부 NuGet 패키지 의존성은 없습니다. 런타임 포함 게시 시 Microsoft 런타임 패키지 복원에 인터넷 연결이 필요할 수 있습니다.

```powershell
# 기본: 런타임 포함 배포
powershell -ExecutionPolicy Bypass -File .\build.ps1

# .NET 10 Desktop Runtime이 설치된 PC용 작은 배포
powershell -ExecutionPolicy Bypass -File .\build.ps1 -FrameworkDependent

# 개발 빌드 / 실행
dotnet build .\src\DiskSpaceAnalyzer.csproj -c Release
dotnet run --project .\src\DiskSpaceAnalyzer.csproj
```

소스 수정 전에는 같은 디렉터리에 `파일명_BU.확장자`를 만들고 기존 BU를 덮어씁니다. BU C#·XAML 파일은 컴파일에서 제외합니다. `build.ps1`은 먼저 `artifacts`에 게시하고 게시 성공 후 `dist`를 갱신합니다. 기존 배포 파일도 `_BU`로 보존하며, 배포 ZIP에는 BU를 넣지 않습니다. 런타임 의존형 옵션은 별도 `dist-framework`와 `DiskSpaceAnalyzer-framework-win-x64.zip`으로 출력합니다.

## 검증

```powershell
dotnet run --project .\tests\CoreTests.csproj -c Release

# WPF 트리맵 회귀 검증 + 시작 경로·탐색기 등록/갱신/제거 검증 (임시 HKCU 키 사용 후 정리)
dotnet run --project .\tests\TreemapInteraction\TreemapInteractionTests.csproj -c Release

# 실제 Everything 검색식·확장자·크기·OR·경로·취소·검색 후 일반 스캔 검증
.\dist\DiskSpaceAnalyzer.exe --verify --search-test --output ".\artifacts\search-test.json"

# 실제 HWND 시작 좌표·전체화면 복귀 및 모니터/DPI별 배치 계산 검증
dotnet run --project tests/TreemapInteraction/WindowPlacement/WindowPlacementTests.csproj -c Release -- ".\artifacts\window-placement-after.json"

# 읽기 전용: 실제 C 드라이브·검색·직계/증분 조회·용량 합계의 Windows 제외 검증
.\dist\DiskSpaceAnalyzer.exe --verify --filter-test --output ".\artifacts\windows-filter-test.json"

# 읽기 전용: Windows를 제외한 일반 분석 또는 검색 결과
.\dist\DiskSpaceAnalyzer.exe --verify --path C:\ --exclude-windows --output ".\artifacts\filtered-drive.json"

# 읽기 전용: 임의 검색식의 파일 결과와 합계를 JSON으로 확인
.\dist\DiskSpaceAnalyzer.exe --verify --search "*.zip size:>500mb" --output ".\artifacts\search-result.json"

# 읽기 전용: 작은 폴더의 실제 파일 수·용량과 Everything 결과 대조
.\dist\DiskSpaceAnalyzer.exe --verify --path "C:\some\folder" --compare --output ".\artifacts\verify.json"

# 읽기 전용: 드라이브 전체 IPC 집계 성능 측정
.\dist\DiskSpaceAnalyzer.exe --benchmark --path C:\ --output ".\artifacts\benchmark.json"

# 지정한 출력 디렉터리 안에 격리된 테스트 폴더를 만들어 실시간 변경 검증
.\dist\DiskSpaceAnalyzer.exe --verify --path ".\src\Core" --watch-test --output ".\artifacts\live.json"
```

GUI 앱이므로 명령 실행이 즉시 반환될 수 있습니다. 프로세스 종료 후 JSON을 확인합니다. 출력 디렉터리는 먼저 만들어 주세요. `--compare`는 실제 파일 시스템을 순회하므로 접근 가능한 작은 테스트 폴더에서만 사용합니다. `--watch-test`는 `live-fixture-<GUID>` 아래에 생성·크기 변경·삭제·폴더 이름 변경·한글·빈 폴더·0 B 파일을 테스트하며 테스트 폴더를 남깁니다. 스캔 취소도 검증합니다.

2026-11-07 이 PC에서 측정한 결과:

| 검증 | 결과 |
|---|---|
| 실제 C 드라이브 | 파일 1,469,578 + 폴더 354,757 = **1,824,335개 항목** |
| 전체 Everything 조회 + 폴더 집계 | **6.71초**, 기존 인덱스 준비 상태 |
| 측정 직후 프로세스 작업 집합 | 약 **318 MB** (GUI 미포함 검증 프로세스) |
| 실제 폴더의 파일 수·용량·직계 파일 목록 대조 | 일치 |
| 실시간 생성·크기 변경·삭제·폴더 이름 변경 | **2.68초** 후 일치 |
| 182만 항목 합성 집계 | 약 **0.46초**, IPC·최초 인덱싱·UI 미포함 |
| 무작위 트리맵 100종 | 면적 보존·용량 비례·경계·겹침 검사 통과 |

측정 결과는 `artifacts/drive-benchmark-v2.json`, `artifacts/integration-verification.json`, `artifacts/core-tests.json`에 있습니다. 항목 수는 파일 생성·삭제에 따라 달라집니다. **Everything의 비어 있는 데이터베이스를 처음 만드는 시간은 아직 측정하지 않았습니다.** 앱은 인덱스 준비를 최대 3분 기다린 뒤 실패 원인을 안내합니다. 3분 이내 최초 인덱싱을 모든 PC에 보장하는 수치는 아닙니다.

## 용량과 지원 범위

- 표시값은 파일의 **논리적 크기 합계**이며 실제 할당 공간이 아닙니다. 하드 링크, 압축, 희소 파일, 파일 시스템 메타데이터 등에 따라 Windows의 사용 중 공간 또는 WizTree의 할당 크기와 다릅니다. 하드 링크의 물리 저장 공간 중복 제거는 구현하지 않았습니다.
- 파일 내용은 읽거나 변경하지 않습니다. 파일 삭제·정리 기능은 없습니다.
- Everything에 실제로 인덱싱된 경로만 분석합니다. 제외 경로, 아직 인덱싱되지 않은 드라이브·네트워크·비 NTFS 폴더는 결과가 없을 수 있습니다. 이 경우 빈 폴더와 미인덱싱 상태를 구분할 수 없어 안내 문구를 표시합니다. 네트워크/FAT 폴더를 자동 등록하는 기능은 없습니다.
- 확인할 수 없는 파일 크기는 0 B로 단정하지 않고 별도 개수 및 파일의 ‘확인 불가’ 표시로 안내합니다.
- 실시간 인덱스 전파·이벤트 감시 권한·디스크 상태에 따라 반영 시간이 달라집니다. 수동 새로고침과 60초 전체 대조로 보완합니다. 전체 집계 사이에 변경된 파일이 있으면 직계 파일 목록과 상위 집계가 다음 동기화까지 잠시 다를 수 있습니다.

## 공식 자료·라이선스

- [Everything SDK / IPC](https://www.voidtools.com/support/everything/sdk/)
- [Everything 백그라운드 시작 옵션](https://www.voidtools.com/support/everything/command_line_options/)
- [Everything 다운로드](https://www.voidtools.com/downloads/)
- [Everything 라이선스](https://www.voidtools.com/License.txt), 배포 사본: `vendor/Everything/License.txt`, `dist/Everything/License.txt`
- [Windows Shell 속성 표시 API](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfow): Windows 자체 파일·폴더 속성 대화 상자를 사용합니다.
- [Windows 탐색기 정적 메뉴 등록](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers), [등록 변경 알림](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify): 현재 사용자에게만 폴더·폴더 배경 메뉴를 등록합니다.

Everything과 SDK의 저작권은 David Carpenter / voidtools에 있습니다. 함께 제공한 원본 라이선스를 배포에 포함합니다.
