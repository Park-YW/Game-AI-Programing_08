using System;
using System.Collections.Generic;
using UnityEngine;

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
        // =====================================================================
        // TODO: Choose an attacker or defender role. -> [수비형(Defender) 선택]
        // =====================================================================

        // [설계 기준 변수 설정]
        // 제공 템플릿의 사거리(Attack Range = 2) 기준 분석 적용
        float attackRange = 2.0f;
        float dangerCloseDistance = 1.0f;

        // ---------------------------------------------------------------------
        // 1. 위기 탈출 시퀀스 (new List<BTNode> 제거 후 가변 인자로 바로 전달)
        // ---------------------------------------------------------------------
        BTNode emergencyEscape = new SequenceNode(
            new ConditionNode(() => (self.CurrentHealth / (float)self.MaxHealth) <= 0.3f), // Health Check
            new ConditionNode(() => cooldownSystem.IsDodgeReady()),                        // Cooldown Check
            new ActionNode(() => {
                actionController.Dodge(-DirectionToTarget()); // 반대 방향으로 회피
                return BTNodeStatus.Success;
            })
        );

        // ---------------------------------------------------------------------
        // 2. 가드 성공 후 무작위(Non-deterministic) 반격 심리전 패턴 정의
        // ---------------------------------------------------------------------
        BTNode randomCounterPatterns = new RandomSelectorNode(
            // 패턴 A: 적을 조준하고 즉각적인 기본 반격
            new SequenceNode(
                new ConditionNode(() => cooldownSystem.IsAttackReady()),
                new ConditionNode(() => IsFacingTarget(45f)), // Facing Check
                new ActionNode(() => {
                    actionController.Attack();
                    return BTNodeStatus.Success;
                })
            ),
            // 패턴 B: 측면 기습 회피 기동 후 반격
            new SequenceNode(
                new ConditionNode(() => cooldownSystem.IsDodgeReady()),
                new ActionNode(() => {
                    Vector3 sideDirection = Vector3.Cross(DirectionToTarget(), Vector3.up).normalized;
                    actionController.Dodge(sideDirection);
                    return BTNodeStatus.Success;
                }),
                new ActionNode(() => {
                    actionController.Attack();
                    return BTNodeStatus.Success;
                })
            )
        );

        // ---------------------------------------------------------------------
        // 3. 실시간 가드 및 카운터 시퀀스
        // ---------------------------------------------------------------------
        BTNode guardAndCounter = new SequenceNode(
            new ConditionNode(() => cooldownSystem.IsBlockReady()),
            new ActionNode(() => {
                actionController.Block();
                return BTNodeStatus.Success;
            }),
            randomCounterPatterns // 블로킹 직후 무작위 반격 체계 돌입
        );

        // ---------------------------------------------------------------------
        // 4. 복합 교전 제어 (ParallelNode 활용)
        // 오류 CS7036 해결: 첫 번째나 마지막 인자에 조건에 맞는 threshold 정수값(예: 1, 1)을 넣어줍니다.
        // (보통 ParallelNode(int successThreshold, int failureThreshold, params BTNode[] nodes) 구조입니다)
        // ---------------------------------------------------------------------
        BTNode activeDefenseArea = new ParallelNode(
            1, // successThreshold: 둘 중 하나만 만족해도 실행 상태 유지
            1, // failureThreshold: 조건 노드가 Failure를 반환하면 즉시 실패 처리
            new ConditionNode(() => DistanceToTarget() <= attackRange), // Distance Check
            guardAndCounter
        );

        // ---------------------------------------------------------------------
        // 5. 안전거리 유지 시퀀스 (상대가 너무 밀고 들어오면 뒤로 후퇴 무빙)
        // ---------------------------------------------------------------------
        BTNode maintainDistance = new SequenceNode(
            new ConditionNode(() => DistanceToTarget() <= dangerCloseDistance),
            new ActionNode(() => {
                actionController.Move(-DirectionToTarget());
                return BTNodeStatus.Running;
            })
        );

        // ---------------------------------------------------------------------
        // 6. 기본 대기 및 추적 행동 (Fallback)
        // ---------------------------------------------------------------------
        BTNode fallbackMove = new ActionNode(() => {
            actionController.Move(DirectionToTarget());
            return BTNodeStatus.Running;
        });


        // =====================================================================
        // TODO: Build a root SelectorNode or SequenceNode.
        // =====================================================================
        root = new SelectorNode(
            emergencyEscape,     // 1순위: 위기 처해지면 즉시 회피 구동
            activeDefenseArea,   // 2순위: 사거리 내 진입 시 병렬 가드 및 무작위 반격
            maintainDistance,    // 3순위: 너무 인접 시 안정적인 거리 유지 무빙
            fallbackMove         // 4순위: 기본 타겟 추적 방향 이동 (Fallback)
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