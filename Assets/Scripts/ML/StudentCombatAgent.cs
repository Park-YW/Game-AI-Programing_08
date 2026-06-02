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

    // Action constants for a clear RL action space.
    // Branch 1: 스킬 제어 (0 = 안함, 1 = 공격, 2 = 가드, 3 = 회피)
    private const int SkillNone = 0;
    private const int SkillAttack = 1;
    private const int SkillBlock = 2;
    private const int SkillDodge = 3;

    // Branch 0: 이동 제어용 상수 추가 (0 = 정지, 1 = 전진, 2 = 후퇴)
    private const int MoveNone = 0;
    private const int MoveForward = 1;
    private const int MoveBackward = 2;

    // 학습용 실시간 상태 체크 변수들
    private float lastSelfHealth = 1f;
    private float lastOpponentHealth = 1f;

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
        // 에피소드가 시작될 때 체력 기록 초기화
        if (self != null && opponent != null)
        {
            lastSelfHealth = self.CurrentHealthRatio;
            lastOpponentHealth = opponent.CurrentHealthRatio;
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // [총 10차원의 관측 정보 수집] -> 유니티 에디터 Space Size에 '10' 입력 필수
        if (self == null || opponent == null || cooldownSystem == null) return;

        // 1. 체력 상태 (2차원)
        sensor.AddObservation(self.CurrentHealthRatio);
        sensor.AddObservation(opponent.CurrentHealthRatio);

        // 2. 내 쿨타임 상태 (3차원)
        sensor.AddObservation(cooldownSystem.IsAttackReady());
        sensor.AddObservation(cooldownSystem.IsBlockReady());
        sensor.AddObservation(cooldownSystem.IsDodgeReady());

        // 3. 상대방과의 거리 및 방향 (2차원)
        Vector3 offset = GetHorizontalOffsetToTarget();
        sensor.AddObservation(offset.magnitude); // 직선 거리
        sensor.AddObservation(Vector3.Dot(transform.forward, offset.normalized)); // 정면 조준 각도 일치성

        // 4. 상대 수비형 BT의 실시간 액션 상태 감지 (3차원) - 공격형 RL의 핵심 힌트
        if (opponent.ActionController != null)
        {
            sensor.AddObservation(opponent.ActionController.IsBlocking); // 상대 방패 활성화 여부
            sensor.AddObservation(opponent.ActionController.IsAttacking); // 상대 공격 여부
        }
        else
        {
            sensor.AddObservation(false);
            sensor.AddObservation(false);
        }
        sensor.AddObservation(actionController.IsBlocking || actionController.IsAttacking); // 내 행동 여부
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (self == null || opponent == null || actionController == null || cooldownSystem == null) return;

        // ---------------------------------------------------------------------
        // 1. 유니티 인스펙터 Behavior Parameters 설정 매핑 규칙
        //    - Discrete Branches 수: 2
        //    - Branch 0 Size: 3 (0:정지, 1:전진, 2:후퇴)
        //    - Branch 1 Size: 4 (0:안함, 1:공격, 2:가드, 3:회피)
        // ---------------------------------------------------------------------
        int moveCommand = actions.DiscreteActions[0];
        int skillCommand = actions.DiscreteActions[1];

        Vector3 dirToTarget = GetDirectionToTarget();
        float distance = GetHorizontalOffsetToTarget().magnitude;

        // 상시 타겟 시선 고정
        actionController.Face(dirToTarget);

        // ---------------------------------------------------------------------
        // 2. Branch 0 : 이동 제어 실행
        // ---------------------------------------------------------------------
        // 가드 중이거나 공격 애니메이션 실행 중이 아닐 때만 무빙 가능
        if (!actionController.IsBlocking && !cooldownSystem.IsAttackReady())
        {
            if (moveCommand == MoveForward)
            {
                actionController.Move(dirToTarget);
            }
            else if (moveCommand == MoveBackward)
            {
                actionController.Move(-dirToTarget);
            }
            else
            {
                actionController.Move(Vector3.zero);
            }
        }

        // ---------------------------------------------------------------------
        // 3. Branch 1 : 스킬 및 전투 제어 실행
        // ---------------------------------------------------------------------
        if (skillCommand == SkillAttack && cooldownSystem.IsAttackReady())
        {
            // [패널티 설계] 상대방 수비형 BT가 방패를 들고 굳건히 막고 있는데 공격을 박은 경우
            if (opponent.ActionController != null && opponent.ActionController.IsBlocking)
            {
                AddReward(-1.5f); // 무지성 방패 들이받기 벌점 (가드를 피해 치도록 학습 유도)
            }
            // 허공 칼질 패널티 (사거리가 안 닿는데 공격 날린 경우)
            else if (distance > 2.0f)
            {
                AddReward(-0.2f);
            }

            actionController.Attack();
        }
        else if (skillCommand == SkillBlock && cooldownSystem.IsBlockReady())
        {
            actionController.Block(dirToTarget);
        }
        else if (skillCommand == SkillDodge && cooldownSystem.IsDodgeReady())
        {
            // 상대방 공격형 유무에 따라 회피 기동
            actionController.Dodge(dirToTarget);
        }

        // ---------------------------------------------------------------------
        // 4. 실시간 정밀 보상 체계 계산 (Reward Function)
        // ---------------------------------------------------------------------

        // 가) 타격 성공 보상 (상대 체력이 줄어들었을 때 칭찬)
        if (opponent.CurrentHealthRatio < lastOpponentHealth)
        {
            float damageDealt = lastOpponentHealth - opponent.CurrentHealthRatio;
            AddReward(damageDealt * 4.0f); // 대미지 비율만큼 가산점 부여 (+1.0 ~ +2.0 상당)
            lastOpponentHealth = opponent.CurrentHealthRatio;
        }

        // 나) 피격 감점 (내가 카운터 맞아서 피가 깎였을 때 벌점)
        if (self.CurrentHealthRatio < lastSelfHealth)
        {
            float damageTaken = lastSelfHealth - self.CurrentHealthRatio;
            AddReward(-damageTaken * 2.0f); // 무지성 딜교 패널티
            lastSelfHealth = self.CurrentHealthRatio;
        }

        // 다) 공격형 전용 타임 패널티 및 추격 가이드 보상
        if (distance <= 2.1f)
        {
            // 사거리 근처에서 적절히 대치 및 조준을 잘 유지하고 있다면 미세 보상 (빠른 추격 유도)
            AddReward(0.01f);
        }
        else
        {
            // 너무 멀리 도망쳐 다니면 공격성 제고를 위해 미세 감점
            AddReward(-0.002f);
        }

        // ---------------------------------------------------------------------
        // 5. 에피소드 종료 조건 처리 (End Conditions)
        // ---------------------------------------------------------------------
        if (opponent.IsDead)
        {
            // 상대 수비형 BT를 격파하고 승리 시 대량의 보상 부여
            SetReward(5.0f);
            EndEpisode();
        }
        else if (self.IsDead)
        {
            // 내가 카운터 맞아 사망 시 패배 처리
            SetReward(-3.0f);
            EndEpisode();
        }
    }

    #region 수학적 벡터 연산 헬퍼 함수군

    private Vector3 GetDirectionToTarget()
    {
        Vector3 offset = GetHorizontalOffsetToTarget();
        return offset.sqrMagnitude <= 0.0001f ? transform.forward : offset.normalized;
    }

    private Vector3 GetHorizontalOffsetToTarget()
    {
        if (opponent == null) return Vector3.zero;
        Vector3 offset = opponent.transform.position - transform.position;
        offset.y = 0f;
        return offset;
    }

    #endregion

    private void FillDefaultReferences()
    {
        if (self == null)
        {
            self = GetComponent<CombatCharacter>();
        }

        if (actionController == null)
        {
            actionController = GetComponent<CombatActionController>();
        }

        if (cooldownSystem == null)
        {
            cooldownSystem = GetComponent<CooldownSystem>();
        }

        if (episodeManager == null)
        {
            episodeManager = FindFirstObjectByType<EpisodeManager>();
        }
    }
}