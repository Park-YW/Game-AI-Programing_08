using System;
using Unity.VisualScripting;
using UnityEngine;

// Student template: replace BuildTree() with an attacker or defender BT strategy.
public class StudentBTStrategy : MonoBehaviour
{
    [SerializeField] private CombatCharacter self;
    [SerializeField] private CombatCharacter target;
    [SerializeField] private CombatActionController actionController;
    [SerializeField] private CooldownSystem cooldownSystem;

    // 거리 설정
    [SerializeField] private float attackDistance = 1.8f;
    [SerializeField] private float maintainDistance = 3.5f;
    [SerializeField] private float farDistance = 5.0f;

    private BTNode root;
    private Blackboard bb;

    public class Blackboard
    {
        public float TargetHealthRatio;
        public bool IsTargetAttacking;
        public bool IsTargetEvading;
        public bool IsTargetGuarding;
        public float DistanceToTarget;

        public enum ManeuverType { None, Left, Right, Back, Forward, Idle }
        public ManeuverType CurrentManeuver = ManeuverType.None;
    }

    private void Awake()
    {
        FillDefaultReferences();
        bb = new Blackboard();
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
        float lastChaseTime = -999f;
        float rollCatchTimer = 0f;

        var updateBBNode = new ActionNode(() =>
        {
            bb.TargetHealthRatio = target.CurrentHealthRatio;
            bb.IsTargetAttacking = target.ActionController != null && target.ActionController.IsAttacking;
            bb.IsTargetEvading = target.ActionController != null && target.ActionController.IsInvincible;
            bb.IsTargetGuarding = target.ActionController != null && target.ActionController.IsBlocking;
            bb.DistanceToTarget = GetHorizontalOffsetToTarget().magnitude;

            return BTNodeStatus.Success;
        });

        var brainNode = new SelectorNode(

            // [2-1. 위협 반응 (최우선 방어/회피)]
            new SequenceNode(
                new ConditionNode(() => bb.IsTargetAttacking),
                new SelectorNode(
                    new SequenceNode(
                        new ConditionNode(CanDodge),
                        new ActionNode(ActionDodgeBackward)
                    ),
                    new SequenceNode(
                        new ConditionNode(CanBlock),
                        new ActionNode(ActionBlock)
                    )
                )
            ),

            // [2-2. 처형 및 패닉 롤 캐치 로직]
            new SequenceNode(
                new ConditionNode(() => bb.TargetHealthRatio <= 0.3f),

                // [수정된 Decorator 1: 쿨타임 게이트 및 부가 효과]
                // ConditionNode의 성공(Success) 결과를 가로채어 타이머를 리셋하고 그대로 Success를 반환합니다.
                new DecoratorNode(
                    new ConditionNode(() => Time.time - lastChaseTime >= 2.0f),
                    (status) =>
                    {
                        if (status == BTNodeStatus.Success)
                        {
                            lastChaseTime = Time.time;
                            return BTNodeStatus.Success;
                        }
                        return BTNodeStatus.Failure;
                    }
                ),

                new SelectorNode(
                    // A. 구르기 캐치 (타이밍 조절 + 기본 공격)
                    new SequenceNode(

                        // [수정된 Decorator 2: Wait Timer (상태 변조)]
                        // 적이 회피 중이면(Success) 즉시 공격으로 넘어가지 않고 0.3초간 'Running'으로 변환하여 트리를 붙잡아둡니다.
                        new DecoratorNode(
                            new ConditionNode(() => bb.IsTargetEvading),
                            (status) =>
                            {
                                if (status == BTNodeStatus.Success)
                                {
                                    rollCatchTimer += Time.deltaTime;
                                    if (rollCatchTimer >= 0.3f)
                                    {
                                        rollCatchTimer = 0f;
                                        return BTNodeStatus.Success; // 타이머 완료 시 비로소 Success 반환 -> 다음 공격 노드 실행
                                    }
                                    return BTNodeStatus.Running; // 0.3초 전까지는 Running
                                }
                                rollCatchTimer = 0f; // 회피 중이 아니면 실패 및 타이머 초기화
                                return status;
                            }
                        ),
                        new ActionNode(ActionAttack) // 대기(Running)가 끝나고 Success가 반환되면 실행됨
                    ),

                    // B. 회피 유도 압박 (기본 이동과 대기의 무작위 조합)
                    new SequenceNode( // 압박 로직을 묶기 위한 Sequence
                                      // 1단계: 방향 결정
                        new SelectorNode(
                            new SequenceNode(
                                new ConditionNode(() => bb.CurrentManeuver != Blackboard.ManeuverType.None),
                                new ActionNode(() => BTNodeStatus.Success)
                            ),
                            new RandomSelectorNode(
                                new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Forward; return BTNodeStatus.Success; }),
                                new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Idle; return BTNodeStatus.Success; }),
                                new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Left; return BTNodeStatus.Success; }),
                                new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Right; return BTNodeStatus.Success; })
                            )
                        ),
                        // 2단계: 실행 및 상태 검증 (Parallel)
                        new DecoratorNode(
                            new ParallelNode(1, 1,
                                // 적이 공격하거나 회피를 시작하면 즉시 압박 기동 취소
                                new ConditionNode(() => bb.TargetHealthRatio <= 0.3f && !bb.IsTargetAttacking && !bb.IsTargetEvading),
                                new ActionNode(() =>
                                {
                                    if (bb.CurrentManeuver == Blackboard.ManeuverType.Forward) ActionMoveIn();
                                    else if (bb.CurrentManeuver == Blackboard.ManeuverType.Idle) ActionIdle();
                                    else if (bb.CurrentManeuver == Blackboard.ManeuverType.Left) ActionSideStepLeft();
                                    else if (bb.CurrentManeuver == Blackboard.ManeuverType.Right) ActionSideStepRight();

                                    return BTNodeStatus.Running; // 흐름 유지
                                })
                            ),
                            (status) =>
                            {
                                if (status != BTNodeStatus.Running) bb.CurrentManeuver = Blackboard.ManeuverType.None;
                                return status;
                            }
                        )
                    )
                )
            ),

            // [2-3. 일반 공격]
            new SequenceNode(
                new ConditionNode(CanAttackCooldown),
                new ConditionNode(() => bb.DistanceToTarget <= attackDistance),

                // [수정된 Decorator 3: Invert (결과 반전)]
                // 가드 중인지 확인한 결과를 반전시킵니다. (가드 중이면 Failure, 아니면 Success)
                new DecoratorNode(
                    new ConditionNode(() => bb.IsTargetGuarding),
                    (status) => status == BTNodeStatus.Success ? BTNodeStatus.Failure : BTNodeStatus.Success
                ),
                new DecoratorNode(
                    new ConditionNode(() => bb.IsTargetEvading),
                    (status) => status == BTNodeStatus.Success ? BTNodeStatus.Failure : BTNodeStatus.Success
                ),

                new ActionNode(ActionAttack)
            ),

            // [2-4. 거리 조절 (아웃복싱 기동)]
            new SelectorNode(
                new SequenceNode(
                    new ConditionNode(() => bb.DistanceToTarget > farDistance),
                    new ActionNode(ActionMoveIn)
                ),
                new SequenceNode(
                    new ConditionNode(() => bb.DistanceToTarget <= maintainDistance),

                    // 1단계: 방향 결정 (CurrentManeuver가 None일 때만 RandomSelector 실행)
                    new SelectorNode(
                        // 이미 방향이 정해져 있다면 무시하고 다음으로 넘어감
                        new SequenceNode(
                            new ConditionNode(() => bb.CurrentManeuver != Blackboard.ManeuverType.None),
                            new ActionNode(() => BTNodeStatus.Success)
                        ),
                        // 방향이 정해져 있지 않다면 난수로 하나를 선택하여 BB에 저장
                        new RandomSelectorNode(
                            new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Left; return BTNodeStatus.Success; }),
                            new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Right; return BTNodeStatus.Success; }),
                            new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Back; return BTNodeStatus.Success; })
                        )
                    ),

                    // 2단계: Parallel 검증 및 이동 실행 (Until Fail)
                    // Decorator를 씌워 Parallel이 종료될 때 상태를 다시 None으로 초기화합니다.
                    new DecoratorNode(
                        new ParallelNode(1, 1, // Success 임계값 1, Fail 임계값 1

                            // [검증 조건] 이 조건이 Fail을 반환하는 순간 Parallel 전체가 즉시 Fail로 종료됨
                            new ConditionNode(() =>
                                // maintainDistance(3.5f)에 진입했더라도, 빠져나갈 때는 약간의 여유(예: + 0.3f)를 주어 경계선 떨림 방지
                                bb.DistanceToTarget <= maintainDistance + 0.3f &&
                                !bb.IsTargetAttacking
                            ),

                            // [이동 실행] Blackboard에 저장된 방향으로 이동
                            new ActionNode(() =>
                            {
                                if (bb.CurrentManeuver == Blackboard.ManeuverType.Left) ActionSideStepLeft();
                                else if (bb.CurrentManeuver == Blackboard.ManeuverType.Right) ActionSideStepRight();
                                else if (bb.CurrentManeuver == Blackboard.ManeuverType.Back) ActionMoveBack();

                                // 이동하면서 항상 Running을 반환하여 트리의 흐름을 이곳에 유지
                                return BTNodeStatus.Running;
                            }
                            )
                        ),
                        (status) =>
                        {
                            // Parallel이 Success나 Fail로 끝났다면(조건 불만족 등), 방향 상태를 초기화
                            if (status != BTNodeStatus.Running)
                            {
                                bb.CurrentManeuver = Blackboard.ManeuverType.None;
                            }
                            return status;
                        }
                    )
                )
            ),
            // [2-5. 기본 대기]
            new ActionNode(ActionIdle)
        );

        root = new ParallelNode(1, 1, updateBBNode, brainNode);

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


    private bool CanDodge() => cooldownSystem != null && cooldownSystem.IsDodgeReady();
    private bool CanBlock() => cooldownSystem != null && cooldownSystem.IsBlockReady();
    private bool CanAttackCooldown() => cooldownSystem != null && cooldownSystem.IsAttackReady();

    private BTNodeStatus ActionDodgeBackward()
    {
        actionController.Face(GetDirectionToTarget());
        actionController.Dodge(-GetDirectionToTarget());
        return BTNodeStatus.Success;
    }

    private BTNodeStatus ActionBlock()
    {
        actionController.Block(GetDirectionToTarget());
        return BTNodeStatus.Success;
    }

    private BTNodeStatus ActionAttack()
    {
        actionController.Face(GetDirectionToTarget());
        actionController.Attack();
        return BTNodeStatus.Success;
    }

    private BTNodeStatus ActionMoveIn()
    {
        actionController.Move(GetDirectionToTarget());
        return BTNodeStatus.Success;
    }

    private BTNodeStatus ActionMoveBack()
    {
        actionController.Move(-GetDirectionToTarget());
        return BTNodeStatus.Success;
    }

    private BTNodeStatus ActionSideStepLeft()
    {
        Vector3 leftDir = Vector3.Cross(Vector3.up, GetDirectionToTarget()).normalized;
        actionController.Move(leftDir);
        return BTNodeStatus.Success;
    }

    private BTNodeStatus ActionSideStepRight()
    {
        Vector3 rightDir = Vector3.Cross(GetDirectionToTarget(), Vector3.up).normalized;
        actionController.Move(rightDir);
        return BTNodeStatus.Success;
    }

    private BTNodeStatus ActionIdle()
    {
        // 타겟을 향해 몸을 돌리며 대기
        if (!actionController.IsAttacking && !actionController.IsInvincible)
        {
            actionController.UpdateRotationLock(GetDirectionToTarget());
        }
        return BTNodeStatus.Success;
    }

    private Vector3 GetDirectionToTarget()
    {
        Vector3 offset = GetHorizontalOffsetToTarget();
        return offset.sqrMagnitude <= 0.0001f ? transform.forward : offset.normalized;
    }

    private Vector3 GetHorizontalOffsetToTarget()
    {
        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        return offset;
    }

    BTNode CreateSustainedAction(Func<BTNodeStatus> actionFunc, float duration)
    {
        float timer = 0f;
        return new DecoratorNode(
            new ActionNode(actionFunc),
            (status) =>
            {
                // ActionNode(이동 명령)는 매 프레임 실행됨
                timer += Time.deltaTime;
                if (timer >= duration)
                {
                    timer = 0f; // 타이머 초기화
                    return BTNodeStatus.Success; // 지정된 시간이 끝나면 비로소 Success
                }
                return BTNodeStatus.Running; // 그 전까지는 트리의 흐름을 이곳에 붙잡아둠 (Running)
            }
        );
    }
}
