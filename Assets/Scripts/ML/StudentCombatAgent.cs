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

    private const int SkillNone = 0;
    private const int SkillAttack = 1;
    private const int SkillBlock = 2;
    private const int SkillDodge = 3;

    private float previousSelfHp;
    private float previousOpponentHp;

    // 프레임 추적용 상태 기억 변수
    private bool wasBlockingLastFrame = false;

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
        FillDefaultReferences();

        wasBlockingLastFrame = false;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.Rebind();
        }

        if (self != null && opponent != null)
        {
            previousSelfHp = self.CurrentHealth;
            previousOpponentHp = opponent.CurrentHealth;
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (self == null || opponent == null || cooldownSystem == null) return;

        sensor.AddObservation(self.CurrentHealthRatio);
        sensor.AddObservation(opponent.CurrentHealthRatio);

        float distance = Vector3.Distance(transform.position, opponent.transform.position);
        sensor.AddObservation(distance);

        sensor.AddObservation(cooldownSystem.IsAttackReady() ? 1f : 0f);
        sensor.AddObservation(cooldownSystem.IsBlockReady() ? 1f : 0f);
        sensor.AddObservation(cooldownSystem.IsDodgeReady() ? 1f : 0f);

        sensor.AddObservation(opponent.ActionController.IsAttacking ? 1f : 0f);
        sensor.AddObservation(opponent.ActionController.IsBlocking ? 1f : 0f);
        sensor.AddObservation(opponent.ActionController.IsInvincible ? 1f : 0f);

        bool opponentVulnerable = !opponent.ActionController.IsAttacking &&
                                   !opponent.ActionController.IsBlocking &&
                                   !opponent.ActionController.IsInvincible;
        sensor.AddObservation(opponentVulnerable ? 1f : 0f);

        Vector3 dir = (opponent.transform.position - transform.position).normalized;
        sensor.AddObservation(dir.x);
        sensor.AddObservation(dir.z);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (self == null || opponent == null || actionController == null || cooldownSystem == null) return;
        if (self.IsDead) return;

        int combatAction = actions.DiscreteActions[0];
        int movementAction = actions.DiscreteActions[1];

        Vector3 toOpponent = (opponent.transform.position - transform.position).normalized;
        Vector3 moveDirection = Vector3.zero;

        // 이동 제어
        switch (movementAction)
        {
            case 1: moveDirection = toOpponent; break;
            case 2: moveDirection = -toOpponent; break;
            case 3: moveDirection = -transform.right; break;
            case 4: moveDirection = transform.right; break;
        }

        float distance = Vector3.Distance(transform.position, opponent.transform.position);

        if (moveDirection != Vector3.zero)
        {
            // [변경점 1]: 무지성 개돌 및 몸싸움 비비기 페널티 폭탄 상향 (-0.2 -> -1.0)
            if (distance <= 0.8f && movementAction == 1)
            {
                actionController.Move(-toOpponent); // 강제 백스텝 유도
                AddReward(-1.0f);
            }
            else
            {
                // [변경점 2]: 이미 사거리 내에 충분히 들어왔는데도 무작정 돌진(W)만 누르면 미세 감점 부여
                if (distance <= 1.8f && movementAction == 1)
                {
                    AddReward(-0.02f);
                }
                actionController.Move(moveDirection);
            }
        }

        // 현재 액션 시작 전 가드 상태 임시 백업
        bool currentBlockingFrame = actionController.IsBlocking;

        // 스킬 제어 루틴
        switch (combatAction)
        {
            case SkillAttack:
                if (cooldownSystem.IsAttackReady())
                {
                    if (opponent.ActionController.IsBlocking) AddReward(-0.2f);

                    if (distance > 2.1f)
                    {
                        AddReward(-0.3f);
                    }
                    else if (wasBlockingLastFrame)
                    {
                        AddReward(0.5f);
                    }

                    actionController.Face(toOpponent);
                    actionController.Attack();
                }
                break;

            case SkillBlock:
                if (cooldownSystem.IsBlockReady())
                {
                    if (opponent.ActionController.IsAttacking) AddReward(2.5f);
                    else AddReward(-0.6f);

                    actionController.Face(toOpponent);
                    actionController.Block(toOpponent);
                }
                break;

            case SkillDodge:
                if (cooldownSystem.IsDodgeReady())
                {
                    if (opponent.ActionController.IsAttacking) AddReward(1.5f);
                    else AddReward(-1.2f);

                    actionController.Dodge(-toOpponent);
                }
                break;
        }

        // 실시간 딜교환 및 피해 연산 정산 구역
        if (opponent.CurrentHealth < previousOpponentHp)
        {
            AddReward(1.0f);

            if (wasBlockingLastFrame)
            {
                AddReward(2.0f);
                if (transform.parent != null)
                {
                    Debug.Log($"[{transform.parent.name}] 🛡️⚔️ 가드 후 반격 적중 (보상 +2.0)");
                }
            }

            bool opponentVulnerable = !opponent.ActionController.IsAttacking &&
                                       !opponent.ActionController.IsBlocking &&
                                       !opponent.ActionController.IsInvincible;
            if (opponentVulnerable) AddReward(1.0f);
        }

        if (self.CurrentHealth < previousSelfHp)
        {
            if (wasBlockingLastFrame && !currentBlockingFrame)
            {
                AddReward(-4.0f);
            }
            else
            {
                AddReward(-3.0f);
            }
        }

        // [변경점 3]: 수비형 전용 '황금 사거리 대치' 유도 보상 전면 재정리
        if (opponent.CurrentHealthRatio > 0.3f)
        {
            if (distance >= 1.8f && distance <= 2.8f)
            {
                // 최적의 수비형 아웃복싱 거리 유지 시 숨쉬기 가산점 10배 상향 (+0.005 -> +0.05)
                if (actionController.IsBlocking && !opponent.ActionController.IsAttacking)
                {
                    AddReward(-0.02f); // 존버 방지
                }
                else
                {
                    AddReward(0.05f);
                }
            }
            else if (distance < 1.5f)
            {
                // 너무 바짝 붙어서 들이받으면 감점 주입
                AddReward(-0.05f);
            }
            else
            {
                // 너무 멀리 째면(도망가면) 거리 비례 감점
                AddReward(-0.02f * distance);
            }
        }

        // 다음 프레임을 위해 현재 상태 유기적 백업
        wasBlockingLastFrame = currentBlockingFrame;

        previousOpponentHp = opponent.CurrentHealth;
        previousSelfHp = self.CurrentHealth;

        if (opponent.IsDead)
        {
            AddReward(10.0f);
            EndEpisode();
        }

        if (self.IsDead)
        {
            AddReward(-10.0f);
            EndEpisode();
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActions = actionsOut.DiscreteActions;

        if (Input.GetKey(KeyCode.W)) discreteActions[1] = 1;
        else if (Input.GetKey(KeyCode.S)) discreteActions[1] = 2;
        else if (Input.GetKey(KeyCode.A)) discreteActions[1] = 3;
        else if (Input.GetKey(KeyCode.D)) discreteActions[1] = 4;
        else discreteActions[1] = 0;

        if (Input.GetKey(KeyCode.J)) discreteActions[0] = SkillAttack;
        else if (Input.GetKey(KeyCode.K)) discreteActions[0] = SkillBlock;
        else if (Input.GetKey(KeyCode.L)) discreteActions[0] = SkillDodge;
        else discreteActions[0] = SkillNone;
    }

    private void FillDefaultReferences()
    {
        if (self == null) self = GetComponent<CombatCharacter>();
        if (actionController == null) actionController = GetComponent<CombatActionController>();
        if (cooldownSystem == null) cooldownSystem = GetComponent<CooldownSystem>();
        if (episodeManager == null) episodeManager = FindFirstObjectByType<EpisodeManager>();

        if (opponent == null)
        {
            CombatCharacter[] characters = FindObjectsByType<CombatCharacter>(FindObjectsSortMode.None);
            foreach (var ch in characters)
            {
                if (ch.gameObject != this.gameObject)
                {
                    opponent = ch;
                    break;
                }
            }
        }
    }
}