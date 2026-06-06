using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

// Resets both combatants and reports the episode result for later BT/RL training loops.
// CSV 로깅 기능 추가: Assets/Results/episode_log.csv 에 에피소드 결과 자동 저장
public class EpisodeManager : MonoBehaviour
{
    [SerializeField] private CombatCharacter agentA;
    [SerializeField] private CombatCharacter agentB;
    [SerializeField] private Transform spawnPointA;
    [SerializeField] private Transform spawnPointB;
    [SerializeField] private float resetDelay = 1.5f;
    [SerializeField] private float maxEpisodeTime = 60f;

    // CSV 로깅 활성화 여부 (Inspector에서 체크/해제 가능)
    // 학습 중 행동 분포까지 수집하려면 true 유지
    // 단순 동작 확인 시에는 false로 설정
    [SerializeField] private bool enableCsvLogging = true;

    // 몇 에피소드마다 파일에 쓸지 설정
    // 1 = 매 에피소드마다 저장 (안전하지만 느림)
    // 10 = 10에피소드마다 저장 (학습 속도 영향 최소화)
    [SerializeField] private int flushInterval = 10;

    private bool episodeDone;
    private float episodeStartTime;
    private Coroutine delayedResetRoutine;

    // CSV 관련
    private int episodeCount = 0;
    private string csvPath;
    private StringBuilder csvBuffer = new StringBuilder();

    // ── 행동 카운터 (AgentA) ──────────────────────────────
    private int agentA_Attack  = 0;
    private int agentA_Block   = 0;
    private int agentA_Dodge   = 0;
    private int agentA_Hit     = 0;
    private int agentA_Blocked = 0;

    // ── 행동 카운터 (AgentB) ──────────────────────────────
    private int agentB_Attack  = 0;
    private int agentB_Block   = 0;
    private int agentB_Dodge   = 0;
    private int agentB_Hit     = 0;
    private int agentB_Blocked = 0;

    // 싱글톤 접근용
    public static EpisodeManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        FillDefaultReferences();
    }

    private void Start()
    {
        if (enableCsvLogging)
        {
            InitializeCsvLog();
        }

        ResetEpisode();
    }

    private void Update()
    {
        CheckEpisodeEnd();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    // 앱 종료 시 버퍼에 남은 데이터 강제 저장
    private void OnApplicationQuit()
    {
        FlushBuffer();
    }

    // ── 행동 카운터 외부 호출 함수 ──────────────────────────────
    // CombatActionController에서 호출

    public void LogAttack(string agentName)
    {
        if (IsAgentA(agentName)) agentA_Attack++;
        else if (IsAgentB(agentName)) agentB_Attack++;
    }

    public void LogBlock(string agentName)
    {
        if (IsAgentA(agentName)) agentA_Block++;
        else if (IsAgentB(agentName)) agentB_Block++;
    }

    public void LogDodge(string agentName)
    {
        if (IsAgentA(agentName)) agentA_Dodge++;
        else if (IsAgentB(agentName)) agentB_Dodge++;
    }

    public void LogHit(string agentName)
    {
        if (IsAgentA(agentName)) agentA_Hit++;
        else if (IsAgentB(agentName)) agentB_Hit++;
    }

    public void LogBlocked(string agentName)
    {
        if (IsAgentA(agentName)) agentA_Blocked++;
        else if (IsAgentB(agentName)) agentB_Blocked++;
    }

    // ── 에이전트 이름 확인 ──────────────────────────────

    private bool IsAgentA(string agentName)
    {
        return agentA != null && agentName == agentA.name;
    }

    private bool IsAgentB(string agentName)
    {
        return agentB != null && agentName == agentB.name;
    }

    // ── 에피소드 관리 ──────────────────────────────

    public void ResetEpisode()
    {
        if (delayedResetRoutine != null)
        {
            StopCoroutine(delayedResetRoutine);
            delayedResetRoutine = null;
        }

        episodeDone = false;
        episodeStartTime = Time.time;

        ResetCounters();
        ResetAgent(agentA, spawnPointA);
        ResetAgent(agentB, spawnPointB);

        Debug.Log("Episode reset.");
    }

    public bool CheckEpisodeEnd()
    {
        if (episodeDone) return true;

        bool agentADead = agentA != null && agentA.IsDead;
        bool agentBDead = agentB != null && agentB.IsDead;

        if (agentADead || agentBDead)
        {
            if (agentADead && agentBDead)
                EndEpisode("draw");
            else if (agentBDead)
                EndEpisode($"{agentA.name} wins");
            else
                EndEpisode($"{agentB.name} wins");

            return true;
        }

        if (maxEpisodeTime > 0f && Time.time - episodeStartTime >= maxEpisodeTime)
        {
            EndEpisode("timeout draw");
            return true;
        }

        return false;
    }

    public bool IsEpisodeDone()
    {
        return episodeDone;
    }

    private void EndEpisode(string result)
    {
        episodeDone = true;
        float duration = Time.time - episodeStartTime;
        episodeCount++;

        Debug.Log($"Episode ended: {result}.");

        if (enableCsvLogging)
        {
            BufferCsvLine(result, duration);

            // flushInterval마다 파일에 쓰기
            if (episodeCount % flushInterval == 0)
            {
                FlushBuffer();
            }
        }

        if (delayedResetRoutine == null)
        {
            delayedResetRoutine = StartCoroutine(ResetAfterDelay());
        }
    }

    private void ResetCounters()
    {
        agentA_Attack = agentA_Block = agentA_Dodge = 0;
        agentA_Hit = agentA_Blocked = 0;
        agentB_Attack = agentB_Block = agentB_Dodge = 0;
        agentB_Hit = agentB_Blocked = 0;
    }

    // ── CSV 로깅 함수 ──────────────────────────────

    private void InitializeCsvLog()
    {
        string directory = Application.dataPath + "/Results";
        Directory.CreateDirectory(directory);

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        csvPath = directory + $"/episode_log_{timestamp}.csv";

        string header =
            "Episode,Result,Duration," +
            "A_Attack,A_Block,A_Dodge,A_Hit,A_Blocked," +
            "B_Attack,B_Block,B_Dodge,B_Hit,B_Blocked\n";

        File.WriteAllText(csvPath, header);
        Debug.Log($"[EpisodeManager] CSV 로그 시작: {csvPath}");
    }

    // 버퍼에 한 줄 추가 (파일 쓰기 없음)
    private void BufferCsvLine(string result, float duration)
    {
        csvBuffer.Append(
            $"{episodeCount},{result},{duration:F2}," +
            $"{agentA_Attack},{agentA_Block},{agentA_Dodge}," +
            $"{agentA_Hit},{agentA_Blocked}," +
            $"{agentB_Attack},{agentB_Block},{agentB_Dodge}," +
            $"{agentB_Hit},{agentB_Blocked}\n");
    }

    // 버퍼를 파일에 쓰고 초기화
    private void FlushBuffer()
    {
        if (string.IsNullOrEmpty(csvPath) || csvBuffer.Length == 0) return;
        File.AppendAllText(csvPath, csvBuffer.ToString());
        csvBuffer.Clear();
    }

    // ── 에이전트 리셋 ──────────────────────────────

    private IEnumerator ResetAfterDelay()
    {
        yield return new WaitForSeconds(resetDelay);
        delayedResetRoutine = null;
        ResetEpisode();
    }

    private void ResetAgent(CombatCharacter agent, Transform spawnPoint)
    {
        if (agent == null) return;

        agent.ActionController?.ResetActionState();
        agent.CooldownSystem?.ResetCooldowns();
        agent.ResetCharacter();

        Rigidbody body = agent.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (spawnPoint != null)
        {
            agent.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
        }
    }

    private void FillDefaultReferences()
    {
        if (agentA == null)
        {
            GameObject found = GameObject.Find("Agent_A");
            agentA = found != null ? found.GetComponent<CombatCharacter>() : null;
        }

        if (agentB == null)
        {
            GameObject found = GameObject.Find("Agent_B");
            agentB = found != null ? found.GetComponent<CombatCharacter>() : null;
        }
    }
}
