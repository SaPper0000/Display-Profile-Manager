# Tarkov Gamma Manager v1.5.2

<img width="1086" height="753" alt="image" src="https://github.com/user-attachments/assets/ef31e21c-1acc-4ff2-b1cc-3b4837f48914" />


## ⚠️ 관리자 권한 실행

게임을 실행한 상태에서 글로벌 핫키를 사용하려면 프로그램을 관리자 권한으로 실행해야 합니다.

Escape from Tarkov 할 때 감마 / 밝기 / 대비 / 채도 같은 화면 설정을 프로필로 묶어서 편하게 바꾸는 Windows용 디스플레이 제어 유틸리티입니다.

글로벌 핫키(Apply / Toggle / Cycle)로 원하는 프로필을 바로 적용하거나 순환 적용할 수 있으며, 다중 모니터 환경에서도 모니터별 설정과 프로필을 독립적으로 관리할 수 있습니다.

> **⚠️ 이 프로그램은 Battlestate Games 또는 Escape from Tarkov의 공식 프로그램이 아닙니다.**

## ✨ v1.5.2 주요 변경사항

v1.5.2는 사용자 피드백으로 제보된 슬라이더 미반영 및 프로필 저장 시 수치 초기화 버그를 완벽히 해결하고, DDC/CI 하드웨어 통신 안정화, 비정상 종료 시 자동 복구, 비동기 세대(Generation) 제어 및 UI 렌더링 최적화를 적용한 안정화 릴리즈입니다.

### 🐛 프로필 저장 및 콤보박스 선택 로직 개선 (버그 해결)

**프로필 이름 클릭/입력 시 슬라이더 값 덮어쓰기 방지:** 드롭다운 콤보박스의 이벤트 트리거 방식을 SelectionChangeCommitted로 변경하고 텍스트 일치 검증을 추가하여, 새 프로필 이름을 입력하거나 텍스트를 클릭할 때 열심히 설정해 둔 슬라이더 값이 이전 프로필 값으로 원복되는 문제를 완벽히 차단했습니다.

**프로필 저장 시 덮어쓰기 분기 개선:** 기존 프로필 덮어쓰기 확인 창에서 [아니오]를 눌렀을 때 사용자가 조작 중이던 메인 슬라이더 수치를 그대로 보존합니다.

### 🛡️ DDC/CI 하드웨어 통신 씹힘 개선 및 병렬 락(Lock) 안정화

**하드웨어 디바운싱 지연 시간 최적화 (130ms):** 모니터 칩셋(I2C 버스) 응답이 느린 환경에서 명령이 누락되거나 폐기(Cancel)되는 현상을 방지하기 위해 하드웨어 밝기 및 대비 전송 지연 시간을 130ms로 균일하게 조정했습니다.

**DDC/CI 스레드 락 및 통신 동기화:** DisplayService 내부의 물리 제어 진입부에 동기화 락(_physicalApplyLock)을 적용하여 슬라이더 연속 드래그 시 스레드 경합을 방지했습니다.

**모니터 펌웨어 오차값 안전 보정:** DDC/CI 밝기/대비 검증 시 모니터 펌웨어가 최소/최대 범위를 벗어난 값을 반환해 읽기/쓰기 실패로 처리되던 문제를 보정했습니다.

### 🔄 비정상 종료 시 원본 화면 자동 복구 (Crash Recovery)

- 비정상 종료 감지 및 자동 롤백: 게임 도중 튕김이나 PC 강제 종료 등으로 프로그램이 정상 종료되지 않았을 때, 재실행 시 GammaManager.StartupBackup.json 백업을 감지하여 훼손된 모니터 색감/밝기를 원래 상태로 자동 복원 후 새 기준점을 잡습니다.

### ⚡ 16비트 GPU 감마 램프 정밀 연산 공식 적용

**Gamma.cs의 연산 공식을** 16비트 룩업 테이블(LUT) 표준에 맞춰 정규화하여 감마 거듭제곱 및 대비 연산 시 계단 현상 및 색 왜곡을 최소화했습니다.

### 💬 OSD 렌더링 최적화 및 GDI 리소스 누수 차단

**GDI+ 폰트 캐싱 (_cachedFont):** OSD 팝업 알림 시 매 프레임마다 폰트 객체를 생성/해제하던 방식을 캐싱 구조로 개선하여 GDI 핸들 누수 및 프레임 드랍을 차단했습니다.

**CancellationToken 기반 타이머 제어:** 연속 핫키 입력 시 이전 페이드아웃 타이머를 즉각 취소하여 팝업 깜빡임을 해결했습니다.

### 💾 백업 파일명 타임스탬프 자동 생성

프로필 전체 백업 시 파일명에 현재 날짜/시간(예: Tarkov-Gamma-Manager-Backup-YYYYMMDD-HHmmss.ini)이 자동으로 포함되어 기존 백업을 실수로 덮어쓰는 일을 방지했습니다.

## 🚀 주요 기능 및 특징

## 🚀 성능 및 최적화

- 비동기 세대 관리 (Generation Counter): 슬라이더 연속 조작이나 빠른 단축키 연타 시 이전 비동기 작업이 최신 설정을 덮어쓰지 않도록 차단합니다.

INI 메모리 캐싱 & 원자적(Atomic) 저장: INI 읽기/쓰기를 메모리에서 즉각 처리하며, 50ms 비동기 배치 및 3회 재시도 안전 저장 루틴을 적용했습니다.

자원 누수 제로화: 모니터 DDC/CI 핸들, WMI 인스턴스, GDI 리소스를 모니터 변경 및 프로그램 종료 시 완벽히 정리합니다.

디스플레이 연결 감지: 모니터 케이블 탈착이나 절전 모드 복귀 시 1.5초 안정화 후 물리 핸들을 자동으로 재취득합니다.

### 🔄 전체 및 개별 모니터 강제 초기화 (Hard Reset) 핫키

화면 색상이나 밝기가 꼬였을 때 한 번의 핫키 입력으로 프로그램 실행 시점의 원본 디스플레이 상태로 즉시 복구합니다.

전체 디스플레이 초기화(__HARD_RESET_ALL__) 및 개별 모니터 단독 초기화(__HARD_RESET_<모니터명>)를 지원합니다.

### 🔁 순환(Cycle) 프로필 핫키

프로필 목록에서 순환에 체크된 프로필들을 하나의 글로벌 핫키로 차례대로 순환 적용할 수 있습니다.

### 🎨 테마 및 커스텀 일러스트

다크 모드 / 라이트 모드 테마를 지원합니다.

우측 패널에 기본 모니터 이미지 외에 Killa & Tagilla 팬아트 이미지를 선택하여 적용할 수 있습니다.

## 🎮 상세 기능 안내

### ⌨️ 글로벌 핫키 (Apply / Toggle / Cycle)

Apply: 핫키 입력 즉시 해당 프로필 적용.

Toggle: 첫 번째 입력 시 프로필 적용, 두 번째 입력 시 Toggle 이전 상태(감마/대비/밝기/채도)로 완벽 복구.

Cycle: 지정된 프로필들을 순서대로 순환하며 적용.

- Windows 키(Win) 지원: Win + Hotkey 조합 등록 가능.

### 📋 프로필 목록 관리 (ProfileManagerForm)

순서 변경: 다중 선택(Shift / Ctrl) 지원, 위 / 아래 / 맨위 / 맨아래 이동

정렬: 이름순 오름차순(A-Z) / 내림차순(Z-A) 정렬

이름 수정: 중복 및 시스템 예약어 검증을 거친 안전한 이름 변경

공유: Base64 인코딩 문자열을 통한 클립보드 복사(Export) / 붙여넣기(Import)

### 💬 OSD 팝업 알림 설정 (OSDSettingsForm)

표시 위치: 상단 중앙(Top Center) / 좌측 상단(Top Left) / 우측 상단(Top Right)

폰트 색상: 초록(LimeGreen) / 노랑(Yellow) / 하늘색(SkyBlue) / 흰색(White) / 오렌지(Orange) / 빨강(Red)

상세 커스텀: 폰트 크기(20~48pt), 화면 표시 시간(1.0~3.0초)

## 🔒 안티치트 관련 안내

이 프로그램은 게임 메모리나 프로세스에 전혀 개입하지 않으며, Windows 표준 디스플레이 API 및 하드웨어 DDC/CI만을 사용하는 디스플레이 제어 유틸리티입니다.

소프트웨어 내부에는 다음과 같은 개입 기능이 일절 포함되어 있지 않습니다.

- 게임 프로세스 감지 및 포그라운드 후킹

- 게임 메모리 읽기 / 쓰기

- DLL Injection / Hooking

- 커널 드라이버 / 네트워크 패킷 조작 / 화면 캡처

⚠️ 안티치트 정책은 게임 개발사의 재량이므로 모든 환경에서의 절대적인 안전을 개발사가 보증할 수는 없습니다.

## 📦 설치 및 데이터 저장 위치

별도의 설치 과정이 필요 없는 단일 포터블 실행 파일입니다.

모든 설정, 백업, 로그 파일은 사용자 앱 데이터 폴더에 보관됩니다:

```text
%LOCALAPPDATA%\TarkovGammaManager
├─ config\ (GammaManager.ini 설정 파일)
├─ state\  (GammaManager.StartupBackup.json 원본 상태 백업)
├─ logs\   (TarkovGammaManager-YYYY-MM-DD.log)
└─ backup\ (수동 프로필 백업)
```

프로그램 우측 패널의 📁 설정 / 로그 폴더 열기 버튼을 누르면 해당 경로가 바로 열립니다.

## 📋 지원 기능 요약

| 기능 | 지원 여부 |
|---|:---:|
| Gamma / Brightness / Contrast 조절 | ✅ |
| 모니터 DDC/CI 하드웨어 밝기 및 대비 | ✅ 지원 모니터 한정 |
| AMD Saturation / NVIDIA Digital Vibrance | ✅ 지원 드라이버 한정 |
| 다중 모니터 독립/병렬 제어 (Handle Locks) | ✅ |
| 비동기 세대 관리 (Generation Counter) | ✅ |
| 글로벌 프로필 핫키 (Apply / Toggle / Cycle) | ✅ |
| Windows 키(Win) 포함 핫키 조합 | ✅ |
| Hard Reset (개별 모니터 / 전체 디스플레이) | ✅ |
| 비정상 종료 시 자동 원본 복구 (Crash Recovery) | ✅ |
| OSD 팝업 알림 & GDI 캐싱 최적화 | ✅ |
| 프로필 순서 이동 (위/아래/맨위/맨아래) 및 정렬 | ✅ |
| 프로필 Export / Import 클립보드 공유 | ✅ |
| 중복 실행 방지 및 기존 창 활성화 | ✅ |
| 설정 및 로그 폴더 원클릭 열기 (📁) | ✅ |
| 구버전 INI 자동 마이그레이션 | ✅ |
| 한국어 / English UI 지원 | ✅ |


## 📜 Credits & License

- **원본 프로젝트:** [Gamma-Manager (KrasnovM)](https://github.com/KrasnovM/Gamma-Manager)

- **팬아트 일러스트:** automatic8 ([Reddit 4K Killa & Tagilla Wallpaper](https://www.reddit.com/r/EscapefromTarkov/comments/1hqmrm6/4k_killa_tagilla_wallpaper/))

- **라이선스:** CC0 1.0 Universal ([LICENSE.txt](LICENSE.txt))

- **GitHub 저장소:** [SaPper0000/Tarkov-Gamma-Manager](https://github.com/SaPper0000/Tarkov-Gamma-Manager)
