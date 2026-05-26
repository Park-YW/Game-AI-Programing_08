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
        // [기존 변수] 타겟 상태 정보
        public float TargetHealthRatio;
        public bool IsTargetAttacking;
        public bool IsTargetEvading;
        public bool IsTargetGuarding;
        public float DistanceToTarget;

        // [기존 변수] 방향 상태
        public enum ManeuverType { None, Left, Right, Back, Forward, Idle }
        public ManeuverType CurrentManeuver = ManeuverType.None;

        // ==============================================================
        // [신규 추가] 상태 잠금 (State-Gating) 및 타이머 변수
        // ==============================================================

        /// <summary>
        /// 현재 기동(이동) 로직이 실행 중인지 여부. 
        /// true일 경우 트리 최상단에서 다른 거리 체크를 무시하고 이동 업데이트 브랜치로 직행합니다.
        /// 위협(방어/회피)이 감지되거나, 기동 종료 조건이 만족되면 false로 해제해야 합니다.
        /// </summary>
        public bool IsManeuvering = false;

        /// <summary>
        /// 현재 기동(Maneuver)을 언제 종료할 것인지 기록하는 타임스탬프 (Time.time 기준)
        /// </summary>
        public float ManeuverEndTime = 0f;

        /// <summary>
        /// 구르기 캐치나 특수 압박 기동의 쿨타임을 관리하는 타임스탬프 (Time.time 기준)
        /// 데코레이터의 로컬 변수 초기화 문제를 해결하기 위해 BB로 옮겼습니다.
        /// </summary>
        public float NextChaseTime = 0f;

        // 구르기 캐치 타이머용
        public bool IsWaitingForRollCatch = false;
        public float RollCatchEndTime = 0f;
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

        // ---------------------------------------------------------
        // 공통 노드: Blackboard 상태 업데이트 (매 프레임 실행)
        // ---------------------------------------------------------
        var updateBBNode = new ActionNode(() =>
        {
            bb.TargetHealthRatio = target.CurrentHealthRatio;
            bb.IsTargetAttacking = target.ActionController != null && target.ActionController.IsAttacking;
            bb.IsTargetEvading = target.ActionController != null && target.ActionController.IsInvincible;
            bb.IsTargetGuarding = target.ActionController != null && target.ActionController.IsBlocking;
            bb.DistanceToTarget = GetHorizontalOffsetToTarget().magnitude;

            return BTNodeStatus.Success;
        });

        // =========================================================
        // [1] Threat Response (위협 반응 - 최우선 순위)
        // 적이 공격 중일 때 발동. 기존 이동(Maneuver) 상태를 강제 해제하고 회피/방어 수행
        // =========================================================
        var threatResponseSeq = new SequenceNode(
            // 진입 조건: 적이 공격 중인가?
            new ConditionNode(() => bb.IsTargetAttacking),

            // 상태 잠금 해제: 진행 중이던 기동을 즉시 취소하여 Jitter 방지
            new ActionNode(() => {
                if (bb.IsManeuvering)
                {
                    bb.IsManeuvering = false;
                    bb.CurrentManeuver = Blackboard.ManeuverType.None;
                }
                return BTNodeStatus.Success;
            }),

            // 방어 행동 선택 (회피 우선, 불가 시 가드)
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
        );

        // =========================================================
        // [2] Maneuver UPDATE (기동 유지)
        // =========================================================
        var maneuverUpdateSeq = new SequenceNode(
            new ConditionNode(() => bb.IsManeuvering),

            new SelectorNode(
                // [A. 종료 조건 브랜치]
                new SequenceNode(
                    // [수정됨] 방향에 따라 논리적으로 종료 조건을 다르게 적용합니다.
                    new ConditionNode(() => {
                        // 1. 목표 시간이 끝났을 때 무조건 종료
                        if (Time.time >= bb.ManeuverEndTime) return true;

                        // 2. 전진 중일 때: 목표 거리(maintainDistance)까지 좁혀졌다면 조기 종료
                        if (bb.CurrentManeuver == Blackboard.ManeuverType.Forward && bb.DistanceToTarget <= maintainDistance) return true;

                        // 3. 후진 중일 때: 충분히 거리가 벌어졌다면 조기 종료
                        if (bb.CurrentManeuver == Blackboard.ManeuverType.Back && bb.DistanceToTarget > maintainDistance + 1.0f) return true;

                        return false;
                    }),

                    new ActionNode(() => {
                        bb.IsManeuvering = false;
                        bb.CurrentManeuver = Blackboard.ManeuverType.None;
                        return BTNodeStatus.Success;
                    })
                ),

                // [B. 이동 실행 브랜치]
                new ActionNode(() => {
                    if (bb.CurrentManeuver == Blackboard.ManeuverType.Left) ActionSideStepLeft();
                    else if (bb.CurrentManeuver == Blackboard.ManeuverType.Right) ActionSideStepRight();
                    else if (bb.CurrentManeuver == Blackboard.ManeuverType.Back) ActionMoveBack();
                    else if (bb.CurrentManeuver == Blackboard.ManeuverType.Forward) ActionMoveIn();
                    else ActionIdle();

                    return BTNodeStatus.Success;
                })
            )
        );

        // =========================================================
        // [3] Execute & Pressure (처형 및 압박)
        // 타겟의 체력이 30% 이하일 때 발동. 구르기를 캐치하거나 공격적인 기동으로 압박합니다.
        // =========================================================
        var executeAndPressureSeq = new SequenceNode(
            // 진입 조건 1: 타겟 체력이 30% 이하인가?
            new ConditionNode(() => bb.TargetHealthRatio <= 0.3f),
            // 진입 조건 2: 처형 패턴 쿨타임(2초)이 지났는가?
            new ConditionNode(() => Time.time >= bb.NextChaseTime),

            new SelectorNode(
                // -------------------------------------------------
                // [A. 구르기 캐치 (Roll Catch)]
                // -------------------------------------------------
                new SequenceNode(
                    // 타겟이 회피 중이거나, 이미 우리가 타이머를 재고 있는 중일 때 진입
                    // (회피 모션이 0.3초보다 빨리 끝나더라도 타이머를 끝까지 마치기 위함)
                    new ConditionNode(() => bb.IsTargetEvading || bb.IsWaitingForRollCatch),

                    new ActionNode(() => {
                        if (!bb.IsWaitingForRollCatch)
                        {
                            bb.IsWaitingForRollCatch = true;
                            bb.RollCatchEndTime = Time.time + 0.3f;
                        }

                        if (Time.time < bb.RollCatchEndTime)
                        {
                            actionController.UpdateRotationLock(GetDirectionToTarget());
                            // [핵심 변경] Running 대신 Success 반환!
                            // IsWaitingForRollCatch가 true이므로, 다음 프레임에 조건문을 통과해 다시 여기로 옵니다.
                            // 하지만 그전에 [위협 반응]을 틱(Tick)할 수 있는 기회를 보장받습니다!
                            return BTNodeStatus.Success;
                        }

                        bb.IsWaitingForRollCatch = false;
                        ActionAttack();
                        bb.NextChaseTime = Time.time + 2.0f;
                        return BTNodeStatus.Success;
                    })
                ),

                // -------------------------------------------------
                // [B. 압박 기동 (Pressure ENTRY)]
                // -------------------------------------------------
                new SequenceNode(
                    // 이미 기동 중이라면 방향을 덮어쓰지 않음
                    new ConditionNode(() => !bb.IsManeuvering),

                    new ActionNode(() => {
                        // 뒤로 가는(Back) 기동을 배제하고 전진, 좌, 우 중 랜덤하게 압박 방향 결정
                        int rand = UnityEngine.Random.Range(0, 3);
                        if (rand == 0) bb.CurrentManeuver = Blackboard.ManeuverType.Forward;
                        else if (rand == 1) bb.CurrentManeuver = Blackboard.ManeuverType.Left;
                        else bb.CurrentManeuver = Blackboard.ManeuverType.Right;

                        // 상태 잠금(IsManeuvering) 발동 및 1.0초 ~ 1.5초의 압박 타이머 세팅
                        bb.IsManeuvering = true;
                        bb.ManeuverEndTime = Time.time + UnityEngine.Random.Range(1.0f, 1.5f);

                        // 처형 패턴 쿨타임을 초기화하여 압박 중 다른 패턴이 난입하지 않게 함
                        bb.NextChaseTime = bb.ManeuverEndTime;

                        // 여기서는 이동 함수(Move)를 직접 호출하지 않습니다!
                        // Success를 반환하면 다음 프레임에 [2번 브랜치: Maneuver UPDATE]가
                        // BB에 적힌 방향과 남은 시간을 보고 대신 이동시켜 줍니다.
                        return BTNodeStatus.Success;
                    })
                )
            )
        );

        // =========================================================
        // [4] Normal Attack (일반 공격)
        // 적과 충분히 가깝고, 공격 쿨타임이 돌았을 때 실행
        // =========================================================
        var normalAttackSeq = new SequenceNode(
            // 진입 조건 1: 공격 사거리 이내인가?
            new ConditionNode(() => bb.DistanceToTarget <= attackDistance),
            // 진입 조건 2: 공격 쿨타임이 준비되었는가?
            new ConditionNode(CanAttackCooldown),

            // 진입 조건 3 & 4: 적이 무적(회피)이거나 방어 중이면 공격 낭비 방지
            new DecoratorNode(
                new ConditionNode(() => bb.IsTargetGuarding),
                (status) => status == BTNodeStatus.Success ? BTNodeStatus.Failure : BTNodeStatus.Success
            ),
            new DecoratorNode(
                new ConditionNode(() => bb.IsTargetEvading),
                (status) => status == BTNodeStatus.Success ? BTNodeStatus.Failure : BTNodeStatus.Success
            ),

            // 조건 통과 시 공격 실행
            new ActionNode(ActionAttack)
        );

        // =========================================================
        // [5] Maneuver ENTRY (기동 진입)
        // 타겟과 거리가 멀어졌을 때, 거리를 좁히거나 일정 거리를 유지하기 위한 기동을 시작합니다.
        // =========================================================
        var maneuverEntrySeq = new SequenceNode(
            // 핵심 조건: 현재 기동 중이 아닐 때(잠금 해제 상태일 때)만 새 방향을 결정합니다.
            new ConditionNode(() => !bb.IsManeuvering),

            new SelectorNode(
                // [A. 먼 거리 - 전진 기동]
                new SequenceNode(
                    new ConditionNode(() => bb.DistanceToTarget > farDistance),
                    new ActionNode(() => {
                        bb.CurrentManeuver = Blackboard.ManeuverType.Forward;
                        // 상태 잠금 및 유지 시간(0.5초 ~ 1.0초) 세팅
                        bb.IsManeuvering = true;
                        bb.ManeuverEndTime = Time.time + UnityEngine.Random.Range(0.5f, 1.0f);
                        return BTNodeStatus.Success;
                    })
                ),

                // [B. 애매한 거리 - 거리 유지 기동 (아웃복싱)]
                new SequenceNode(
                    new ConditionNode(() => bb.DistanceToTarget <= maintainDistance),
                    new ActionNode(() => {
                        // 전진을 제외하고 좌, 우, 뒤 중 하나로 스텝을 밟음
                        int rand = UnityEngine.Random.Range(0, 3);
                        if (rand == 0) bb.CurrentManeuver = Blackboard.ManeuverType.Left;
                        else if (rand == 1) bb.CurrentManeuver = Blackboard.ManeuverType.Right;
                        else bb.CurrentManeuver = Blackboard.ManeuverType.Back;

                        bb.IsManeuvering = true;
                        bb.ManeuverEndTime = Time.time + UnityEngine.Random.Range(0.5f, 1.2f);
                        return BTNodeStatus.Success;
                    })
                )
            )
        );

        // =========================================================
        // [6] Idle (대기)
        // 위 모든 브랜치의 조건을 만족하지 못했을 때 (예: 거리가 3.5 ~ 5.0 사이이면서 기동 중이 아닐 때)
        // =========================================================
        var idleAction = new ActionNode(ActionIdle);

        // =========================================================
        // 최종 조립 (Root Node 구성)
        // 우선순위 순서대로 Selector에 배치합니다.
        // =========================================================
        var brainNode = new SelectorNode(
            threatResponseSeq,       // [1] 최우선: 위협 반응 (방어/회피)
            maneuverUpdateSeq,       // [2] 상태 1순위: 기동 중이라면 다른 거 무시하고 계속 걷기
            executeAndPressureSeq,   // [3] 공격 1순위: 처형 및 특수 압박
            normalAttackSeq,         // [4] 공격 2순위: 일반 사거리 내 공격
            maneuverEntrySeq,        // [5] 상태 2순위: 가만히 서있다면 새로 거리 조절 시작
            idleAction               // [6] 최하위: 모두 아니면 대기하며 타겟 주시
        );

        // ParallelNode(2, 1)로 수정하여 Success 즉시 종료 버그 우회 (Stateless 환경 대응)
        // 1번(Success 임계값)을 2로 올려, 틱 중 조건 만족으로 끝나지 않고 Running을 보장합니다.
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
        // [추가된 핵심 코드] 이동을 확실하게 멈춥니다!
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
