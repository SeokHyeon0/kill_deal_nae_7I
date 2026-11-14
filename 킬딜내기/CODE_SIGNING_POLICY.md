# Code signing policy

이 저장소는 SignPath Foundation의 오픈소스 코드 서명 프로그램 신청을 준비 중입니다. 승인 및 서명된 배포본의 게시 여부는 해당 [릴리스](https://github.com/SeokHyeon0/kill_deal_nae_7I/releases)에서 확인할 수 있습니다.

## 서명 대상과 빌드 출처

킬딜내기 프로젝트가 개발한 BJ용 `KillDeal-BJ.exe`와 시청자용 `KillDeal-Viewer.exe`가 대상입니다. 두 파일의 제품 이름은 `킬딜내기`이며, 제품 버전은 같은 릴리스 버전으로 맞춥니다. 서명 요청은 공개 소스 저장소의 [Windows build 워크플로](.github/workflows/windows-build.yml)가 GitHub 호스팅 Windows 러너에서 컴파일한 결과를 사용합니다.

## 프로젝트 역할과 승인

| 역할 | 담당 |
| --- | --- |
| 소스 및 빌드 스크립트 작성·관리 | [SeokHyeon0](https://github.com/SeokHyeon0) |
| 외부 변경 검토 | [SeokHyeon0](https://github.com/SeokHyeon0) |
| 서명 요청 승인 | [SeokHyeon0](https://github.com/SeokHyeon0) |

담당자는 GitHub와 SignPath에서 2단계 인증을 사용하고, 서명 요청마다 빌드할 커밋과 대상 파일을 확인한 뒤 수동으로 승인합니다. 서명 작업은 SignPath의 빌드 출처 검증을 거칩니다.

## 배포본 확인

재단에서 승인받고 서명한 릴리스에 표시할 문구는 다음과 같습니다.

> Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

재단 승인을 받아 서명한 EXE는 Windows의 **속성 → 디지털 서명**에서 SignPath Foundation 게시자와 서명 상태를 확인할 수 있습니다. 해당 릴리스의 배포 안내에도 이 서명 정책 링크를 포함합니다.

기존 킬딜내기 업데이트의 제작자 RSA 개인키는 별도로 보호합니다. 소스에는 검증용 공개키가 포함되어 있습니다. 기존 자동 업데이트로 Windows 서명판을 받게 하는 작업은 두 검증의 호환성을 확인한 뒤 진행합니다.

- [개인정보 처리방침](PRIVACY.md)
- [SignPath Foundation 이용 조건](https://signpath.org/terms.html)
