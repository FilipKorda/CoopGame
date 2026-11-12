using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Puts a cutscene stand-in's hands on two points while this object is switched on (a Timeline Activation clip):
    /// e.g. both hands on the bottom edge of a gate the characters lift. The points can move (put them under
    /// something the Timeline moves) and the hands follow; switched off, the hands go back to the normal pose.
    /// </summary>
    public class CutsceneHandGrip : MonoBehaviour
    {
        [SerializeField] PlayerCubeRig figure;
        [SerializeField] Transform leftHand;
        [SerializeField] Transform rightHand;

        void OnEnable()
        {
            if (figure != null) figure.SetHandTargets(leftHand, rightHand, true);
        }

        void OnDisable()
        {
            if (figure != null) figure.SetHandTargets(null, null, false);
        }

        /// <summary>Editor setup.</summary>
        public void Setup(PlayerCubeRig rig, Transform left, Transform right)
        {
            figure = rig;
            leftHand = left;
            rightHand = right;
        }
    }
}
