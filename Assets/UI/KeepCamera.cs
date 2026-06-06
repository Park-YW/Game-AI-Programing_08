using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.GraphicsBuffer;

public class KeepCamera : MonoBehaviour
{
    private Camera mainCam;

    void Start()
    {
        // 씬에 있는 메인 카메라를 자동으로 찾아서 연결합니다.
        mainCam = Camera.main;
    }

    void LateUpdate()
    {
        if (mainCam == null) return;

        // UI 캔버스의 앞면(forward)이 카메라가 바라보는 앞면과 똑같은 방향을 향하게 합니다.
        transform.forward = mainCam.transform.forward;
    }
    

}
