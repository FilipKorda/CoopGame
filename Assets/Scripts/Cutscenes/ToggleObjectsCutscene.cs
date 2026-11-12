using UnityEngine;

public class ToggleObjectsCutscene : MonoBehaviour
{
    public void ToggleObject(bool active)
    {
        gameObject.SetActive(active);
    }
}
