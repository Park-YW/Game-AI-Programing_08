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

    private const int MoveNone = 0;
    private const int MoveForward = 1;
    private const int MoveBackward = 2;
    private const int MoveLeft = 3;
    private const int MoveRight = 4;

    private const int SkillNone = 0;
    private const int SkillAttack = 1;
    private const int SkillBlock = 2;
    private const int SkillDodge = 3;

    private float lastSelfHealth = 1f;
    private float lastOpponentHealth = 1f;
    private bool isInitialized = false;

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

        if (self != null && opponent != null)
        {
            lastSelfHealth = self.CurrentHealthRatio;
            lastOpponentHealth = opponent.CurrentHealthRatio;
            isInitialized = true;
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (self == null || opponent == null || cooldownSystem == null) return;

        sensor.AddObservation(self.CurrentHealthRatio);
        sensor.AddObservation(opponent.CurrentHealthRatio);

        sensor.AddObservation(cooldownSystem.IsAttackReady() ? 1.0f : 0.0f);
        sensor.AddObservation(cooldownSystem.IsBlockReady() ? 1.0f : 0.0f);
        sensor.AddObservation(cooldownSystem.IsDodgeReady() ? 1.0f : 0.0f);

        Vector3 offset = GetHorizontalOffsetToTarget();
        float distance = offset.magnitude;
        sensor.AddObservation(Mathf.Clamp01(distance / 20.0f));

        Vector3 dirToTarget = distance <= 0.0001f ? transform.forward : offset.normalized;
        sensor.AddObservation(dirToTarget.x);
        sensor.AddObservation(dirToTarget.z);

        if (opponent.ActionController != null)
        {
            sensor.AddObservation(opponent.ActionController.IsBlocking ? 1.0f : 0.0f);
            sensor.AddObservation(opponent.ActionController.IsInvincible ? 1.0f : 0.0f);
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

        if (!isInitialized || lastSelfHealth <= 0.05f || lastOpponentHealth <= 0.05f)
        {
            lastSelfHealth = self.CurrentHealthRatio;
            lastOpponentHealth = opponent.CurrentHealthRatio;
            isInitialized = true;
            return;
        }

        int moveCommand = actions.DiscreteActions[0];
        int skillCommand = actions.DiscreteActions[1];

        Vector3 dirToTarget = GetDirectionToTarget();
        Vector3 leftDirection = Vector3.Cross(dirToTarget, Vector3.up).normalized;
        float distance = GetHorizontalOffsetToTarget().magnitude;

        actionController.Face(dirToTarget);

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

        if (skillCommand == SkillAttack && cooldownSystem.IsAttackReady())
        {
            if (opponent.ActionController != null && opponent.ActionController.IsBlocking)
            {
                AddReward(-1.5f);
            }
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
            actionController.Dodge(dirToTarget);
        }

        if (opponent.CurrentHealthRatio < lastOpponentHealth && lastOpponentHealth > 0.1f)
        {
            float damageDealt = lastOpponentHealth - opponent.CurrentHealthRatio;
            AddReward(damageDealt * 4.0f);
            lastOpponentHealth = opponent.CurrentHealthRatio;
        }

        if (self.CurrentHealthRatio < lastSelfHealth && lastSelfHealth > 0.1f)
        {
            float damageTaken = lastSelfHealth - self.CurrentHealthRatio;
            AddReward(-damageTaken * 2.0f);
            lastSelfHealth = self.CurrentHealthRatio;
        }

        // ---------------------------------------------------------------------
        // [강력한 추격 유도 보상 시스템으로 개조]
        // ---------------------------------------------------------------------
        if (distance <= 2.0f)
        {
            // 공격 유효 사거리 내로 진입 성공 시 매 프레임 파격적인 보상 부여 (가장 중요)
            AddReward(0.1f);
        }
        else
        {
            // 거리가 2.0f보다 멀리 떨어져 있으면 상시로 거대한 패널티 부여!
            // 이 패널티 때문에 AI는 가만히 서 있으면 점수가 계속 파멸적으로 깎이므로, 
            // 살기 위해서라도 무조건 전진(MoveForward)을 선택해 적에게 접근하게 됩니다.
            AddReward(-0.05f * distance);
        }
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

        if (Input.GetKey(KeyCode.W)) discreteActions[0] = MoveForward;
        else if (Input.GetKey(KeyCode.S)) discreteActions[0] = MoveBackward;
        else if (Input.GetKey(KeyCode.A)) discreteActions[0] = MoveLeft;
        else if (Input.GetKey(KeyCode.D)) discreteActions[0] = MoveRight;
        else discreteActions[0] = MoveNone;

        if (Input.GetKey(KeyCode.J)) discreteActions[1] = SkillAttack;
        else if (Input.GetKey(KeyCode.K)) discreteActions[1] = SkillBlock;
        else if (Input.GetKey(KeyCode.L)) discreteActions[1] = SkillDodge;
        else discreteActions[1] = SkillNone;
    }

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

    private void FillDefaultReferences()
    {
        if (self == null) self = GetComponent<CombatCharacter>();
        if (actionController == null) actionController = GetComponent<CombatActionController>();
        if (cooldownSystem == null) cooldownSystem = GetComponent<CooldownSystem>();

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

        if (episodeManager == null) episodeManager = FindFirstObjectByType<EpisodeManager>();
    }
}