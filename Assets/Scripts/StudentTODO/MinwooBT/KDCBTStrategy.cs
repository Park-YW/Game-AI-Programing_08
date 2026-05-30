using System;
using System.Collections.Generic;
using UnityEngine;

public class KDCBTStrategy : MonoBehaviour
{
    [SerializeField] private CombatCharacter self;
    [SerializeField] private CombatCharacter target;
    [SerializeField] private CombatActionController actionController;
    [SerializeField] private CooldownSystem cooldownSystem;

    [SerializeField] private float closeDistance = 2.0f;
    [SerializeField] private float preferredDistance = 2.4f;
    [SerializeField] private float lowHealthRatio = 0.3f;

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

        if (actionController.IsBlocking)
        {
            actionController.UpdateRotationLock(GetDirectionToTarget());
        }

        root.Tick();
    }

    private void BuildTree()
    {
        // 1. 위기 탈출 시퀀스 (체력 부족 시 뒤로 회피)
        BTNode emergencyEscape = new SequenceNode(
            new ConditionNode(ShouldDodge),
            new ActionNode(DodgeAway)
        );

        // 2. 가드 성공 후 연계 카운터 패턴 (RandomSelectorNode)
        BTNode postGuardCounter = new RandomSelectorNode(
            new SequenceNode(
                new ConditionNode(CanCounterAttack),
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
                new ConditionNode(CanCounterAttack),
                new ActionNode(Attack)
            )
        );

        // 3. ★ [수비형 확률 가중치 대개혁] 심리전 확률 분배
        // 무작위 선택 노드를 일반 SelectorNode로 바꾸고, 내부에 '확률 조건문'을 심어 빈도를 정밀 통제합니다.
        BTNode multiTacticalCombat = new SelectorNode(
            // [전술 A - 핵심 수비 행동]: 상대가 위협적일 때 철벽 방어 후 카운터 (발동 확률 85%의 메인 주력선)
            new SequenceNode(
                new ConditionNode(() => UnityEngine.Random.value <= 0.85f), // 85% 확률 주입
                new ConditionNode(CanBlockIncomingAttack),
                new ActionNode(() => {
                    actionController.Move(Vector3.zero); // 무빙 간섭 차단
                    actionController.Block(GetDirectionToTarget());
                    return BTNodeStatus.Success;
                }),
                postGuardCounter
            ),
            // [전술 B - 가끔 터지는 기습]: 상대 빈틈 보일 때 선제 공격 (발동 확률 15%로 대폭 축소)
            new SequenceNode(
                new ConditionNode(() => UnityEngine.Random.value <= 0.15f), // 15% 이하로 제안
                new ConditionNode(CanCounterAttack),
                new ActionNode(() => {
                    actionController.Move(Vector3.zero);
                    actionController.Face(GetDirectionToTarget());
                    actionController.Attack();
                    return BTNodeStatus.Success;
                })
            ),
            // [전술 C - 타이밍 회피]: 가드 대신 측면 회피 후 역습
            new SequenceNode(
                new ConditionNode(CanCounterAttack),
                new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsDodgeReady() && IsTargetAttackReady()),
                new ActionNode(() => {
                    Vector3 sideDirection = Vector3.Cross(GetDirectionToTarget(), Vector3.up).normalized;
                    actionController.Face(GetDirectionToTarget());
                    actionController.Dodge(sideDirection);
                    return BTNodeStatus.Success;
                }),
                new ConditionNode(CanCounterAttack),
                new ActionNode(Attack)
            )
        );

        // 4. 실시간 교전 제어 보호막 (가드 중 무빙 명령 가로채기 방지)
        BTNode combatStateGuard = new SelectorNode(
            multiTacticalCombat,
            new SequenceNode(
                new ConditionNode(() => actionController != null && actionController.IsBlocking),
                new ActionNode(() => {
                    actionController.Move(Vector3.zero); // 가드 중 빽스텝 관성 브레이크 고정
                    return BTNodeStatus.Success;
                })
            )
        );

        // 5. 실시간 교전 제어 (ParallelNode - 2순위)
        BTNode activeDefenseArea = new ParallelNode(
            1, 1,
            combatStateGuard
        );

        // 6. 초근접 교전 확정 구역
        BTNode closeCombatZone = new SequenceNode(
            new ConditionNode(IsTargetClose),
            activeDefenseArea
        );

        // 7. 기본 대치 및 안전거리 유지 행동 (애니메이션 글리치 해결 버전)
        BTNode distanceStabilizer = new ActionNode(MaintainDistanceWithSmoothBrake);


        // =====================================================================
        // [최종 Root Selector 조립]
        // =====================================================================
        root = new SelectorNode(
            emergencyEscape,
            closeCombatZone,
            distanceStabilizer
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

    #region 조건문 함수군 (Condition Nodes)

    private bool ShouldDodge()
    {
        return self.CurrentHealthRatio <= lowHealthRatio
            && cooldownSystem != null
            && cooldownSystem.IsDodgeReady();
    }

    private bool CanBlockIncomingAttack()
    {
        return IsTargetClose()
            && actionController != null && !actionController.IsBlocking
            && cooldownSystem != null && cooldownSystem.IsBlockReady()
            && (IsTargetAttacking() || IsTargetAttackReady());
    }

    private bool CanCounterAttack()
    {
        return IsTargetClose()
            && cooldownSystem != null
            && cooldownSystem.IsAttackReady();
    }

    private bool IsTargetClose()
    {
        return GetHorizontalOffsetToTarget().magnitude <= closeDistance;
    }

    private bool IsTargetAttacking()
    {
        if (target == null) return false;
        return target.ActionController != null && target.ActionController.IsAttacking;
    }

    private bool IsTargetAttackReady()
    {
        if (target == null) return false;
        return target.CooldownSystem != null && target.CooldownSystem.IsAttackReady();
    }

    #endregion

    #region 행동 함수군 (Action Nodes)

    private BTNodeStatus DodgeAway()
    {
        actionController.Face(GetDirectionToTarget());
        actionController.Dodge(-GetDirectionToTarget());
        return BTNodeStatus.Success;
    }

    private BTNodeStatus Attack()
    {
        actionController.Face(GetDirectionToTarget());
        actionController.Attack();
        return BTNodeStatus.Success;
    }

    // ★ 애니메이션 꼬임 및 미끄러짐을 완벽하게 해결한 빽스텝 튜닝 함수
    private BTNodeStatus MaintainDistanceWithSmoothBrake()
    {
        Vector3 offset = GetHorizontalOffsetToTarget();
        float currentDistance = offset.magnitude;

        // 구역 1: 적당한 안전거리 영역에 안착하면 물리 가속도를 지우고 정지 (모션 굳어짐 방지)
        if (currentDistance >= closeDistance && currentDistance <= preferredDistance)
        {
            actionController.Move(Vector3.zero);
        }
        // 구역 2: 적이 너무 밀고 들어왔을 때 (2.0f 미만)
        else if (currentDistance < closeDistance)
        {
            // ★ 애니메이션이 깨지지 않도록 회피(Dodge) 명령을 완전히 배제하고, 
            // 뒤로 걷는 순수 백스텝 Move 방향과 속도 가중치만 부드럽게 가해줍니다.
            actionController.Move(-GetDirectionToTarget() * 0.3f);
        }
        // 구역 3: 교전 사거리 밖으로 너무 멀어지면 대치를 위해 서서히 접근
        else
        {
            actionController.Move(GetDirectionToTarget() * 0.4f);
        }

        return BTNodeStatus.Success;
    }

    #endregion

    #region 수학적 벡터 연산 함수군

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

    #endregion

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