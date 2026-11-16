
using UnityEngine;
using System.Collections;

public class DoorController : MonoBehaviour
{
    [SerializeField] private Transform leftDoor;
    [SerializeField] private Transform rightDoor;

    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float animationDuration = 1f;
    public float AnimationDuration => animationDuration;

    private Quaternion leftClosedRotation;
    private Quaternion rightClosedRotation;
    
    private Quaternion leftOpenRotation;
    private Quaternion rightOpenRotation;

    private Coroutine animationCoroutine;
    private bool isOpen;

    private void Awake()
    {
        leftClosedRotation = leftDoor.localRotation;
        rightClosedRotation = rightDoor.localRotation;

        leftOpenRotation = leftClosedRotation * Quaternion.Euler(0f, -openAngle, 0f);
        rightOpenRotation = rightClosedRotation * Quaternion.Euler(0f, openAngle, 0f);
    }

    [ContextMenu("Open Door")]
    public void Open()
    {
        if (isOpen) return;

        isOpen = true;
        PlayAnimation(true);
    }

    [ContextMenu("Close Door")]
    public void Close()
    {
        if (!isOpen) return;

        isOpen = false;
        PlayAnimation(false);
    }

    public void Toggle()
    {
        if (isOpen)
            Close();
        else
            Open();
    }

    private void PlayAnimation(bool open)
    {
        if (animationCoroutine != null)
            StopCoroutine(animationCoroutine);

        animationCoroutine = StartCoroutine(AnimateDoors(
            open ? leftOpenRotation : leftClosedRotation,
            open ? rightOpenRotation : rightClosedRotation
        ));
    }

    private IEnumerator AnimateDoors(Quaternion leftTarget, Quaternion rightTarget)
    {
        Quaternion leftStart = leftDoor.localRotation;
        Quaternion rightStart = rightDoor.localRotation;

        float elapsedTime = 0f;

        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;

            float t = Mathf.Clamp01(elapsedTime / animationDuration);
            t = Mathf.SmoothStep(0f, 1f, t);

            leftDoor.localRotation = Quaternion.Slerp(leftStart, leftTarget, t);
            rightDoor.localRotation = Quaternion.Slerp(rightStart, rightTarget, t);

            yield return null;
        }

        leftDoor.localRotation = leftTarget;
        rightDoor.localRotation = rightTarget;

        animationCoroutine = null;
    }
}
