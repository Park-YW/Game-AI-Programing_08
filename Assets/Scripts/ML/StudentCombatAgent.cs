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

    [SerializeField] private float maxDistance = 10f;

    // Behavior Parameters: Discrete Branches [5, 4]
    // Branch 0 (move): 0 stay, 1 forward, 2 back, 3 left, 4 right
    // Branch 1 (skill): 0 none, 1 attack, 2 block, 3 dodge
    private const int MoveForward = 1;
    private const int MoveBack = 2;
    private const int MoveLeft = 3;
    private const int MoveRight = 4;
    private const int SkillAttack = 1;
    private const int SkillBlock = 2;
    private const int SkillDodge = 3;

    private const int ObservationCount = 15;

    private const float WinReward = 10f;
    private const float LosePenalty = 1f;
    private const float TimeoutPenalty = 2f;
    private const float DamageDealtRewardPerPoint = 0.1f;

    // Opponent profile: imstar_Attack_BT (rush + attack @ 1.6m, reactive block, dodge @ 30% HP)
    private const float MeleeRange = 1.6f;
    private const float LowEnemyHealthRatio = 0.3f;

    // Priority 1 — threat response (imstar_Attack_BT attacks often once in range)
    private const float GuardSuccessReward = 1.5f;
    private const float PerfectCounterReward = 3.5f;
    private const float PostGuardAttackAttemptReward = 0.4f;
    private const float PostGuardHitBonusReward = 1f;
    private const float IdleBlockPenalty = 0.12f;
    private const float StrategicRetreatReward = 0.5f;
    private const float ThreatBlockAttemptReward = 0.2f;
    private const float ThreatDodgeAttemptReward = 0.15f;
    private const float LateDodgePenalty = 0.2f;
    private const float DamageTakenPenaltyLight = 1f;
    private const float DamageTakenPenaltyHeavy = 2f;
    private const float CounterLinkWindow = 1f;

    // Priority 2 — execute pressure
    private const float DealCatchReward = 1f;
    private const float PressurePositionReward = 0.05f;
    private const float PassivePlayPenalty = 0.5f;
    private const float DealCatchHitWindow = 0.35f;

    // Priority 3 — deliberate strikes
    private const float ValidStrikeReward = 1f;
    private const float MeaninglessAttackPenalty = 0.15f;
    private const float DefenseRecoveryWindow = 0.6f;

    // Spacing / skill exploration
    private const float StepPenalty = 0.002f;
    private const float NeutralStrafeReward = 0.06f;
    private const float SkillStrafeConflictPenalty = 0.22f;
    private const float CombatSkillBlockedPenalty = 0.18f;
    private const float NoSkillInMeleePenalty = 0.12f;
    private const float SkillUseReward = 0.06f;
    private const float MeleeAttackAttemptReward = 0.25f;
    private const float FacingOpponentReward = 0.01f;
    private const float FacingAwayPenalty = 0.04f;
    private const float FacingAngleThreshold = 50f;

    [SerializeField] private float opponentAttackHitDelay = 0.4f;
    [SerializeField] private float opponentAttackLatePhaseRatio = 0.75f;
    [SerializeField] private float defaultOpponentAttackDuration = 0.85f;

    private float lastSelfHealth;
    private float lastOpponentHealth;

    private bool wasOpponentAttacking;
    private bool wasOpponentBlocking;
    private bool wasOpponentInvincible;
    private bool wasSelfBlocking;

    private float opponentAttackStartTime;
    private float lastOpponentAttackDuration;
    private bool guardRewardedForCurrentAttack;

    private float lastDefenseSuccessTime;
    private float opponentDodgeEndTime;
    private float opponentDefenseEndTime;

    private bool dealCatchSetupActive;
    private bool attackedDuringDealCatchSetup;

    private int lastMoveAction;
    private int lastSkillAction;

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
        ResetRewardTracking();
        lastSelfHealth = self != null ? self.CurrentHealth : 0f;
        lastOpponentHealth = opponent != null ? opponent.CurrentHealth : 0f;

        if (opponent == null)
        {
            Debug.LogWarning($"{name}: Opponent is not assigned. Observations and combat rewards will not work.");
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (self == null || opponent == null || actionController == null || cooldownSystem == null)
        {
            for (int i = 0; i < ObservationCount; i++)
            {
                sensor.AddObservation(0f);
            }

            return;
        }

        Vector3 toOpponent = opponent.transform.position - transform.position;
        toOpponent.y = 0f;
        float distance = toOpponent.magnitude;
        Vector3 dir = distance > 0.001f ? toOpponent / distance : Vector3.forward;
        Vector3 localDir = transform.InverseTransformDirection(dir);

        CooldownSystem oppCd = opponent.CooldownSystem;
        CombatActionController oppAct = opponent.ActionController;

        sensor.AddObservation(self.CurrentHealthRatio);
        sensor.AddObservation(opponent.CurrentHealthRatio);
        sensor.AddObservation(Mathf.Clamp01(distance / maxDistance));
        sensor.AddObservation(localDir.x);
        sensor.AddObservation(localDir.z);
        sensor.AddObservation(cooldownSystem.GetAttackCooldownRatio());
        sensor.AddObservation(cooldownSystem.GetBlockCooldownRatio());
        sensor.AddObservation(cooldownSystem.GetDodgeCooldownRatio());
        sensor.AddObservation(oppCd != null ? oppCd.GetAttackCooldownRatio() : 0f);
        sensor.AddObservation(oppCd != null ? oppCd.GetBlockCooldownRatio() : 0f);
        sensor.AddObservation(oppCd != null ? oppCd.GetDodgeCooldownRatio() : 0f);
        sensor.AddObservation(actionController.IsBusy ? 1f : 0f);
        sensor.AddObservation(oppAct != null && oppAct.IsAttacking ? 1f : 0f);
        sensor.AddObservation(oppAct != null && oppAct.IsBlocking ? 1f : 0f);
        sensor.AddObservation(oppAct != null && oppAct.IsInvincible ? 1f : 0f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (actionController == null)
        {
            return;
        }

        Vector3 toOpponent = Vector3.forward;
        if (opponent != null)
        {
            toOpponent = opponent.transform.position - transform.position;
            toOpponent.y = 0f;
            if (toOpponent.sqrMagnitude > 0.0001f)
            {
                toOpponent.Normalize();
            }
        }

        int move = actions.DiscreteActions[0];
        int skill = actions.DiscreteActions[1];
        lastMoveAction = move;
        lastSkillAction = skill;

        Vector3 strafeRight = Vector3.Cross(Vector3.up, toOpponent);
        if (strafeRight.sqrMagnitude > 0.0001f)
        {
            strafeRight.Normalize();
        }
        else
        {
            strafeRight = transform.right;
        }

        switch (move)
        {
            case MoveForward:
                actionController.MoveWithoutFacing(toOpponent);
                break;
            case MoveBack:
                actionController.MoveWithoutFacing(-toOpponent);
                break;
            case MoveLeft:
                actionController.MoveWithoutFacing(-strafeRight);
                break;
            case MoveRight:
                actionController.MoveWithoutFacing(strafeRight);
                break;
        }

        bool dodgeAttempted = false;
        bool attackStarted = false;
        bool blockStarted = false;
        bool wasBlocking = actionController.IsBlocking;
        bool attackReady = cooldownSystem != null && cooldownSystem.IsAttackReady();
        bool blockReady = cooldownSystem != null && cooldownSystem.IsBlockReady();
        bool dodgeReady = cooldownSystem != null && cooldownSystem.IsDodgeReady();

        switch (skill)
        {
            case SkillAttack:
            {
                bool wasAttacking = actionController.IsAttacking;
                actionController.Face(toOpponent);
                actionController.Attack();
                attackStarted = actionController.IsAttacking && !wasAttacking;
                break;
            }
            case SkillBlock:
                actionController.Block(toOpponent);
                blockStarted = actionController.IsBlocking && !wasBlocking;
                break;
            case SkillDodge:
                if (dodgeReady)
                {
                    dodgeAttempted = true;
                    lastDefenseSuccessTime = Time.time;
                }

                actionController.Face(toOpponent);
                actionController.Dodge(-toOpponent);
                break;
        }

        bool combatSkillBlocked =
            (skill == SkillAttack && attackReady && !attackStarted)
            || (skill == SkillBlock && blockReady && !blockStarted)
            || (skill == SkillDodge && dodgeReady && !dodgeAttempted);

        if (skill == SkillAttack && dealCatchSetupActive && opponent.ActionController != null
            && opponent.ActionController.IsInvincible)
        {
            attackedDuringDealCatchSetup = true;
        }

        if (!actionController.IsBusy && opponent != null)
        {
            actionController.Face(toOpponent);
        }

        ApplyDecisionRewards(move, skill, dodgeAttempted, attackStarted, combatSkillBlocked);
        ApplyFacingRewards();
        AddReward(-StepPenalty);
        TryEndEpisodeOnDeath();
    }

    private void LateUpdate()
    {
        if (!isActiveAndEnabled || self == null || opponent == null || actionController == null)
        {
            return;
        }

        UpdateCombatStateTracking();
        TryRewardGuardSuccess();
        ApplyDamageRewards();
        TryEndEpisodeOnTimeout();
    }

    private void UpdateCombatStateTracking()
    {
        CombatActionController oppAct = opponent.ActionController;
        bool opponentAttacking = oppAct != null && oppAct.IsAttacking;
        bool opponentBlocking = oppAct != null && oppAct.IsBlocking;
        bool opponentInvincible = oppAct != null && oppAct.IsInvincible;
        bool selfBlocking = actionController.IsBlocking;

        if (opponentAttacking && !wasOpponentAttacking)
        {
            opponentAttackStartTime = Time.time;
            guardRewardedForCurrentAttack = false;
        }

        if (!opponentAttacking && wasOpponentAttacking)
        {
            float duration = Mathf.Max(0.1f, Time.time - opponentAttackStartTime);
            lastOpponentAttackDuration = duration;
        }

        if (opponentInvincible && !wasOpponentInvincible)
        {
            if (opponent.CurrentHealthRatio <= LowEnemyHealthRatio)
            {
                dealCatchSetupActive = true;
                attackedDuringDealCatchSetup = false;
            }
        }

        if (wasOpponentInvincible && !opponentInvincible)
        {
            opponentDodgeEndTime = Time.time;
        }

        if ((opponentBlocking || opponentInvincible) && !(wasOpponentBlocking || wasOpponentInvincible))
        {
            opponentDefenseEndTime = -1f;
        }

        if ((wasOpponentBlocking || wasOpponentInvincible) && !opponentBlocking && !opponentInvincible)
        {
            opponentDefenseEndTime = Time.time;
        }

        wasOpponentAttacking = opponentAttacking;
        wasOpponentBlocking = opponentBlocking;
        wasOpponentInvincible = opponentInvincible;
        wasSelfBlocking = selfBlocking;
    }

    private void TryRewardGuardSuccess()
    {
        CombatActionController oppAct = opponent.ActionController;
        if (oppAct == null || !oppAct.IsAttacking || guardRewardedForCurrentAttack)
        {
            return;
        }

        float elapsed = Time.time - opponentAttackStartTime;
        if (elapsed < opponentAttackHitDelay * 0.5f || elapsed > opponentAttackHitDelay * 1.5f)
        {
            return;
        }

        if (!actionController.IsBlocking)
        {
            return;
        }

        AddReward(GuardSuccessReward);
        lastDefenseSuccessTime = Time.time;
        guardRewardedForCurrentAttack = true;
    }

    private void ApplyDamageRewards()
    {
        if (self == null || opponent == null)
        {
            return;
        }

        float dmgToSelf = lastSelfHealth - self.CurrentHealth;
        float dmgToOpponent = lastOpponentHealth - opponent.CurrentHealth;

        if (dmgToSelf > 0f)
        {
            CombatActionController oppAct = opponent.ActionController;
            bool threatened = oppAct != null && (oppAct.IsAttacking || wasOpponentAttacking);
            AddReward(-(threatened ? DamageTakenPenaltyHeavy : DamageTakenPenaltyLight));
        }

        if (dmgToOpponent > 0f)
        {
            AddReward(dmgToOpponent * DamageDealtRewardPerPoint);
            ApplyHitRewards();
        }

        lastSelfHealth = self.CurrentHealth;
        lastOpponentHealth = opponent.CurrentHealth;
    }

    private void ApplyHitRewards()
    {
        float distance = GetHorizontalDistance();
        bool recentDefense = Time.time - lastDefenseSuccessTime <= CounterLinkWindow;

        if (recentDefense && lastSkillAction == SkillAttack)
        {
            AddReward(PerfectCounterReward);
            AddReward(PostGuardHitBonusReward);
            return;
        }

        if (opponent.CurrentHealthRatio <= LowEnemyHealthRatio
            && opponentDodgeEndTime > 0f
            && Time.time - opponentDodgeEndTime <= DealCatchHitWindow
            && !attackedDuringDealCatchSetup)
        {
            AddReward(DealCatchReward);
            dealCatchSetupActive = false;
            return;
        }

        if (distance <= MeleeRange && IsSafeToStrike())
        {
            AddReward(ValidStrikeReward);
        }
    }

    private void ApplyDecisionRewards(
        int move,
        int skill,
        bool dodgeAttempted,
        bool attackStarted,
        bool combatSkillBlocked)
    {
        if (self == null || opponent == null || cooldownSystem == null)
        {
            return;
        }

        CombatActionController oppAct = opponent.ActionController;
        float distance = GetHorizontalDistance();
        bool opponentAttacking = oppAct != null && oppAct.IsAttacking;
        bool opponentBlocking = oppAct != null && oppAct.IsBlocking;
        bool opponentInvincible = oppAct != null && oppAct.IsInvincible;
        bool enemyLowHealth = opponent.CurrentHealthRatio <= LowEnemyHealthRatio;
        bool blockOnCooldown = !cooldownSystem.IsBlockReady();

        if (blockOnCooldown && opponentAttacking && move == MoveBack)
        {
            AddReward(StrategicRetreatReward);
        }

        bool recentDefense = Time.time - lastDefenseSuccessTime <= CounterLinkWindow;

        if (opponentAttacking)
        {
            if (skill == SkillBlock)
            {
                AddReward(ThreatBlockAttemptReward);
            }
            else if (skill == SkillDodge && !IsOpponentAttackLatePhase())
            {
                AddReward(ThreatDodgeAttemptReward);
            }
        }
        else if (skill == SkillBlock)
        {
            AddReward(-IdleBlockPenalty);
        }

        if (recentDefense && skill == SkillAttack)
        {
            AddReward(PostGuardAttackAttemptReward);
        }

        if (skill == SkillDodge && opponentAttacking && IsOpponentAttackLatePhase())
        {
            AddReward(-LateDodgePenalty);
        }

        if (attackStarted && (opponentBlocking || opponentInvincible))
        {
            AddReward(-MeaninglessAttackPenalty);
        }

        if (enemyLowHealth && !opponentAttacking && (move == MoveBack || dodgeAttempted))
        {
            AddReward(-PassivePlayPenalty);
        }

        if (enemyLowHealth
            && !cooldownSystem.IsAttackReady()
            && distance <= MeleeRange
            && IsPressuringMove(move))
        {
            AddReward(PressurePositionReward);
        }

        if (IsStrafeMove(move) && skill == 0)
        {
            AddReward(NeutralStrafeReward);
        }

        if (IsStrafeMove(move) && IsCombatSkill(skill))
        {
            AddReward(-SkillStrafeConflictPenalty);
        }
        else if (combatSkillBlocked)
        {
            AddReward(-CombatSkillBlockedPenalty);
        }

        if (skill != 0)
        {
            AddReward(SkillUseReward);
        }

        if (distance <= MeleeRange && skill == 0)
        {
            AddReward(-NoSkillInMeleePenalty);
        }

        if (attackStarted && distance <= MeleeRange)
        {
            AddReward(MeleeAttackAttemptReward);
        }
    }

    private void ApplyFacingRewards()
    {
        if (opponent == null)
        {
            return;
        }

        Vector3 toOpponent = opponent.transform.position - transform.position;
        toOpponent.y = 0f;
        if (toOpponent.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector3 forward = transform.forward;
        forward.y = 0f;
        float angle = Vector3.Angle(forward, toOpponent.normalized);
        if (angle <= FacingAngleThreshold)
        {
            AddReward(FacingOpponentReward);
        }
        else
        {
            AddReward(-FacingAwayPenalty);
        }
    }

    private bool IsOpponentAttackLatePhase()
    {
        CombatActionController oppAct = opponent.ActionController;
        if (oppAct == null || !oppAct.IsAttacking)
        {
            return false;
        }

        float duration = lastOpponentAttackDuration > 0.1f
            ? lastOpponentAttackDuration
            : defaultOpponentAttackDuration;
        float elapsed = Time.time - opponentAttackStartTime;
        return elapsed >= duration * opponentAttackLatePhaseRatio;
    }

    private bool IsSafeToStrike()
    {
        CombatActionController oppAct = opponent.ActionController;
        if (oppAct == null)
        {
            return true;
        }

        if (oppAct.IsAttacking || oppAct.IsBlocking || oppAct.IsInvincible)
        {
            return false;
        }

        if (opponentDefenseEndTime > 0f
            && Time.time - opponentDefenseEndTime <= DefenseRecoveryWindow)
        {
            return true;
        }

        CooldownSystem oppCd = opponent.CooldownSystem;
        if (oppCd != null
            && (oppCd.GetBlockCooldownRatio() > 0.2f || oppCd.GetDodgeCooldownRatio() > 0.2f))
        {
            return true;
        }

        return opponentDefenseEndTime > 0f;
    }

    private bool ShouldPressIn(float distance)
    {
        if (distance > MeleeRange)
        {
            return false;
        }

        if (cooldownSystem != null && cooldownSystem.IsAttackReady())
        {
            return IsSafeToStrike();
        }

        return opponent != null && opponent.CurrentHealthRatio <= LowEnemyHealthRatio;
    }

    private static bool IsPressuringMove(int move)
    {
        return move == MoveForward;
    }

    private static bool IsStrafeMove(int move)
    {
        return move == MoveLeft || move == MoveRight;
    }

    private static bool IsCombatSkill(int skill)
    {
        return skill == SkillAttack || skill == SkillBlock || skill == SkillDodge;
    }

    private float GetHorizontalDistance()
    {
        if (opponent == null)
        {
            return maxDistance;
        }

        Vector3 offset = opponent.transform.position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private void TryEndEpisodeOnDeath()
    {
        if (opponent != null && opponent.IsDead)
        {
            AddReward(WinReward);
            EndEpisode();
        }
        else if (self != null && self.IsDead)
        {
            AddReward(-LosePenalty);
            EndEpisode();
        }
    }

    private void TryEndEpisodeOnTimeout()
    {
        if (episodeManager == null)
        {
            FillDefaultReferences();
        }

        if (episodeManager != null && episodeManager.TryConsumeMlTimeoutEnd())
        {
            AddReward(-TimeoutPenalty);
            EndEpisode();
        }
    }

    private void ResetRewardTracking()
    {
        wasOpponentAttacking = false;
        wasOpponentBlocking = false;
        wasOpponentInvincible = false;
        wasSelfBlocking = false;
        guardRewardedForCurrentAttack = false;
        opponentAttackStartTime = 0f;
        lastOpponentAttackDuration = defaultOpponentAttackDuration;
        lastDefenseSuccessTime = -CounterLinkWindow;
        opponentDodgeEndTime = -1f;
        opponentDefenseEndTime = -1f;
        dealCatchSetupActive = false;
        attackedDuringDealCatchSetup = false;
        lastMoveAction = 0;
        lastSkillAction = 0;
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

        if (episodeManager == null)
        {
            episodeManager = FindFirstObjectByType<EpisodeManager>();
        }

        if (opponent == null)
        {
            opponent = ResolveOpponentCharacter();
        }
    }

    private CombatCharacter ResolveOpponentCharacter()
    {
        if (self != null && self.transform.parent != null)
        {
            CombatCharacter[] localAgents = self.transform.parent.GetComponentsInChildren<CombatCharacter>();
            foreach (CombatCharacter candidate in localAgents)
            {
                if (candidate != self)
                {
                    return candidate;
                }
            }
        }

        CombatCharacter[] allAgents = FindObjectsByType<CombatCharacter>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (CombatCharacter candidate in allAgents)
        {
            if (candidate != self)
            {
                return candidate;
            }
        }

        return null;
    }
}
