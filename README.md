# Unity Skill Manager

설치된 Unity 패키지의 `Skills~`를 찾아 프로젝트 루트의 `.agents/skills`와 `.claude/skills`에 취합합니다. Unity 2022.3 이상을 대상으로 합니다.

## 사용

1. Package Manager → **+ → Add package from disk**에서 이 패키지의 `package.json`을 선택합니다.
2. **Tools → Achieve → Unity Skill Manager**에서 발견한 스킬과 등록 상태를 확인합니다.
3. **모두 동기화** 또는 선택한 스킬의 **등록·갱신**을 누릅니다. 대상 도구와 자동 동기화를 선택할 수 있습니다.

자동 동기화는 에디터를 열거나 패키지가 추가·변경·제거될 때 실행됩니다. 로컬 `Skills~` 파일을 직접 수정한 경우 **모두 동기화**를 누르세요. 창의 **한국어 / English**로 언어를 선택합니다.

## 패키지에서 스킬 제공

```text
Skills~/
└─ my-skill/
   ├─ SKILL.md
   ├─ agents/openai.yaml       # 선택
   └─ references/             # 선택
```

`SKILL.md`는 YAML frontmatter의 `name`·`description`을 포함해야 하며 이름은 폴더명과 같아야 합니다. 참조 파일·스크립트·이미지도 함께 복사합니다.

다른 패키지가 같은 이름을 제공하거나 기존 파일을 직접 수정했다면 보존하고 충돌 상태를 표시합니다. 패키지를 제거하면 관리자가 등록한 변경 없는 스킬만 정리합니다. **프로젝트에서 제거**한 스킬은 다시 등록할 때까지 제외됩니다.

등록 이력과 프로젝트 설정은 `ProjectSettings/AchieveSkillManager.json`에 저장합니다. 팀에서 공유하려면 이 파일과 프로젝트의 스킬 폴더를 함께 커밋하세요. 관리 패키지 자체를 제거할 때는 제거 전에 대상 체크박스를 해제하고 동기화하면 됩니다.
