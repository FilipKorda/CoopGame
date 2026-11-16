using Unity.Netcode;
using UnityEngine;
using System.Collections;

namespace CoopGame
{
    public class DoorSequence : NetworkBehaviour
    {
        [SerializeField] DoorController[] doors;
        [SerializeField] float pause = 0.5f;

        readonly NetworkVariable<bool> opened = new();
        readonly NetworkVariable<double> openedAt = new();

        Coroutine sequenceCoroutine;

        public bool IsOpened => opened.Value;

        double Now => NetworkManager != null
            ? NetworkManager.ServerTime.Time
            : Time.timeAsDouble;

        public override void OnNetworkSpawn()
        {
            if (opened.Value)
                sequenceCoroutine = StartCoroutine(OpenSequence());
        }

        public void ServerOpen()
        {
            if (!IsServer || opened.Value) return;

            openedAt.Value = Now;
            opened.Value = true;

            if (sequenceCoroutine == null)
                sequenceCoroutine = StartCoroutine(OpenSequence());
        }

        IEnumerator OpenSequence()
        {
            for (int i = 0; i < doors.Length; i++)
            {
                if (doors[i] == null)
                    continue;

                double targetTime = openedAt.Value;

                for (int j = 0; j < i; j++)
                {
                    if (doors[j] != null)
                        targetTime += doors[j].AnimationDuration + pause;
                }

                while (Now < targetTime)
                    yield return null;

                doors[i].Open();

                yield return new WaitForSeconds(doors[i].AnimationDuration + pause);
            }

            sequenceCoroutine = null;
        }

        public override void OnNetworkDespawn()
        {
            if (sequenceCoroutine != null)
            {
                StopCoroutine(sequenceCoroutine);
                sequenceCoroutine = null;
            }
        }
    }
}