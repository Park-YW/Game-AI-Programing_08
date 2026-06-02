using UnityEngine;

public class EnvSpawner : MonoBehaviour
{
    [Header("배치 설정")]
    public GameObject envPrefab; // 복제할 훈련장 프리팹 (TrainingArea)
    public int rows = 5;         // 가로로 배치할 개수
    public int columns = 5;      // 세로로 배치할 개수
    public float spacing = 20f;  // 훈련장 사이의 거리 (훈련장 크기에 맞게 조절하세요)

    [ContextMenu("1. 훈련장 자동 배치하기 (클릭)")]
    public void SpawnEnvironments()
    {
        if (envPrefab == null)
        {
            Debug.LogError("생성할 프리팹을 Inspector에 넣어주세요!");
            return;
        }

        for (int x = 0; x < rows; x++)
        {
            for (int z = 0; z < columns; z++)
            {
                Vector3 spawnPos = new Vector3(x * spacing, 0, z * spacing) + transform.position;
                GameObject newEnv = Instantiate(envPrefab, spawnPos, Quaternion.identity);

                newEnv.transform.parent = this.transform;
                newEnv.name = $"TrainingArea_{x}_{z}";
            }
        }
        Debug.Log($"총 {rows * columns}개의 훈련장 배치가 완료되었습니다.");
    }

    // 새롭게 추가된 삭제 기능
    [ContextMenu("2. 생성된 훈련장 모두 지우기 (클릭)")]
    public void ClearEnvironments()
    {
        int childCount = transform.childCount;

        // 자식 오브젝트를 지울 때는 리스트 순서가 꼬이지 않도록 항상 맨 뒤(역순)부터 지워야 합니다.
        for (int i = childCount - 1; i >= 0; i--)
        {
            // 에디터 상에서 지울 때는 Destroy 대신 DestroyImmediate를 사용합니다.
            DestroyImmediate(transform.GetChild(i).gameObject);
        }

        Debug.Log($"총 {childCount}개의 훈련장이 깔끔하게 삭제되었습니다.");
    }
}