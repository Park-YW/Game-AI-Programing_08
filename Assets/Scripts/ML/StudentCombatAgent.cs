using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(CombatCharacter))]
[RequireComponent(typeof(CooldownSystem))]
[RequireComponent(typeof(CombatActionController))]
public class StudentCombatAgent : Agent
{
    public CombatCharacter self;
    public CombatCharacter opponent;
    public CombatActionController actionController;
    public CooldownSystem cooldownSystem;
    public EpisodeManager episodeManager;

    // 강화학습 행동 공간 정의 상수
    private const int SkillNone = 0;
    private const int SkillAttack = 1;
    private const int SkillBlock = 2;
    private const int SkillDodge = 3;

    [Header("Reward Parameters")]
    [SerializeField] private float damageTakenPenalty = -0.2f;
    [SerializeField] private float damageDealtReward = 0.2f;

    [Header("MinwooBT-Specific Rewards")]
    [SerializeField] private float threatResponseEvadeGuardReward = 0.05f;     // 공격 대응 (방어/회피)
    [SerializeField] private float counterAttackReward = 0.1f;                 // 방어/회피 후 반격
    [SerializeField] private float rollCatchReward = 0.1f;                     // 회피 캐치
    [SerializeField] private float pressureManeuverReward = 0.001f;            // 체력이 낮을 때 압박 기동
    [SerializeField] private float pressureFleePenalty = -0.001f;              // 체력이 낮을 때 도망치면 패널티
    [SerializeField] private float whiffPenalty = -0.05f;                      // 헛스윙 패널티

    [Header("Distance Maintenance Rewards")]
    [SerializeField] private float outOfRangePenalty = -0.001f;     // 적정 거리를 벗어났을 때 패널티
    [SerializeField] private float optimalDistanceReward = 0.001f;  // 적정 거리(1.8 ~ 3.5) 유지 보상

    [Header("Episode Rewards")]
    [SerializeField] private float stepPenalty = -0.0005f;
    [SerializeField] private float deathPenalty = -1.0f;
    [SerializeField] private float killReward = 1.0f;

    // 보상 계산 전용 이전 상태 추적 변수 (CalculateRewards 마지막에 갱신)
    private bool wasOpponentAttackingLastStep;
    private bool wasOpponentEvadingLastStep;

    // 카운터 어택 기회 판정 타이머
    private float counterWindowEndTime = 0f;

    // 중복 보상 방지 플래그
    private bool hasRewardedForCurrentRollCatch;

    // 적 공격 애니메이션 구간 내 방어 및 피격 여부 추적 변수
    private bool tookDamageDuringOpponentAttack;
    private bool attemptedDefenseDuringOpponentAttack;

    // 예측 시스템 내부 상태 (CollectObservations에서 갱신)
    private bool wasTargetAttacking;
    private bool wasTargetEvading;
    private bool wasTargetGuarding;

    private float targetAttackVulnerableUntil;
    private float targetEvadeVulnerableUntil;
    private float targetGuardVulnerableUntil;

    // 쿨타임 예상치 상수
    private const float estimatedAttackCooldown = 1.5f;
    private const float estimatedEvadeCooldown = 2.0f;
    private const float estimatedGuardCooldown = 2.5f;

    // 체력 변화 계산용 변수
    private float previousSelfHealthRatio;
    private float previousOpponentHealthRatio;

    // 공격 확인 변수
    private bool wasAttackingLastStep;
    private bool dealtDamageDuringCurrentAttack;

    public override void Initialize()
    {
        FillDefaultReferences();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    public override void OnEpisodeBegin()
    {
        // 예측 변수 초기화
        wasTargetAttacking = false;
        wasTargetEvading = false;
        wasTargetGuarding = false;

        targetAttackVulnerableUntil = 0f;
        targetEvadeVulnerableUntil = 0f;
        targetGuardVulnerableUntil = 0f;

        // 보상 계산용 체력 초기화
        previousSelfHealthRatio = self.CurrentHealthRatio;
        previousOpponentHealthRatio = opponent.CurrentHealthRatio;

        // 추적 플래그 및 타이머 초기화
        tookDamageDuringOpponentAttack = false;
        attemptedDefenseDuringOpponentAttack = false;
        wasOpponentAttackingLastStep = false;
        wasOpponentEvadingLastStep = false;
        hasRewardedForCurrentRollCatch = false;
        counterWindowEndTime = 0f;

        wasAttackingLastStep = false;
        dealtDamageDuringCurrentAttack = false;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 상태 및 타겟 예측 갱신
        UpdatePredictions();
        var opponentAction = opponent.GetComponent<CombatActionController>();

        // 1. 내 상태 관측 (7개)
        sensor.AddObservation(self.CurrentHealthRatio);
        sensor.AddObservation(actionController.IsAttacking ? 1f : 0f);
        sensor.AddObservation(actionController.IsBlocking ? 1f : 0f);
        sensor.AddObservation(actionController.IsInvincible ? 1f : 0f);
        sensor.AddObservation(cooldownSystem.IsAttackReady() ? 1f : 0f);
        sensor.AddObservation(cooldownSystem.IsBlockReady() ? 1f : 0f);
        sensor.AddObservation(cooldownSystem.IsDodgeReady() ? 1f : 0f);

        // 2. 상대 상태 관측 (9개)
        sensor.AddObservation(opponent.CurrentHealthRatio);

        Vector3 toTarget = opponent.transform.position - self.transform.position;
        float rawDistance = toTarget.magnitude;

        // 연속적 거리 관측
        sensor.AddObservation(Mathf.Clamp(rawDistance, 0, 10f) / 10f);

        // 이산적 거리 관측 (MinwooBT 임계값 기준 적용)
        sensor.AddObservation(rawDistance <= 1.8f ? 1f : 0f); // 공격 사거리 내 
        sensor.AddObservation(rawDistance > 3.5f ? 1f : 0f);  // 유지 거리 밖

        // 로컬 방향 벡터 관측
        Vector3 localDir = self.transform.InverseTransformDirection(toTarget.normalized);
        sensor.AddObservation(localDir.x);
        sensor.AddObservation(localDir.z);

        sensor.AddObservation(opponentAction.IsAttacking ? 1f : 0f);
        sensor.AddObservation(opponentAction.IsBlocking ? 1f : 0f);
        sensor.AddObservation(opponentAction.IsInvincible ? 1f : 0f);

        // 3. 상대 쿨타임 예측 (3개)
        sensor.AddObservation(Mathf.Clamp01((targetAttackVulnerableUntil - Time.time) / estimatedAttackCooldown));
        sensor.AddObservation(Mathf.Clamp01((targetEvadeVulnerableUntil - Time.time) / estimatedEvadeCooldown));
        sensor.AddObservation(Mathf.Clamp01((targetGuardVulnerableUntil - Time.time) / estimatedGuardCooldown));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int moveIndex = actions.DiscreteActions[0]; // Branch 0
        int combatIndex = actions.DiscreteActions[1]; // Branch 1

        Vector3 moveDirection = Vector3.zero;

        // 상대방 기준 방향 벡터 계산 (Y축 높이 차이 무시)
        Vector3 rawToTarget = opponent.transform.position - transform.position;
        rawToTarget.y = 0f;

        Vector3 fwd = rawToTarget.sqrMagnitude > 0.0001f ? rawToTarget.normalized : transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        // 1. 이동 액션 정의 (총 7개)
        switch (moveIndex)
        {
            case 0: moveDirection = Vector3.zero; break;
            case 1: moveDirection = fwd; break;
            case 2: moveDirection = -fwd; break;
            case 3: moveDirection = -right; break;
            case 4: moveDirection = right; break;
            case 5: moveDirection = (fwd - right).normalized; break;
            case 6: moveDirection = (fwd + right).normalized; break;
        }

        // 2. 전투 액션 실행 (총 4개)
        switch (combatIndex)
        {
            case SkillNone:
                break;
            case SkillAttack:
                actionController.Face(fwd);
                actionController.Attack();
                break;
            case SkillBlock:
                actionController.Block(fwd);
                break;
            case SkillDodge:
                // 입력된 이동 방향이 있으면 그 방향, 없으면 후방 회피
                Vector3 dodgeDir = moveDirection.sqrMagnitude > 0 ? moveDirection : -fwd;
                actionController.Dodge(dodgeDir);
                break;
        }

        // 3. 이동 실행
        actionController.Move(moveDirection);

        // 4. 보상 계산 및 승패 판정
        CalculateRewards();
    }

    private void CalculateRewards()
    {
        float currentSelfHealth = self.CurrentHealthRatio;
        float currentOppHealth = opponent.CurrentHealthRatio;

        // 데미지 발생 검출
        bool tookDamageThisFrame = currentSelfHealth < previousSelfHealthRatio;
        bool dealtDamageThisFrame = currentOppHealth < previousOpponentHealthRatio;

        if (tookDamageThisFrame) AddReward(damageTakenPenalty);
        if (dealtDamageThisFrame) AddReward(damageDealtReward);

        var oppAction = opponent.GetComponent<CombatActionController>();
        bool isOppAttacking = oppAction.IsAttacking;
        bool isOppEvading = oppAction.IsInvincible;

        bool isOppInEvadeCooldown = Time.time < targetEvadeVulnerableUntil;
        float distanceToOpponent = Vector3.Distance(transform.position, opponent.transform.position);

        AddReward(stepPenalty);

        // =========================================================
        // [0] 헛스윙 (Whiff) 판정 추론
        // =========================================================
        bool isSelfAttacking = actionController.IsAttacking;

        // 1. 내 공격 시작 시점 (Rising Edge)
        if (isSelfAttacking && !wasAttackingLastStep)
        {
            dealtDamageDuringCurrentAttack = false;
        }

        // 2. 내 공격 애니메이션 진행 중 타격 성공 여부 추적
        if (isSelfAttacking)
        {
            if (dealtDamageThisFrame) dealtDamageDuringCurrentAttack = true;
        }

        // 3. 내 공격 종료 시점 (Falling Edge) - 헛스윙 판정
        if (!isSelfAttacking && wasAttackingLastStep)
        {
            // 공격 모션이 끝났는데 데미지를 한 번도 못 입혔다면 헛스윙
            if (!dealtDamageDuringCurrentAttack)
            {
                AddReward(whiffPenalty);
            }
        }

        // =========================================================
        // [1] 위협 대응 및 카운터 어택 추론
        // =========================================================

        // 적 공격 시작 시점 감지 및 추적 변수 초기화
        if (isOppAttacking && !wasOpponentAttackingLastStep)
        {
            tookDamageDuringOpponentAttack = false;
            attemptedDefenseDuringOpponentAttack = false;
        }

        // 적 공격 애니메이션 진행 중 피격 및 방어 행위 누적 추적
        if (isOppAttacking)
        {
            if (tookDamageThisFrame) tookDamageDuringOpponentAttack = true;
            if (actionController.IsBlocking || actionController.IsInvincible) attemptedDefenseDuringOpponentAttack = true;
        }

        // 적 공격 종료 시점 감지 및 방어 성공 1회 판정
        if (!isOppAttacking && wasOpponentAttackingLastStep)
        {
            // 방어를 시도했고 모션 내내 데미지를 입지 않았을 경우 성공
            if (attemptedDefenseDuringOpponentAttack && !tookDamageDuringOpponentAttack)
            {
                if (distanceToOpponent <= 3.0f) // 허공 가드 방지용 거리 조건
                {
                    AddReward(threatResponseEvadeGuardReward);
                    counterWindowEndTime = Time.time + 1.5f; // 카운터 기회 제공
                }
            }
        }

        // 카운터 어택 성공 판정
        if (Time.time < counterWindowEndTime && dealtDamageThisFrame)
        {
            AddReward(counterAttackReward);
            counterWindowEndTime = 0f; // 중복 방지
        }

        // =========================================================
        // [2] 구르기 캐치 판독
        // =========================================================

        // 적 구르기 시작 시점 감지
        if (isOppEvading && !wasOpponentEvadingLastStep)
        {
            hasRewardedForCurrentRollCatch = false;
        }

        // 무적 상태가 끝났으나 후딜레이(쿨타임) 중일 때 타격 성공 시
        if (isOppInEvadeCooldown && !isOppEvading)
        {
            if (dealtDamageThisFrame && !hasRewardedForCurrentRollCatch)
            {
                AddReward(rollCatchReward);
                hasRewardedForCurrentRollCatch = true;
            }
        }

        // =========================================================
        // [3] 압박 기동
        // =========================================================
        // 적 체력이 낮을 때 이동 명령이 가능한 상태에서 다가가면 보상
        if (currentOppHealth <= 0.3f && !actionController.IsBusy)
        {
            Vector3 toTarget = opponent.transform.position - transform.position;
            toTarget.y = 0f;
            float dotForward = Vector3.Dot(transform.forward, toTarget.normalized);

            if (distanceToOpponent <= 4.0f) // 일정 거리 내에서만 유효
            {
                if (dotForward > 0.5f) AddReward(pressureManeuverReward);
                else if (dotForward < -0.5f) AddReward(pressureFleePenalty);
            }
        }
        // =========================================================
        // [3.5] 거리 유지 기동 (Distance Maintenance)
        // =========================================================
        // 적 체력이 30%를 초과할 때(처형/압박 페이즈가 아닐 때) 행동 불능 상태가 아니라면 적용
        if (currentOppHealth > 0.3f && !actionController.IsBusy)
        {
            // BT의 설정값: attackDistance = 1.8f, maintainDistance = 3.5f
            if (distanceToOpponent < 1.8f)
            {
                // 너무 가깝지만 내가 공격 중이 아니라면 뒤로 물러나도록 패널티
                if (!isSelfAttacking && !actionController.IsBlocking)
                {
                    AddReward(outOfRangePenalty);
                }
            }
            else if (distanceToOpponent > 3.5f)
            {
                // 거리가 너무 멀면 다가가도록 패널티
                AddReward(outOfRangePenalty);
            }
            else
            {
                // 1.8 ~ 3.5 사이의 적정 거리를 유지하며 견제 중일 때 지속적인 소폭 보상
                AddReward(optimalDistanceReward);
            }
        }

        // =========================================================
        // [4] 에피소드 종료 판정
        // =========================================================
        bool justDied = self.IsDead && previousSelfHealthRatio > 0f;
        bool oppJustDied = opponent.IsDead && previousOpponentHealthRatio > 0f;

        if (justDied)
        {
            AddReward(deathPenalty);
            EndEpisode();
        }
        else if (oppJustDied)
        {
            AddReward(killReward);
            EndEpisode();
        }

        // =========================================================
        // 상태 갱신 (반드시 조건 판정이 끝난 마지막에 수행)
        // =========================================================
        previousSelfHealthRatio = currentSelfHealth;
        previousOpponentHealthRatio = currentOppHealth;

        wasOpponentAttackingLastStep = isOppAttacking;
        wasOpponentEvadingLastStep = isOppEvading;

        wasAttackingLastStep = isSelfAttacking;
    }

    private void FillDefaultReferences()
    {
        if (self == null) self = GetComponent<CombatCharacter>();
        if (actionController == null) actionController = GetComponent<CombatActionController>();
        if (cooldownSystem == null) cooldownSystem = GetComponent<CooldownSystem>();
        if (episodeManager == null) episodeManager = FindFirstObjectByType<EpisodeManager>();
    }

    private void UpdatePredictions()
    {
        if (opponent == null) return;
        var opponentAction = opponent.GetComponent<CombatActionController>();
        if (opponentAction == null) return;

        bool isTargetAttacking = opponentAction.IsAttacking;
        bool isTargetEvading = opponentAction.IsInvincible;
        bool isTargetGuarding = opponentAction.IsBlocking;

        // 행동 시작 1프레임 시점에 쿨타임 예상 타이머 설정
        if (isTargetAttacking && !wasTargetAttacking)
            targetAttackVulnerableUntil = Time.time + estimatedAttackCooldown;

        if (isTargetEvading && !wasTargetEvading)
            targetEvadeVulnerableUntil = Time.time + estimatedEvadeCooldown;

        if (isTargetGuarding && !wasTargetGuarding)
            targetGuardVulnerableUntil = Time.time + estimatedGuardCooldown;

        wasTargetAttacking = isTargetAttacking;
        wasTargetEvading = isTargetEvading;
        wasTargetGuarding = isTargetGuarding;
    }
}