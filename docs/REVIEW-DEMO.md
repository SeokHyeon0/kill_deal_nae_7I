# 심사용 로컬 데모

Windows의 .NET Framework 4.7.2 이상과 Firebase CLI를 준비합니다. [Firebase 에뮬레이터 설치 안내](https://firebase.google.com/docs/emulator-suite/install_and_configure)

## 에뮬레이터 시작

저장소 루트에서 다음 명령을 실행합니다. 이 설정은 로컬 PC의 Auth 9099, Realtime Database 9000, 에뮬레이터 UI 4000을 사용합니다.

```powershell
firebase emulators:start --only auth,database --config firebase.review.json --project demo-killdeal-signpath
```

1. 브라우저에서 에뮬레이터 UI의 **Authentication → Add user**로 시험 계정을 만듭니다. 시험 계정의 UID를 복사합니다.
2. **Realtime Database**의 빈 데이터 루트에 [예제 방 데이터](../signpath/firebase.review-room.json)를 가져옵니다. 파일을 선택할 때 현재 대상이 `demo-killdeal-signpath` 로컬 에뮬레이터인지 확인합니다.
3. 같은 로컬 DB에 `roles/admins/<시험 계정 UID>`의 값을 Boolean `true`로 추가합니다.
4. 심사용 BJ EXE로 시험 계정에 로그인합니다. 시청자 EXE는 익명 인증으로 연결됩니다. 두 앱에서 **심사용 킬내기** 방을 선택합니다.

임의의 시험 전용 이메일과 비밀번호를 사용하세요. 심사용 EXE는 GitHub 업데이트 조회를 건너뛰고 같은 PC에서 실행 중인 Firebase 에뮬레이터에만 연결합니다. 기존 Firebase Console에 예제 데이터를 가져올 필요는 없습니다.
