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

    // Action constants for a clear RL action space.
    // Branch 0: action type (0 = none, 1 = attack, 2 = block, 3 = dodge)
    // Add more branches if more complex behaviors are needed.
    // Make sure the Behavior Parameters action space in Unity Editor matches these constants.
    private const int SkillNone = 0;
    private const int SkillAttack = 1;
    private const int SkillBlock = 2;
    private const int SkillDodge = 3;
    private float previousSelfHp;
    private float previousOpponentHp;


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
        // TODO: Reset or initialize values needed at the start of each episode.
        previousSelfHp = self.CurrentHealth;
        previousOpponentHp = opponent.CurrentHealth;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // TODO: Add observations for the agent to learn from.
        // Example:
        // sensor.AddObservation(self.CurrentHealthRatio);
        // Make sure the Behavior Parameters observation space in Unity Editor matches the number of observations added here.
        
        sensor.AddObservation(self.CurrentHealthRatio);

        sensor.AddObservation(opponent.CurrentHealthRatio);

        float distance =
            Vector3.Distance(
                transform.position,
                opponent.transform.position);

        sensor.AddObservation(distance);

        sensor.AddObservation(
            cooldownSystem.IsAttackReady() ? 1f : 0f);

        sensor.AddObservation(
            cooldownSystem.IsBlockReady() ? 1f : 0f);

        sensor.AddObservation(
            cooldownSystem.IsDodgeReady() ? 1f : 0f);

        sensor.AddObservation(
            opponent.ActionController.IsAttacking ? 1f : 0f);

        sensor.AddObservation(
            opponent.ActionController.IsBlocking ? 1f : 0f);

        sensor.AddObservation(
            opponent.ActionController.IsInvincible ? 1f : 0f);

        bool opponentVulnerable =
            !opponent.ActionController.IsAttacking &&
            !opponent.ActionController.IsBlocking &&
            !opponent.ActionController.IsInvincible;

        sensor.AddObservation(
            opponentVulnerable ? 1f : 0f);

        Vector3 dir =
        (opponent.transform.position - transform.position).normalized;

        sensor.AddObservation(dir.x);
        sensor.AddObservation(dir.z);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // TODO: Convert actions into movement or combat commands.
        // TODO: Add rewards or penalties based on the result.
        // TODO: End the episode when needed.

        int combatAction = actions.DiscreteActions[0];
        int movementAction = actions.DiscreteActions[1];

        Vector3 toOpponent =
            (opponent.transform.position - transform.position).normalized;

        Vector3 moveDirection = Vector3.zero;

        switch (movementAction)
        {
            case 1:
                moveDirection = toOpponent;
                break;

            case 2:
                moveDirection = -toOpponent;
                break;

            case 3:
                moveDirection = -transform.right;
                break;

            case 4:
                moveDirection = transform.right;
                break;
        }

        if (moveDirection != Vector3.zero)
        {
            actionController.Move(moveDirection);
        }

        switch (combatAction)
        {
            case SkillAttack:
                if (opponent.ActionController.IsBlocking)
                {
                    AddReward(-0.2f);
                }

                if (opponent.ActionController.IsInvincible)
                {
                    AddReward(-0.3f);
                }

                actionController.Face(toOpponent);
                actionController.Attack();
                break;

            case SkillBlock:
                if (!opponent.ActionController.IsAttacking)
                {
                    AddReward(-0.3f);
                }
                actionController.Face(toOpponent);
                actionController.Block();
                break;

            case SkillDodge:

                if (!opponent.ActionController.IsAttacking)
                {
                    AddReward(-0.4f);
                }
                actionController.Dodge(-toOpponent);
                break;
        }
        if (opponent.CurrentHealth < previousOpponentHp)
        {
            AddReward(0.5f);
            bool opponentVulnerable =
                !opponent.ActionController.IsAttacking &&
                !opponent.ActionController.IsBlocking &&
                !opponent.ActionController.IsInvincible;

            if (opponentVulnerable)
            {
                AddReward(1.0f);
            }
        }

        if (self.CurrentHealth < previousSelfHp)
        {
            AddReward(-1.0f);
        }

        float distance =
            Vector3.Distance(
                transform.position,
                opponent.transform.position);

        if (opponent.CurrentHealthRatio > 0.3f)
        {
            if (distance >= 1.8f && distance <= 3.5f)
            {
                AddReward(0.005f);
            }
            else
            {
                AddReward(-0.005f);
            }
        }

        previousOpponentHp = opponent.CurrentHealth;
        previousSelfHp = self.CurrentHealth;

        if (opponent.IsDead)
        {
            AddReward(4.0f);
            EndEpisode();
        }

        if (self.IsDead)
        {
            AddReward(-5.0f);
            EndEpisode();
        }
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
    }
}
