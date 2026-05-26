using UnityEngine;

public class StudentBTStrategy : MonoBehaviour
{
    [SerializeField] private CombatCharacter self;
    [SerializeField] private CombatCharacter target;
    [SerializeField] private CombatActionController actionController;
    [SerializeField] private CooldownSystem cooldownSystem;

    private const float AttackRange = 1.6f;

    private BTNode root;

    private void Awake()
    {
        FillDefaultReferences();
        BuildTree();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    private void Update()
    {
        if (!CanTick()) return;
        root.Tick();
    }

    private void BuildTree()
    {
        // 언제 어떤 조건에서 공격할 것인가
        BTNode attackSequence = new SequenceNode(
            new ConditionNode(IsTargetNotBlocking), //block 애니메이션 재생 중이 아닐 때
            new ConditionNode(IsTargetNotInvincible), // 회피 중이 아닐 때
            new ConditionNode(IsReadyToStrike), // 공격 유효 사거리에 있을 때(1.6f 안)
            new ConditionNode(IsAttackReady), // 내 공격 쿨다운이 채워졌는지
            new ActionNode(FaceAndAttack) // 공격 명령
        );

        root = new SelectorNode(

            // [1] 반격 차단 [RandomSelectorNode 요건 충족]
            new SequenceNode(
                new ConditionNode(IsTargetAttacking),
                new ConditionNode(IsInCloseRange),
                new ConditionNode(IsAnyDefenseReady),
                new RandomSelectorNode(
                    new SequenceNode(
                        new ConditionNode(IsDodgeReady),
                        new ActionNode(DodgeForwardAndClose)),
                    new SequenceNode(
                        new ConditionNode(IsBlockReady),
                        new ActionNode(BlockIncoming)))),

            // [2] 무방비 상태일 시 공격
            new SequenceNode(
                new ConditionNode(IsInAttackRange),
                attackSequence),

            // [3] 상시 압박 추격 무빙 [ParallelNode 요건 충족]
            new ParallelNode(1, 2,
                new ActionNode(ApproachOrWait),
                new ActionNode(FaceTarget))
        );
    }

    // ── Condition 함수 ──────────────────────────────

    private bool IsDodgeReady()
    {
        return cooldownSystem != null && cooldownSystem.IsDodgeReady();
    }

    private bool IsBlockReady()
    {
        return cooldownSystem != null && cooldownSystem.IsBlockReady();
    }

    private bool IsAttackReady()
    {
        return cooldownSystem != null && cooldownSystem.IsAttackReady();
    }

    private bool IsAnyDefenseReady()
    {
        return cooldownSystem != null && (cooldownSystem.IsDodgeReady() || cooldownSystem.IsBlockReady());
    }

    private bool IsInAttackRange()
    {
        return DistanceToTarget() <= AttackRange;
    }

    private bool IsInCloseRange()
    {
        return DistanceToTarget() <= 2.0f;
    }

    private bool IsTargetAttacking()
    {
        CombatActionController tc = target.ActionController;
        return tc != null && tc.IsAttacking;
    }

    private bool IsTargetNotBlocking()
    {
        CombatActionController tc = target.ActionController;
        if (tc == null) return false;
        return !tc.IsBlocking;
    }

    private bool IsTargetNotInvincible()
    {
        CombatActionController tc = target.ActionController;
        if (tc == null) return false;
        return !tc.IsInvincible;
    }

    private bool IsReadyToStrike()
    {
        return DistanceToTarget() <= AttackRange && IsFacingTarget(45f);
    }

    private bool IsFacingTarget(float maxAngle)
    {
        Vector3 direction = DirectionToTarget();
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return Vector3.Angle(forward, direction) <= maxAngle;
    }

    // ── Action 함수 ──────────────────────────────

    private BTNodeStatus DodgeForwardAndClose()
    {
        actionController.Face(DirectionToTarget());
        actionController.Dodge(DirectionToTarget());
        return BTNodeStatus.Success;
    }

    private BTNodeStatus BlockIncoming()
    {
        actionController.Block(DirectionToTarget());
        return BTNodeStatus.Success;
    }

    private BTNodeStatus FaceAndAttack()
    {
        actionController.Face(DirectionToTarget());
        actionController.Attack();
        return BTNodeStatus.Success;
    }

    private BTNodeStatus FaceTarget()
    {
        actionController.Face(DirectionToTarget());
        return BTNodeStatus.Success;
    }

    private BTNodeStatus ApproachOrWait()
    {
        actionController.Move(DirectionToTarget());
        return BTNodeStatus.Success;
    }

    // ── 유틸 함수 ──────────────────────────────

    private bool CanTick()
    {
        return root != null && self != null && target != null && actionController != null && !self.IsDead && !target.IsDead;
    }

    private Vector3 DirectionToTarget()
    {
        if (target == null) return transform.forward;
        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= 0.0001f ? transform.forward : offset.normalized;
    }

    private float DistanceToTarget()
    {
        if (target == null) return float.MaxValue;
        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private void FillDefaultReferences()
    {
        if (self == null) self = GetComponent<CombatCharacter>();
        if (actionController == null) actionController = GetComponent<CombatActionController>();
        if (cooldownSystem == null) cooldownSystem = GetComponent<CooldownSystem>();
    }
}
