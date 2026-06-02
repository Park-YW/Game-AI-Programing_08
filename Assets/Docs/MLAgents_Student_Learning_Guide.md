# ML-Agents 전투 RL 학습 가이드

**ML-Agents를 처음 한다면** → 아래 **[Part B (2단계~)]** 만 따라가도 됩니다.  
환경 설치 상세: [MLAgents_Environment_Run_Guide.md](./MLAgents_Environment_Run_Guide.md)  
컴포넌트 목록: [BT_RL_Setup_Guide.md](./BT_RL_Setup_Guide.md)

---

## Part A — 0~1단계 (게임이 되는지 확인)

### 0단계: 준비

- Unity **6000.0.39f1**, 씬 `Assets/Scenes/TermProject_Arena.unity`
- 나중에 학습할 때: Python **3.10.12** + `requirements-mlagents.txt`

### 1단계: 키보드로 싸워 보기 (RL 전 필수)

1. **AgentA** 선택 → `ManualCombatInput` **ON**
2. `StudentCombatAgent`, `BehaviorParameters`, `DecisionRequester`, BT는 **OFF**
3. Play → WASD / J(공격) / K(블록) / L(회피) 동작 확인
4. **AgentB**에 `BaselineDefenderBT` 켜고 1대1 → 죽으면 리셋되는지 확인

여기까지 되면 **2단계로** 갑니다.

---

# Part B — ML-Agents 처음 (2단계~)

## 이게 뭔지 (30초)

- **1판 싸움** = 에피소드
- Unity가 상황을 **숫자 15개**로 Python에 보냄 → **관측**
- Python이 **번호 2개**(이동 + 스킬)를 골라 줌 → **행동**
- 당신 코드가 번호를 `Move()`, `Attack()`으로 **번역**
- 잘하면 **점수(+)** , 나쁘면 **점수(-)** → **보상**
- 반복하면 Python 쪽 **모델**이 나아짐

```text
  Unity                              Python (선생님)
  ─────                              ───────────────
  CollectObservations  ──15숫자──▶   다음 행동 결정
  OnActionReceived     ◀──2번호──    (PPO 학습)
        ▼
  CombatActionController (실제 조작)
```

**당신이 할 일:** `StudentCombatAgent.cs` **한 파일** + Unity에서 **연결·체크박스**.

**BT와 차이:** BT는 `if` 규칙, RL은 점수로 배움. 둘 다 `CombatActionController`만 호출.

---

## 2단계 — Unity 숫자만 확인 (코드 안 써도 됨)

### 지금 할 일 (한 줄)

> AgentA 클릭 → **Behavior Parameters**가 아래 표와 같은지 **눈으로 확인**.

### Inspector에서 보는 곳

1. Hierarchy → **AgentA** (또는 Agent_A)
2. Inspector → **Behavior Parameters**

| 항목 | 값 | 쉬운 뜻 |
|------|-----|---------|
| Behavior Name | `CombatAgent` | Python 설정과 이름 맞춤 |
| Behavior Type | `Default` | 학습 중 |
| Vector Observation → Space Size | **15** | 상황을 숫자 **15개**로 |
| Continuous Actions | **0** | 실수 조작 안 씀 |
| Discrete Branches | **2** | 선택 묶음 **2개** |
| Branch 0 Size | **5** | **이동** 5종 |
| Branch 1 Size | **4** | **스킬** 4종 |

3. 같은 오브젝트 → **Decision Requester**: Period **5**, Take Actions Between Decisions **체크**

### 행동(action) = 버튼 번호

Python이 주는 번호를 **당신이 조작으로 바꿉니다.**

**Branch 0 — 이동 (`actions.DiscreteActions[0]`)**

| 번호 | 의미 | 호출 |
|------|------|------|
| 0 | 가만히 | 없음 |
| 1 | 앞 | `Move(transform.forward)` |
| 2 | 뒤 | `Move(-transform.forward)` |
| 3 | 왼 | `Move(-transform.right)` |
| 4 | 오른 | `Move(transform.right)` |

**Branch 1 — 스킬 (`actions.DiscreteActions[1]`)**

| 번호 | 의미 | 호출 |
|------|------|------|
| 0 | 없음 | — |
| 1 | 공격 | `Face(상대)` → `Attack()` |
| 2 | 블록 | `Block(상대)` |
| 3 | 회피 | `Face` → `Dodge(...)` |

### 관측(observation) = 쪽지 15장

AI는 화면을 못 보니 `sensor.AddObservation(숫자)` 를 **정확히 15번** 호출합니다.  
Inspector의 **15**와 개수가 다르면 **에러** 납니다.

### 2단계 끝

- [ ] 위 숫자 확인함
- [ ] 행동 = 번호 2개, 관측 = 숫자 15개 라고 이해함

---

## 3단계 — `StudentCombatAgent.cs` 작성

파일: `Assets/Scripts/ML/StudentCombatAgent.cs`  
**순서: A → B → C** (한 번에 다 넣지 말 것)

---

### 3-A. Unity에서 드래그 연결 (코드 전)

AgentA 선택 → **Student Combat Agent**:

| 필드 | 넣을 것 |
|------|---------|
| Self | AgentA의 Combat Character |
| Opponent | AgentB의 Combat Character |
| Action Controller | AgentA의 Combat Action Controller |
| Cooldown System | AgentA의 Cooldown System |
| Episode Manager | GameManager의 Episode Manager |

**Opponent는 자동 안 채워짐** → 비어 있으면 꼭 연결.

---

### 3-B. `CollectObservations` — 쪽지 15장

클래스 안에 필드 추가:

```csharp
[SerializeField] private float maxDistance = 10f;
```

`CollectObservations` 전체를 아래로 교체 (`AddObservation` **15번**인지 확인):

```csharp
public override void CollectObservations(VectorSensor sensor)
{
    if (self == null || opponent == null)
    {
        for (int i = 0; i < 15; i++) sensor.AddObservation(0f);
        return;
    }

    Vector3 toOpponent = opponent.transform.position - transform.position;
    toOpponent.y = 0f;
    float distance = toOpponent.magnitude;
    Vector3 dir = distance > 0.001f ? toOpponent / distance : Vector3.forward;
    Vector3 localDir = transform.InverseTransformDirection(dir);

    CooldownSystem oppCd = opponent.CooldownSystem;
    CombatActionController oppAct = opponent.ActionController;

    sensor.AddObservation(self.CurrentHealthRatio);
    sensor.AddObservation(opponent.CurrentHealthRatio);
    sensor.AddObservation(Mathf.Clamp01(distance / maxDistance));
    sensor.AddObservation(localDir.x);
    sensor.AddObservation(localDir.z);
    sensor.AddObservation(cooldownSystem.GetAttackCooldownRatio());
    sensor.AddObservation(cooldownSystem.GetBlockCooldownRatio());
    sensor.AddObservation(cooldownSystem.GetDodgeCooldownRatio());
    sensor.AddObservation(oppCd != null ? oppCd.GetAttackCooldownRatio() : 0f);
    sensor.AddObservation(oppCd != null ? oppCd.GetBlockCooldownRatio() : 0f);
    sensor.AddObservation(oppCd != null ? oppCd.GetDodgeCooldownRatio() : 0f);
    sensor.AddObservation(actionController.IsBusy ? 1f : 0f);
    sensor.AddObservation(oppAct != null && oppAct.IsAttacking ? 1f : 0f);
    sensor.AddObservation(oppAct != null && oppAct.IsBlocking ? 1f : 0f);
    sensor.AddObservation(oppAct != null && oppAct.IsInvincible ? 1f : 0f);
}
```

---

### 3-C. `OnActionReceived` — 번호 → 조작 + 점수

#### (1) 먼저: 고정 행동 테스트 (Python 없이 Play)

`OnActionReceived`만 아래로 넣고 Play → AgentA가 **앞으로 가며 공격**하면 성공.

```csharp
public override void OnActionReceived(ActionBuffers actions)
{
    actionController.Move(transform.forward);
    if (opponent == null) return;

    Vector3 dir = opponent.transform.position - transform.position;
    dir.y = 0f;
    if (dir.sqrMagnitude <= 0.0001f) return;
    dir.Normalize();
    actionController.Face(dir);
    actionController.Attack();
}
```

#### (2) 다음: Python 번호 읽기 + 보상

필드 추가:

```csharp
private float lastSelfHealth;
private float lastOpponentHealth;

public override void OnEpisodeBegin()
{
    lastSelfHealth = self != null ? self.CurrentHealth : 0f;
    lastOpponentHealth = opponent != null ? opponent.CurrentHealth : 0f;
}
```

`OnActionReceived` 본문:

```csharp
public override void OnActionReceived(ActionBuffers actions)
{
    Vector3 toOpponent = Vector3.forward;
    if (opponent != null)
    {
        toOpponent = opponent.transform.position - transform.position;
        toOpponent.y = 0f;
        if (toOpponent.sqrMagnitude > 0.0001f) toOpponent.Normalize();
    }

    int move = actions.DiscreteActions[0];
    int skill = actions.DiscreteActions[1];

    switch (move)
    {
        case 1: actionController.Move(transform.forward); break;
        case 2: actionController.Move(-transform.forward); break;
        case 3: actionController.Move(-transform.right); break;
        case 4: actionController.Move(transform.right); break;
    }

    switch (skill)
    {
        case 1:
            actionController.Face(toOpponent);
            actionController.Attack();
            break;
        case 2:
            actionController.Block(toOpponent);
            break;
        case 3:
            actionController.Face(toOpponent);
            actionController.Dodge(-toOpponent);
            break;
    }

    if (self != null && opponent != null)
    {
        float dmgToOpponent = lastOpponentHealth - opponent.CurrentHealth;
        float dmgToSelf = lastSelfHealth - self.CurrentHealth;
        if (dmgToOpponent > 0f) AddReward(0.1f);
        if (dmgToSelf > 0f) AddReward(-0.1f);
        lastOpponentHealth = opponent.CurrentHealth;
        lastSelfHealth = self.CurrentHealth;
    }

    AddReward(-0.001f);

    if (opponent != null && opponent.IsDead)
    {
        AddReward(1f);
        EndEpisode();
    }
    else if (self != null && self.IsDead)
    {
        AddReward(-1f);
        EndEpisode();
    }
}
```

**보상:** 상대 HP 깎음 +0.1, 내가 맞음 -0.1, 이김 +1, 짐 -1, 매 프레임 -0.001(오래 끌면 불리).

**체력·데미지 숫자는 직접 수정하지 마세요.** `Attack()` 등만 씁니다.

### 3단계 끝

- [ ] Opponent 연결
- [ ] 고정 행동으로 움직임 확인
- [ ] 관측 15개 + `DiscreteActions[0],[1]` 구현
- [ ] 승/패 시 `EndEpisode()`

---

## 4단계 — Unity 체크박스 (코딩 아님)

### AgentA (내 RL 캐릭터)

| 컴포넌트 | ON/OFF |
|----------|--------|
| Student Combat Agent | ON |
| Behavior Parameters | ON |
| Decision Requester | ON |
| Combat Action Controller | ON |
| Manual Combat Input | **OFF** |
| Baseline Attacker BT | **OFF** |

### AgentB (연습 상대)

| 컴포넌트 | ON/OFF |
|----------|--------|
| Baseline Defender BT | ON |
| Student Combat Agent / Behavior Parameters / Decision Requester | **OFF** |

**한 캐릭터에 Manual + BT + RL 동시 ON 금지** (명령이 겹침).

GameManager → Episode Manager에 Agent A/B, 스폰 위치 연결.

---

## 5단계 — Python 켜고 학습

### 처음 한 번 (설치)

```cmd
cd F:\Unity\Game-AI-Programing_08\Game-AI-Programing_08
conda create -n mlagents-combat python=3.10.12
conda activate mlagents-combat
pip install -r requirements-mlagents.txt
mlagents-learn --version
```

### 매번 (순서 중요!)

**① 터미널 먼저**

```cmd
conda activate mlagents-combat
cd F:\Unity\Game-AI-Programing_08\Game-AI-Programing_08
mlagents-learn Assets/Config/combat_ppo.yaml --run-id=test1 --timeout-wait 600
```

Unity 연결 **대기** 문구가 나올 때까지 기다림.

**② Unity** → `TermProject_Arena` → 4단계 확인 → **Play**

**③ 성공:** 터미널에 `Step`, `Mean Reward` 증가 / AgentA가 움직임 (처음엔 서툴어도 정상)

결과: `results/test1/` 폴더

| 문제 | 확인 |
|------|------|
| 연결 안 됨 | Python을 Play **보다 먼저**, Behavior Name `CombatAgent` |
| 에러 | `AddObservation` 15번 맞는지 |
| 안 움직임 | `OnActionReceived`에 `Move` 있는지, AgentA ML ON |

---

## 6~7단계 — 확인 & 완성 모델

- **6:** Step 증가, `results/test1` 생성 → 학습 돌아가는 중
- **7 (나중):** `results/.../CombatAgent/*.onnx` → Behavior Type **Inference Only** + Model 할당 → Python 없이 Play

---

## 2~5단계 할 일만 (표)

| 단계 | 할 일 | 어디 |
|------|--------|------|
| 2 | 숫자 15, 행동 [5][4] 확인·이해 | Unity Inspector |
| 3-A | Opponent 등 연결 | Unity |
| 3-B | `CollectObservations` 15줄 | C# |
| 3-C | 고정 행동 테스트 → `OnActionReceived` + 보상 | C# |
| 4 | AgentA=RL ON, AgentB=BT | Unity 체크박스 |
| 5 | `mlagents-learn` → Play | 터미널 + Unity |

---

## 참고: BT 코드 보는 법

RL도 BT와 같이 `CombatActionController`만 씁니다.

- `Assets/Scripts/BT/BaselineAttackerBT.cs` — 상대 쪽으로 `Move`, `Attack`
- `Assets/Scripts/BT/BaselineDefenderBT.cs` — `Block`, `Dodge`

---

## 관련 문서 · 제출 체크

| 문서 | 용도 |
|------|------|
| [MLAgents_Environment_Run_Guide.md](./MLAgents_Environment_Run_Guide.md) | pip, 연결 오류 |
| [BT_RL_Setup_Guide.md](./BT_RL_Setup_Guide.md) | 컴포넌트·Behavior 숫자 |

제출 전:

- [ ] 관측 15 = 코드 `AddObservation` 15번
- [ ] AgentA만 RL, AgentB BT
- [ ] `mlagents-learn` 로그·`results/` 폴더

---

*Unity 6000.0.39f1 / ML-Agents 1.1.0 기준*
