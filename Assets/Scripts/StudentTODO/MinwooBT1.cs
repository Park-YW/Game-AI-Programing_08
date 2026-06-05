using System;
using Unity.VisualScripting;
using UnityEngine;

// Student template: replace BuildTree() with an attacker or defender BT strategy.
public class MinwooBTStrategy : MonoBehaviour
{
    [SerializeField] private CombatCharacter self;
    [SerializeField] private CombatCharacter target;
    [SerializeField] private CombatActionController actionController;
    [SerializeField] private CooldownSystem cooldownSystem;

    // 거리 설정
    [SerializeField] private float attackDistance = 1.8f;
    [SerializeField] private float maintainDistance = 3.5f;

    private BTNode root;
    private Blackboard bb;

    public class Blackboard
    {
        // 타겟 상태 정보
        public float TargetHealthRatio;
        public bool IsTargetAttacking;
        public bool IsTargetEvading;
        public bool IsTargetGuarding;
        public float DistanceToTarget;

        // 방향 상태
        public enum ManeuverType { None, Left, Right, Back, Forward, Idle }
        public ManeuverType CurrentManeuver = ManeuverType.None;

        public float ManeuverEndTime = 0f;
        public float NextChaseTime = 0f;
        public float RollCatchEndTime = 0f;

        // 상대 상태 확인용 뇌
        public bool WasTargetAttacking = false; // 이전 프레임 공격 여부
        public float TargetAttackStartTime = 0f;      // 적이 공격을 시작한 시간

        public bool WasTargetEvading = false;  // 이전 프레임 회피 여부
        public float TargetEvadeStartTime = 0f;      // 적이 회피를 시작한 시간

        public bool WasTargetGuarding = false; // 이전 프레임 방어 여부
        public float TargetGuardStartTime = 0f;      // 적이 방어를 시작한 시간

        // 적의 예상 공격 쿨타임
        public float EstimatedAttackCooldown = 2.0f;
        public float EstimatedEvadeCooldown = 2.0f;
        public float EstimatedGuardCooldown = 2.0f;

        // 계산한 적의 쿨타임 종료 시점
        public float TargetAttackVulnerableUntil;
        public float TargetEvadeVulnerableUntil;
        public float TargetGuardVulnerableUntil;
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
        // ---------------------------------------------------------
        // 공통 노드: Blackboard 상태 업데이트 (매 프레임 실행)
        // ---------------------------------------------------------
        var updateBBNode = new ActionNode(() =>
        {
            // 이전 프레임 상태 저장
            bb.WasTargetAttacking = bb.IsTargetAttacking;
            bb.WasTargetEvading = bb.IsTargetEvading;
            bb.WasTargetGuarding = bb.IsTargetGuarding;

            // 현재 타겟 체력 업데이트
            bb.TargetHealthRatio = target.CurrentHealthRatio;

            // 타겟 행동 상태 업데이트
            bb.IsTargetAttacking = target.ActionController != null && target.ActionController.IsAttacking;
            bb.IsTargetEvading = target.ActionController != null && target.ActionController.IsInvincible;
            bb.IsTargetGuarding = target.ActionController != null && target.ActionController.IsBlocking;
            bb.DistanceToTarget = GetHorizontalOffsetToTarget().magnitude;

            if (bb.IsTargetAttacking && !bb.WasTargetAttacking)
            {
                bb.TargetAttackVulnerableUntil = Time.time + bb.EstimatedAttackCooldown;
            }

            if (bb.IsTargetEvading && !bb.WasTargetEvading)
            {
                bb.TargetEvadeVulnerableUntil = Time.time + bb.EstimatedEvadeCooldown;
            }

            if (bb.IsTargetGuarding && !bb.WasTargetGuarding)
            {
                bb.TargetGuardVulnerableUntil = Time.time + bb.EstimatedEvadeCooldown;
            }


            // 다음 프레임 비교를 위해 현재 상태를 과거로 덮어쓰기
            bb.WasTargetAttacking = bb.IsTargetAttacking;
            bb.WasTargetEvading = bb.IsTargetEvading;
            bb.WasTargetGuarding = bb.IsTargetGuarding;

            return BTNodeStatus.Success;
        });

        // =========================================================
        // [1] Threat Response (위협 반응) + 반격
        // =========================================================


        // 가드 성공 후 연계 카운터 패턴 (RandomSelectorNode)
        BTNode postGuardCounter = new RandomSelectorNode(
            new SequenceNode(
                new ConditionNode(CanAttackCooldown),
                new ActionNode(() => {
                    actionController.Move(Vector3.zero);
                    actionController.Face(GetDirectionToTarget());
                    actionController.Attack();
                    return BTNodeStatus.Success;
                })
            ),
            new SequenceNode(
                new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsDodgeReady()),
                new ActionNode(() => {
                    Vector3 sideDirection = (Vector3.Cross(GetDirectionToTarget(), Vector3.up) + GetDirectionToTarget()).normalized;
                    actionController.Face(GetDirectionToTarget());
                    actionController.Dodge(sideDirection);
                    return BTNodeStatus.Success;
                }),
                new ConditionNode(CanAttackCooldown),
                new ActionNode(ActionAttack)
            )
        );


        BTNode Counter = new SequenceNode(
                new ConditionNode(CanBlock),
                new ActionNode(() =>
                {
                    actionController.Block(GetDirectionToTarget());
                    return BTNodeStatus.Success;
                }),
                postGuardCounter
        );

        var threatResponseSeq = new SequenceNode(
            new ConditionNode(() => bb.IsTargetAttacking),
            new SelectorNode(
                new SequenceNode(
                    new ConditionNode(CanBlock),
                    Counter
                ),
                new SequenceNode(
                    new ConditionNode(CanBlock),
                    new ActionNode(ActionBlock)
                ),
                new SequenceNode(
                    new ConditionNode(() =>
                    { 
                        if(bb.TargetAttackVulnerableUntil - Time.time > 1.54f)
                        {
                            return true; // 공격이 곧 끝날 것 같으면 회피하지 않도록
                        }
                        return false;
                    }),
                    new ActionNode(ActionDodgeBackward)
                )
            )
        );

        // =========================================================
        // [2] Execute & Pressure (처형 및 압박)
        // 타겟의 체력이 30% 이하일 때 발동. 구르기를 캐치하거나 압박 기동.
        // =========================================================


        var executeAndPressureSeq = new SequenceNode(
            new ConditionNode(() => bb.TargetHealthRatio <= 0.3f),
            new ConditionNode(() => Time.time >= bb.NextChaseTime),

            new SelectorNode(
                // -------------------------------------------------
                // [A. 구르기 캐치 (Roll Catch)]
                // -------------------------------------------------
                new SequenceNode(
                    new ConditionNode(() => bb.IsTargetEvading),
                    new ActionNode(() => {
                        bb.RollCatchEndTime = Time.time + 0.3f;
                        return BTNodeStatus.Success;
                    }),

                    // ParallelNode를 이용해 0.3초 대기 혹은 몹시 급한 위협까지 계속 타겟 추적하며 대기 (Running 반환)
                    new ParallelNode(1, 2,
                        new ConditionNode(() => bb.IsTargetAttacking || Time.time >= bb.RollCatchEndTime),
                        new ActionNode(() => {
                            actionController.UpdateRotationLock(GetDirectionToTarget());
                            return BTNodeStatus.Running;
                        })
                    ),

                    // 대기 종료 시 위협에 의한 종료가 아니었다면 공격
                    new ActionNode(() => {
                        if (!bb.IsTargetAttacking)
                        {
                            ActionAttack();
                            bb.NextChaseTime = Time.time + 2.0f;
                        }
                        return BTNodeStatus.Success;
                    })
                ),

                // -------------------------------------------------
                // [B. 랜덤 압박 기동 (Pressure)]
                // -------------------------------------------------
                new SequenceNode(
                    new RandomSelectorNode(
                        new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Forward; return BTNodeStatus.Success; }),
                        new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Left; return BTNodeStatus.Success; }),
                        new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Right; return BTNodeStatus.Success; })
                    ),
                    new ActionNode(() => {
                        bb.ManeuverEndTime = Time.time + UnityEngine.Random.Range(1.0f, 1.5f);
                        bb.NextChaseTime = bb.ManeuverEndTime;
                        return BTNodeStatus.Success;
                    }),

                    // 조건 만족 시까지 계속 반복하여 기동 (Running 반환)
                    new ParallelNode(1, 2,
                        new ConditionNode(() => bb.IsTargetAttacking || Time.time >= bb.ManeuverEndTime),
                        new ActionNode(() => {
                            if (bb.CurrentManeuver == Blackboard.ManeuverType.Left) ActionSideStepLeft();
                            else if (bb.CurrentManeuver == Blackboard.ManeuverType.Right) ActionSideStepRight();
                            else ActionMoveIn();
                            return BTNodeStatus.Running;
                        })
                    )
                )
            )
        );

        // =========================================================
        // [3] Normal Attack (일반 공격)
        // =========================================================
        var normalAttackSeq = new SequenceNode(
            new ConditionNode(() => bb.DistanceToTarget <= attackDistance),
            new ConditionNode(CanAttackCooldown),

            // 적이 가드/회피 쿨타임으로 인해 무방비 상태라면 공격
            new ConditionNode(() =>
                (!bb.IsTargetGuarding && !bb.IsTargetEvading) ||
                (bb.TargetGuardVulnerableUntil - Time.time > 0.3f && bb.TargetEvadeVulnerableUntil - Time.time > 0.3f)
            ),

            new ActionNode(ActionAttack)
        );

        // =========================================================
        // [4] Maneuver (적과의 거리에 따른 대응 기동)
        // =========================================================
        var maneuverSeq = new SelectorNode(
            // [A. 거리가 너무 멀 때 -> 거리를 좁히기 (전진)]
            new SequenceNode(
                new ConditionNode(() => bb.DistanceToTarget > maintainDistance),
                new ActionNode(() => {
                    bb.CurrentManeuver = Blackboard.ManeuverType.Forward;
                    bb.ManeuverEndTime = Time.time + UnityEngine.Random.Range(0.5f, 1.0f);
                    return BTNodeStatus.Success;
                }),
                new ParallelNode(1, 2,
                    new ConditionNode(() => bb.IsTargetAttacking || Time.time >= bb.ManeuverEndTime || bb.DistanceToTarget <= maintainDistance),
                    new ActionNode(() => { ActionMoveIn(); return BTNodeStatus.Running; })
                )
            ),

            // [B. 거리가 적당할 때 -> 거리 유지 및 견제 (좌/우 사이드스텝 또는 제자리 견제)]
            new SequenceNode(
                new ConditionNode(() => bb.DistanceToTarget >= attackDistance && bb.DistanceToTarget <= maintainDistance),
                new RandomSelectorNode(
                    new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Left; return BTNodeStatus.Success; }),
                    new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Right; return BTNodeStatus.Success; }),
                    new ActionNode(() => { bb.CurrentManeuver = Blackboard.ManeuverType.Idle; return BTNodeStatus.Success; }) // 제자리 유지
                ),
                new ActionNode(() => {
                    bb.ManeuverEndTime = Time.time + UnityEngine.Random.Range(0.5f, 1.2f);
                    return BTNodeStatus.Success;
                }),
                new ParallelNode(1, 2,
                    new ConditionNode(() => {
                        if (bb.IsTargetAttacking) return true;
                        if (Time.time >= bb.ManeuverEndTime) return true;
                        // 기동 중 거리가 적정 범위를 벗어나면 재평가를 위해 종료
                        if (bb.DistanceToTarget < attackDistance || bb.DistanceToTarget > maintainDistance) return true;
                        return false;
                    }),
                    new ActionNode(() => {
                        if (bb.CurrentManeuver == Blackboard.ManeuverType.Left) ActionSideStepLeft();
                        else if (bb.CurrentManeuver == Blackboard.ManeuverType.Right) ActionSideStepRight();
                        else ActionIdle(); // 정지 상태로 타겟 주시
                        return BTNodeStatus.Running;
                    })
                )
            ),

            // [C. 거리가 너무 가까울 때 -> 거리 벌리기 (후퇴)]
            new SequenceNode(
                new ConditionNode(() => bb.DistanceToTarget < attackDistance),
                new ActionNode(() => {
                    bb.CurrentManeuver = Blackboard.ManeuverType.Back;
                    bb.ManeuverEndTime = Time.time + UnityEngine.Random.Range(0.5f, 1.0f);
                    return BTNodeStatus.Success;
                }),
                new ParallelNode(1, 2,
                    new ConditionNode(() => bb.IsTargetAttacking || Time.time >= bb.ManeuverEndTime || bb.DistanceToTarget >= attackDistance),
                    new ActionNode(() => { ActionMoveBack(); return BTNodeStatus.Running; })
                )
            )
        );

        // =========================================================
        // [5] Idle (대기)
        // =========================================================
        var idleAction = new ActionNode(ActionIdle);

        // =========================================================
        // 최종 조립 (Root Node 구성)
        // =========================================================
        var brainNode = new SelectorNode(
            threatResponseSeq,       // [1] 위협 반응
            executeAndPressureSeq,   // [2] 특수 압박 기동 (Running 반환을 통한 자체 지속)
            normalAttackSeq,         // [3] 일반 사거리 내 공격
            maneuverSeq,             // [4] 통상 기동 유지 (Running 반환을 통한 자체 지속)
            idleAction               // [5] 모두 아니면 대기하며 타겟 주시
        );

        // 부모 병렬 노드가 매 틱마다 업데이트를 실행하며 Running을 보장
        root = new ParallelNode(2, 1, updateBBNode, brainNode);
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
        actionController.Move(Vector3.zero);

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
}
