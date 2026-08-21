# Tarkov Gamma Manager v1.4

Escape from Tarkov용 휴대형 Windows 디스플레이 감마/프로필 관리자입니다.

## v1.4 변경 사항

- 프로그램 실행 시 감지된 **모든 모니터**에 대해 자동 **기본값** 프로필을 생성합니다.
- 기본값 프로필은 각 모니터에 연결되어 해당 모니터의 메인 화면 프로필 목록에 표시됩니다.
- 같은 모니터별 기본값 프로필이 **게임 자동**과 **핫키**의 프로필 선택 목록에도 표시됩니다.
- 기존에 저장된 프로필은 덮어쓰지 않습니다.
- 한글 버전과 영어 버전을 별도로 제공합니다.
- 시작 시 디스플레이 상태 백업/복구 기능은 그대로 유지되어, 프로그램 종료 시 실행 전 디스플레이 상태를 복구합니다.

## 사용 방법

- 모니터 목록에서 모니터를 선택합니다.
- 해당 모니터의 프로필 목록에서 프로필을 선택합니다.
- **게임 자동**에서 게임 EXE별 프로필을 지정할 수 있습니다.
- **핫키**에서 프로필별 글로벌 단축키를 지정할 수 있습니다.
- 자동 생성되는 기본값 프로필 이름은 `기본값 - <모니터>` 형식입니다.

설정은 실행 파일과 같은 폴더의 `GammaManager.ini`에 저장됩니다.


## v1.4
- Added full settings backup/restore.
- Backup includes saved monitor profiles, per-monitor defaults, Hotkeys, Game Auto mappings, and application settings stored by Gamma Manager.
- Restore creates an automatic `.pre-restore-YYYYMMDD-HHMMSS.ini` safety backup before replacing the current settings.
- Backups use the native INI format so they can be retained for future version upgrades.


## 핫키 토글 / Hotkey Toggle

- 핫키 설정에서 각 프로필의 동작 방식을 **적용(Apply)** 또는 **토글(Toggle)**로 선택할 수 있습니다.
- **토글**로 지정한 핫키는 한 번 누르면 해당 프로필을 적용하고, 같은 핫키를 다시 누르면 토글 직전에 활성화되어 있던 프로필로 돌아갑니다.
- 게임 자동으로 A 프로필이 적용된 상태에서 B 프로필의 토글 핫키를 누르면 B가 적용되고, 같은 핫키를 다시 누르면 A로 복귀합니다.
- 기존 핫키는 호환성을 위해 기본값이 **적용(Apply)**으로 유지됩니다.
