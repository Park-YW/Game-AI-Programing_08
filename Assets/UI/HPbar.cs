using UnityEngine;
using UnityEngine.UI;

public class HPbar : MonoBehaviour
{
    [SerializeField] private Image barImage;
    [SerializeField] private CombatCharacter target;

    

    void LateUpdate()
    {
        if (target != null)
            barImage.fillAmount = target.CurrentHealthRatio;
    }
}
