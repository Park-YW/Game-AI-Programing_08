using UnityEngine;

public class camera : MonoBehaviour
{
    [Header("타겟 설정")]
    public Transform targetA; // 카메라가 쫓아다닐 기준 (예: 플레이어)
    public Transform targetB; // 바라볼 대상 (예: 적, 보스)

    [Header("카메라 위치 설정")]
    public float distance = 4f; // A의 등 뒤로 얼마나 떨어질 것인가?
    public float height = 1.5f; // A를 기준으로 얼마나 높이 띄울 것인가?

    [Header("부드러움(스무딩) 설정")]
    public float positionSmoothSpeed = 5f; // 위치 이동 속도
    public float rotationSmoothSpeed = 5f; // 회전 속도

    void LateUpdate()
    {
        // 타겟이 하나라도 없으면 작동하지 않음
        if (targetA == null || targetB == null) return;

        // 1. A에서 B를 향하는 방향 벡터 계산 (Y축 높낮이는 무시하여 평면 기준으로 등 뒤를 잡음)
        Vector3 directionToB = targetB.position - targetA.position;
        directionToB.y = 0;
        directionToB.Normalize();

        // 2. 카메라의 목표 위치 계산
        // A의 위치에서 B의 반대 방향으로 distance만큼 뒤로 가고, 위로 height만큼 올라감
        Vector3 desiredPosition = targetA.position - (directionToB * distance) + (Vector3.up * height);

        // 3. 부드럽게 위치 이동 (Lerp)
        transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * positionSmoothSpeed);

        // 4. 부드럽게 회전 처리 (카메라가 B를 바라보도록)
        // 현재 카메라 위치에서 targetB를 향하는 방향
        Vector3 lookDirection = targetB.position - transform.position;

        if (lookDirection != Vector3.zero)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(lookDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, Time.deltaTime * rotationSmoothSpeed);
        }
    }
}