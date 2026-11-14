# 킬딜내기 (KillDeal)

킬딜내기는 Windows에서 PUBG 경기의 팀과 선수 점수를 기록하고 결과를 공유하는 데스크톱 앱입니다. 경기 기록, 모드별 점수 계산, 순위와 팀 요약을 한 화면에서 확인할 수 있습니다.

- **BJ용**은 BJ 계정으로 로그인해 경기 방에서 팀을 편성하거나 기록을 초기화하고, 점수표 결과를 이미지로 복사합니다.
- **시청자용**은 Firebase 익명 인증으로 방에 연결해 점수를 확인하고, 허용된 경기 기록을 입력합니다.

이미 배포된 버전은 [GitHub Releases](https://github.com/SeokHyeon0/kill_deal_nae_7I/releases)에서 확인할 수 있습니다. 이 저장소의 검토용 빌드는 [Firebase 에뮬레이터 예제 설정](bundled/firebase.config.json)을 사용합니다. 에뮬레이터 설정, 시험 계정과 예제 방 준비 방법은 [로컬 데모](docs/REVIEW-DEMO.md)에 있습니다. 실제 서비스에 연결하려면 운영자가 준비한 Firebase 설정이 필요합니다.

## 빌드

Windows PowerShell에서 저장소 루트를 열고 다음 명령을 실행합니다. 스크립트는 Windows의 .NET Framework C# 컴파일러와 저장소의 빌드 의존성을 사용합니다.

```powershell
.\Build-SingleFile.ps1 -OutputDirectory .\build -Role Both -ReviewBuild
```

`build`에 `킬딜내기_BJ.exe`와 `킬딜내기_시청자.exe`가 생성됩니다. 자신의 Firebase 설정을 사용하려면 `-FirebaseConfiguration`에 해당 JSON의 경로를 지정합니다. Firebase 설정 예제에는 실제 계정 자격 증명이 필요하지 않습니다.

검토용 빌드에서는 GitHub 자동 업데이트가 비활성화됩니다. 검토용 EXE는 심사·테스트에 사용합니다. GitHub Actions의 **Windows build** 워크플로도 두 검토용 EXE를 빌드합니다. 설정과 서명 검증 절차는 [SignPath 신청 및 설정](docs/SIGNPATH-SETUP.md)을 참고하세요.

## Code signing policy

- [코드 서명 정책](CODE_SIGNING_POLICY.md)
- [개인정보 처리방침](PRIVACY.md)
- [프로젝트 소스 라이선스](LICENSE)
- [서드파티 라이선스](THIRD_PARTY_NOTICES.md)
