# SignPath 신청 및 설정

## 1. GitHub에 올리기

1. 이 소스의 공개 범위와 [프로젝트 라이선스](../LICENSE)를 확인합니다. 준비된 초안에는 제작자 표기인 karist와 MIT를 사용했습니다. 공개 전에 프로젝트 소스의 권리자와 라이선스를 확인하세요.
2. ZIP을 풀고 숨김 폴더인 `.github`까지 공개 저장소에 올립니다. RSA 개인키와 백업, Firebase 관리자 자격 증명, 실제 이용자 데이터는 로컬에 보관합니다.
3. 본인의 GitHub와 SignPath 계정에서 2단계 인증을 켭니다.
4. GitHub의 **Actions**에서 Windows 워크플로가 성공하고 `killdeal-review-unsigned`에 BJ용·시청자용 EXE 두 개가 있는지 확인합니다.
5. [코드 서명 정책](../CODE_SIGNING_POLICY.md)과 [개인정보 처리방침](../PRIVACY.md)을 확인한 뒤, [SignPath Foundation 신청서](https://signpath.org/apply.html)에 공개 저장소와 [기존 배포 페이지](https://github.com/SeokHyeon0/kill_deal_nae_7I/releases) 링크를 제출합니다.

심사자에게 검토용 EXE는 [로컬 데모](REVIEW-DEMO.md)를 사용한다고 설명하세요. GitHub 자동 업데이트는 이 빌드에서 비활성화됩니다. 재단은 이미 배포된 프로젝트를 대상으로 하므로 기존 배포 링크를 제시하고, 추가 자료를 요청받으면 재단에 전달하세요.

## 2. 재단에서 승인받은 뒤

1. SignPath 프로젝트에 GitHub.com을 신뢰 빌드 시스템으로 연결하고 소스 저장소와 빌드 출처를 지정합니다. 공개 프로젝트에서 서명 정책이 검증용 두 EXE에만 적용되는지 확인합니다. 서명 정책에서 프로젝트 담당자를 승인자로 지정해 수동 승인을 설정합니다. [SignPath 프로젝트 설정](https://docs.signpath.io/projects), [GitHub 연동](https://docs.signpath.io/trusted-build-systems/github)
2. 이 저장소의 [서명 대상 XML](../signpath/artifact-configuration.xml)을 사용해 BJ용과 시청자용 두 EXE를 서명 대상으로 구성합니다.
3. GitHub에서 **Settings → Environments**에 `signpath`를 만듭니다. 아래 값은 **Settings → Secrets and variables → Actions → Variables** 또는 해당 환경의 **Variables**에 추가합니다.

   - `SIGNPATH_ORGANIZATION_ID`
   - `SIGNPATH_PROJECT_SLUG`
   - `SIGNPATH_SIGNING_POLICY_SLUG`
   - `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`

   아래 값은 `signpath` 환경의 **Environment secrets**에 추가합니다.

   - `SIGNPATH_API_TOKEN` — SignPath CI 서비스 계정의 요청 제출용 토큰

4. 기본 브랜치에서 **Actions → Windows build → Run workflow**를 열고 `signpath-test`를 선택합니다. SignPath에 대기 중인 요청을 확인해 승인하고, GitHub의 실행 결과에서 `killdeal-signpath-verification`을 확인합니다. 이 워크플로는 결과물을 Actions에 보관합니다. SignPath는 ZIP 안의 EXE 두 개를 서명하며 외부 ZIP은 운반용입니다. 이 결과물은 로컬 에뮬레이터를 쓰므로 심사·테스트용으로 보관합니다.

## 3. 실제 업데이트 게시 전에

실제 서비스에 연결하는 후보는 `signpath-release-candidate` 모드에서 빌드할 수 있습니다. [`signpath/firebase.release.example.json`](../signpath/firebase.release.example.json)을 참고해 `signpath/firebase.release.json`을 작성하고, 검증된 Firebase 클라이언트 설정을 커밋합니다. [Firebase API 키 안내](https://firebase.google.com/docs/projects/api-keys)에 따라 Firebase 전용으로 제한된 클라이언트용 키만 사용하세요. 서비스 계정과 관리자 자격 증명은 제외합니다. 결과는 `killdeal-signpath-release-candidate`에서 내려받을 수 있습니다. Windows 서명과 실제 Firebase 연결을 확인한 뒤 수동 설치 배포본으로 전달하세요.

현재 킬딜내기 자동 업데이트는 EXE 맨 끝에 붙는 기존 RSA 서명을 검증합니다. SignPath 서명만 적용한 후보를 기존 자동 업데이트용 릴리스의 `KillDeal-BJ.exe` 또는 `KillDeal-Viewer.exe`로 게시하면 설치된 프로그램이 검증에서 거부합니다. 그 파일은 수동 다운로드용으로 별도 제공하세요. 후보가 사용하는 GitHub 저장소의 최신 릴리스에 더 높은 버전의 기존 RSA 배포본이 있다면 후보 EXE가 그 파일로 자동 갱신될 수 있으므로 배포 전에 현재 피드를 확인하세요. 자동 업데이트로 전달하려면 배포 형식과 검증 코드의 호환 작업이 필요합니다.

재단 승인과 실제 서명판 게시 후에는 배포 페이지에 [코드 서명 정책](../CODE_SIGNING_POLICY.md)을 링크하고, 서명 제공자와 인증서 명의에 관한 안내를 [재단 약관](https://signpath.org/terms.html)에 맞게 추가합니다.
