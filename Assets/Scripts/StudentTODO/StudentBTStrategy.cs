using UnityEngine;
using static UnityEngine.GraphicsBuffer;

// Student template: replace BuildTree() with an attacker or defender BT strategy.
public class StudentBTStrategy : MonoBehaviour
{
    [SerializeField] private CombatCharacter self;
    [SerializeField] private CombatCharacter target;
    [SerializeField] private CombatActionController actionController;
    [SerializeField] private CooldownSystem cooldownSystem;

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
        if (!CanTick())
        {
            return;
        }

        root.Tick();
    }

    private void BuildTree()
    {
        // TODO: Choose an attacker or defender role.
        // TODO: Build a root SelectorNode or SequenceNode.
        // TODO: Add ConditionNode objects for health, distance, cooldown, and facing checks.
        // TODO: Add ActionNode objects that call only:
        // actionController.Move(direction), Attack(), Block(), or Dodge(direction).
        // TODO: Include at least two advanced elements in your final strategy:
        // DecoratorNode, ParallelNode, RandomSelectorNode, or another non-deterministic choice.
        root = new SelectorNode(



        new DecoratorNode(
            new SequenceNode(
                new ConditionNode(ShouldDodge),
                new ActionNode(DodgeAway)
            ),
            status => status
        ),

        new SequenceNode(
            new ConditionNode(IsInAttackRange),

            new SelectorNode(

                new SequenceNode(
                    new ConditionNode(IsOpponentAttacking),
                    new ActionNode(BlockTarget)
                ),

                new SequenceNode(
                    new ConditionNode(IsOpponentBlocking),
                    new ActionNode(MoveTowardTarget)
                ),

                new ActionNode(() =>
                {
                    if (Random.value < 0.8f)
                    {
                        return AttackTarget();
                    }

                    return BTNodeStatus.Failure;
                })
            )
        ),

        new ActionNode(MoveTowardTarget)
    );
    }

    private bool CanTick()
    {
        return root != null
            && self != null
            && target != null
            && actionController != null
            && !self.IsDead
            && !target.IsDead;
    }

    private bool ShouldDodge()
    {
        return self.CurrentHealthRatio <= 0.3f
            && cooldownSystem != null
            && cooldownSystem.IsDodgeReady();
    }

    private bool IsInAttackRange()
    {
        return DistanceToTarget() <= 1.6f
            && IsFacingTarget(45f);
    }

    private BTNodeStatus DodgeAway()
    {
        actionController.Face(DirectionToTarget());
        actionController.Dodge(-DirectionToTarget());
        return BTNodeStatus.Success;
    }

    private BTNodeStatus AttackTarget()
    {
        if (cooldownSystem != null && cooldownSystem.IsAttackReady())
        {
            actionController.Face(DirectionToTarget());
            actionController.Attack();
            return BTNodeStatus.Success;
        }

        return BTNodeStatus.Failure;
    }

    private BTNodeStatus BlockTarget()
    {
        if (cooldownSystem != null && cooldownSystem.IsBlockReady())
        {
            actionController.Face(DirectionToTarget());
            actionController.Block();
            return BTNodeStatus.Success;
        }

        return BTNodeStatus.Failure;
    }



    private BTNodeStatus MoveTowardTarget()
    {
        actionController.Move(DirectionToTarget());
        return BTNodeStatus.Success;
    }



    private BTNodeStatus FaceTarget()
    {
        actionController.Face(DirectionToTarget());
        return BTNodeStatus.Success;
    }

    private bool IsOpponentAttacking()
    {
        return target != null
            && target.ActionController != null
            && target.ActionController.IsAttacking;
    }

    private bool IsOpponentBlocking()
    {
        return target != null
            && target.ActionController != null
            && target.ActionController.IsBlocking;
    }

    private Vector3 DirectionToTarget()
    {
        if (target == null)
        {
            return transform.forward;
        }

        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= 0.0001f ? transform.forward : offset.normalized;
    }

    private float DistanceToTarget()
    {
        if (target == null)
        {
            return float.MaxValue;
        }

        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private bool IsFacingTarget(float maxAngle)
    {
        Vector3 direction = DirectionToTarget();
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return Vector3.Angle(forward, direction) <= maxAngle;
    }

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
    }
}

