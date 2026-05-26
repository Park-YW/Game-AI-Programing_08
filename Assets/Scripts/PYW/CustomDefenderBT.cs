using UnityEngine;

[RequireComponent(typeof(CombatCharacter))]
[RequireComponent(typeof(CooldownSystem))]
[RequireComponent(typeof(CombatActionController))]
// BaselineDefenderBT 복제본. BuildTree를 override하여 BT를 커스텀할 수 있습니다.
public class CustomDefenderBT : MonoBehaviour
{
    [SerializeField] protected CombatCharacter self;
    [SerializeField] protected CombatCharacter target;
    [SerializeField] protected CombatActionController actionController;
    [SerializeField] protected CooldownSystem cooldownSystem;
    [SerializeField] protected float closeDistance = 2.0f;
    [SerializeField] protected float preferredDistance = 3.0f;
    [SerializeField] protected float lowHealthRatio = 0.3f;

    protected BTNode root;

    private enum BlockCounterPhase
    {
        Idle,
        Blocking
    }

    private BlockCounterPhase blockCounterPhase = BlockCounterPhase.Idle;

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
        if (!CanTick())
        {
            return;
        }

        if (actionController.IsBlocking)
        {
            actionController.UpdateRotationLock(GetDirectionToTarget());
        }

        root.Tick();
    }

    protected virtual void BuildTree()
    {
        root = new SelectorNode(
            // 1) 상대 공격 시 블락 → 블락 종료 후 공격
            new SequenceNode(
                new ConditionNode(CanStartOrContinueBlockCounter),
                new ActionNode(BlockThenAttack)),

            // 2) 블락 불가 + 공격 들어오면 닷지
            new SequenceNode(
                new ConditionNode(() => blockCounterPhase == BlockCounterPhase.Idle
                    && IsTargetAttacking()
                    && cooldownSystem != null
                    && !cooldownSystem.IsBlockReady()
                    && cooldownSystem.IsDodgeReady()),
                new ActionNode(DodgeAway)),

            // 3) 블락·닷지 둘 다 불가하면 거리 유지
            new SequenceNode(
                new ConditionNode(() => blockCounterPhase == BlockCounterPhase.Idle
                    && cooldownSystem != null
                    && !cooldownSystem.IsBlockReady()
                    && !cooldownSystem.IsDodgeReady()),
                new ActionNode(MaintainDistance)),
            new ActionNode(MaintainDistance));
    }

    protected bool CanTick()
    {
        return root != null
            && self != null
            && target != null
            && actionController != null
            && !self.IsDead
            && !target.IsDead;
    }

    protected bool ShouldDodge()
    {
        return self.CurrentHealthRatio <= lowHealthRatio
            && cooldownSystem != null
            && cooldownSystem.IsDodgeReady();
    }

    protected bool CanBlockIncomingAttack()
    {
        return IsTargetClose()
            && cooldownSystem != null
            && cooldownSystem.IsBlockReady()
            && (IsTargetAttacking() || IsTargetAttackReady());
    }

    protected bool CanDodgeCloseTarget()
    {
        return IsTargetClose()
            && cooldownSystem != null
            && cooldownSystem.IsDodgeReady();
    }

    protected bool CanCounterAttack()
    {
        return IsTargetClose()
            && cooldownSystem != null
            && cooldownSystem.IsAttackReady();
    }

    protected BTNodeStatus Block()
    {
        actionController.Block(GetDirectionToTarget());
        return BTNodeStatus.Success;
    }

    protected BTNodeStatus DodgeAway()
    {
        actionController.Face(GetDirectionToTarget());
        actionController.Dodge(-GetDirectionToTarget());
        return BTNodeStatus.Success;
    }

    protected BTNodeStatus Attack()
    {
        actionController.Face(GetDirectionToTarget());
        actionController.Attack();
        return BTNodeStatus.Success;
    }

    protected bool CanStartOrContinueBlockCounter()
    {
        if (cooldownSystem == null)
        {
            return false;
        }

        if (blockCounterPhase != BlockCounterPhase.Idle)
        {
            return true;
        }

        return IsTargetAttacking() && cooldownSystem.IsBlockReady();
    }

    protected BTNodeStatus BlockThenAttack()
    {
        if (blockCounterPhase == BlockCounterPhase.Idle)
        {
            if (!IsTargetAttacking() || !cooldownSystem.IsBlockReady())
            {
                return BTNodeStatus.Failure;
            }

            actionController.Block(GetDirectionToTarget());
            if (!actionController.IsBlocking)
            {
                return BTNodeStatus.Failure;
            }

            blockCounterPhase = BlockCounterPhase.Blocking;
            return BTNodeStatus.Running;
        }

        if (actionController.IsBlocking)
        {
            return BTNodeStatus.Running;
        }

        blockCounterPhase = BlockCounterPhase.Idle;
        actionController.Face(GetDirectionToTarget());
        actionController.Attack();
        return BTNodeStatus.Success;
    }

    protected BTNodeStatus MaintainDistance()
    {
        Vector3 offset = GetHorizontalOffsetToTarget();
        if (offset.magnitude < preferredDistance)
        {
            actionController.Move(-GetDirectionToTarget());
        }

        return BTNodeStatus.Success;
    }

    protected bool IsTargetClose()
    {
        return GetHorizontalOffsetToTarget().magnitude <= closeDistance;
    }

    protected bool IsInTargetAttackRange()
    {
        return GetHorizontalOffsetToTarget().magnitude <= closeDistance;
    }

    protected bool IsTargetAttacking()
    {
        CombatActionController targetAction = target.ActionController;
        return targetAction != null && targetAction.IsAttacking;
    }

    protected bool IsTargetAttackReady()
    {
        CooldownSystem targetCooldown = target.CooldownSystem;
        return targetCooldown != null && targetCooldown.IsAttackReady();
    }

    protected Vector3 GetDirectionToTarget()
    {
        Vector3 offset = GetHorizontalOffsetToTarget();
        return offset.sqrMagnitude <= 0.0001f ? transform.forward : offset.normalized;
    }

    protected Vector3 GetHorizontalOffsetToTarget()
    {
        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        return offset;
    }

    protected void FillDefaultReferences()
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
    }
}
