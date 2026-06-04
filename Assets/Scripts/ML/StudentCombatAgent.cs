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

    // ■ PDF 25p 가이드라인 지정 이산형 액션 맵 상수 설정
    // Branch 0: 동서남북 이동 제어 (Size 5)
    private const int MoveNone = 0;
    private const int MoveForward = 1;
    private const int MoveBackward = 2;
    private const int MoveLeft = 3;
    private const int MoveRight = 4;

    // Branch 1: 전투 행동 제어 (Size 4)
    private const int SkillNone = 0;
    private const int SkillAttack = 1;
    private const int SkillBlock = 2;
    private const int SkillDodge = 3;

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
        if (self != null && opponent != null)
        {
            lastSelfHealth = self.CurrentHealthRatio;
            lastOpponentHealth = opponent.CurrentHealthRatio;
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (self == null || opponent == null || cooldownSystem == null) return;

        // ■ PDF 24p 가이드라인: 체력, 상대 위치, 거리, 쿨타임 등을 0~1 범위로 정규화하여 수집
        // [총 11차원 Vector Observation 수집] -> 인스펙터 Space Size = 11 고정
        sensor.AddObservation(self.CurrentHealthRatio);      // 내 체력 (0~1)
        sensor.AddObservation(opponent.CurrentHealthRatio);  // 상대 체력 (0~1)

        sensor.AddObservation(cooldownSystem.IsAttackReady() ? 1.0f : 0.0f);
        sensor.AddObservation(cooldownSystem.IsBlockReady() ? 1.0f : 0.0f);
        sensor.AddObservation(cooldownSystem.IsDodgeReady() ? 1.0f : 0.0f);

        // 상대적인 방향 벡터 및 거리 정보 정규화 수집
        Vector3 offset = GetHorizontalOffsetToTarget();
        float distance = offset.magnitude;
        sensor.AddObservation(Mathf.Clamp01(distance / 20.0f)); // 거리 최대 20유닛 기준 정규화

        Vector3 dirToTarget = distance <= 0.0001f ? transform.forward : offset.normalized;
        sensor.AddObservation(dirToTarget.x); // 방향 X (-1~1)
        sensor.AddObservation(dirToTarget.z); // 방향 Z (-1~1)

        // 상대방 수비형 BT의 실시간 액션 상태 파악
        if (opponent.ActionController != null)
        {
            sensor.AddObservation(opponent.ActionController.IsBlocking ? 1.0f : 0.0f);
            sensor.AddObservation(opponent.ActionController.IsAttacking ? 1.0f : 0.0f);
        }
        else
        {
            sensor.AddObservation(0.0f);
            sensor.AddObservation(0.0f);
        }
        sensor.AddObservation(actionController.IsBlocking || actionController.IsAttacking ? 1.0f : 0.0f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (self == null || opponent == null || actionController == null || cooldownSystem == null) return;
        if (self.IsDead) return;

        // ■ PDF 25p: Branch 0과 Branch 1 값을 차례대로 수신
        int moveCommand = actions.DiscreteActions[0];
        int skillCommand = actions.DiscreteActions[1];

        Vector3 dirToTarget = GetDirectionToTarget();
        Vector3 leftDirection = Vector3.Cross(dirToTarget, Vector3.up).normalized; // 측면 왼쪽
        float distance = GetHorizontalOffsetToTarget().magnitude;

        // 상시 타겟 조준 고정
        actionController.Face(dirToTarget);

        // ---------------------------------------------------------------------
        // [Action 파트] Branch 0 : PDF 25p 규칙 기반 동서남북 기동 변환
        // ---------------------------------------------------------------------
        if (!actionController.IsBlocking)
        {
            switch (moveCommand)
            {
                case MoveForward:
                    actionController.Move(dirToTarget);
                    break;
                case MoveBackward:
                    actionController.Move(-dirToTarget);
                    break;
                case MoveLeft:
                    actionController.Move(leftDirection);
                    break;
                case MoveRight:
                    actionController.Move(-leftDirection);
                    break;
                default:
                    actionController.Move(Vector3.zero);
                    break;
            }
        }
        else
        {
            actionController.Move(Vector3.zero);
        }

        // ---------------------------------------------------------------------
        // [Action 파트] Branch 1 : 전투 행동 제어
        // ---------------------------------------------------------------------
        if (skillCommand == SkillAttack && cooldownSystem.IsAttackReady())
        {
            // [Reward 파트] 가이드라인 기반 보상/패널티 설계
            if (opponent.ActionController != null && opponent.ActionController.IsBlocking)
            {
                AddReward(-1.5f); // 방패 들이받기 패널티
            }
            else if (distance > 2.0f)
            {
                AddReward(-0.2f); // 허공 칼질 감점
            }
            actionController.Attack();
        }
        else if (skillCommand == SkillBlock && cooldownSystem.IsBlockReady())
        {
            actionController.Block(dirToTarget);
        }
        else if (skillCommand == SkillDodge && cooldownSystem.IsDodgeReady())
        {
            actionController.Dodge(dirToTarget);
        }

        // ---------------------------------------------------------------------
        // [Reward 파트] 실시간 점수 누적 연산
        // ---------------------------------------------------------------------
        if (opponent.CurrentHealthRatio < lastOpponentHealth)
        {
            float damageDealt = lastOpponentHealth - opponent.CurrentHealthRatio;
            AddReward(damageDealt * 4.0f); // 타격 성공 양수 보상
            lastOpponentHealth = opponent.CurrentHealthRatio;
        }

        if (self.CurrentHealthRatio < lastSelfHealth)
        {
            float damageTaken = lastSelfHealth - self.CurrentHealthRatio;
            AddReward(-damageTaken * 2.0f); // 피격 음수 보상
            lastSelfHealth = self.CurrentHealthRatio;
        }

        // 대치 유지 유도 보상
        if (distance <= 2.1f) AddReward(0.01f);
        else AddReward(-0.002f);

        // 에피소드 종료 조건 판정
        if (opponent.IsDead)
        {
            SetReward(5.0f);
            EndEpisode();
        }
        else if (self.IsDead)
        {
            SetReward(-3.0f);
            EndEpisode();
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActions = actionsOut.DiscreteActions;

        // 키보드 방향키 조작 매핑 (W, S, A, D)
        if (Input.GetKey(KeyCode.W)) discreteActions[0] = MoveForward;
        else if (Input.GetKey(KeyCode.S)) discreteActions[0] = MoveBackward;
        else if (Input.GetKey(KeyCode.A)) discreteActions[0] = MoveLeft;
        else if (Input.GetKey(KeyCode.D)) discreteActions[0] = MoveRight;
        else discreteActions[0] = MoveNone;

        // 마우스 클릭 및 키 조작 매핑 (J, K, L)
        if (Input.GetKey(KeyCode.J)) discreteActions[1] = SkillAttack;
        else if (Input.GetKey(KeyCode.K)) discreteActions[1] = SkillBlock;
        else if (Input.GetKey(KeyCode.L)) discreteActions[1] = SkillDodge;
        else discreteActions[1] = SkillNone;
    }

    #region 벡터 연산 헬퍼 함수

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
        if (self == null) self = GetComponent<CombatCharacter>();
        if (actionController == null) actionController = GetComponent<CombatActionController>();
        if (cooldownSystem == null) cooldownSystem = GetComponent<CooldownSystem>();
        if (episodeManager == null) episodeManager = FindFirstObjectByType<EpisodeManager>();
    }
}