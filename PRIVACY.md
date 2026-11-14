# 킬딜내기 개인정보 처리방침

이 문서는 킬딜내기 Windows 앱과 프로젝트의 기본 네트워크 기능에서 처리하는 정보를 설명합니다. 별도 Firebase 프로젝트에 연결하는 빌드는 해당 프로젝트 운영자의 설정과 접근 정책을 따릅니다.

## Firebase에 연결할 때

- **BJ 인증**에 입력한 이메일과 비밀번호는 로그인 요청으로 Google Firebase Authentication에 전송됩니다. 앱은 비밀번호를 저장하지 않습니다. 다음 로그인에 사용하는 갱신 토큰과 계정 식별자는 Windows 자격 증명 관리자에 보관됩니다.
- **시청자 인증**에는 Firebase가 발급한 익명 계정 식별자를 사용합니다. 갱신 토큰은 현재 Windows 사용자 계정의 데이터 보호 기능으로 암호화해 로컬에 보관합니다.
- **경기 방을 이용할 때**는 방 정보, 입력한 선수 이름, 점수, 경기 모드, 편집 기록을 Firebase Realtime Database에 저장하고 동기화합니다. 인증 토큰은 요청의 접근 권한을 확인하는 데 사용합니다. 기록은 방에 접근할 수 있는 앱 사용자에게 표시될 수 있습니다. 선수 이름에는 방송용 닉네임을 사용할 수 있습니다.

Firebase의 정보 처리에 관한 설명은 [Google Firebase 개인정보 보호 안내](https://firebase.google.com/support/privacy)에서 확인할 수 있습니다.

## GitHub와 로컬 PC

- 앱을 시작하면 최신 배포 정보 확인과 업데이트 파일 다운로드를 위해 GitHub Releases에 접속할 수 있습니다. GitHub는 접속 과정에서 IP 주소 등 연결 정보를 처리할 수 있습니다. [GitHub 개인정보 처리방침](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement)
- 창 위치 등의 환경설정, 익명 인증 정보, 업데이트 임시 파일과 상태 로그를 Windows 사용자 폴더에 저장합니다. BJ에서 결과를 복사하면 결과 이미지가 Windows 클립보드에 들어갑니다.

삭제 또는 접근 관련 문의는 [프로젝트 관리자](https://github.com/SeokHyeon0)에게 연락하세요. 문의 메시지에 로그인 정보나 토큰을 포함하지 마세요.
