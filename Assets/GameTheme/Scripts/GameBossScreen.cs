using UnityEngine;

namespace AcRoguelike.GameTheme
{
    /// <summary>The LCD keeps dropping a four-cell piece with the boss's slow-then-fast rhythm.</summary>
    public sealed class GameBossScreen : MonoBehaviour
    {
        public Transform fallingPiece;
        Vector3 rest;
        float elapsed;
        void Awake() { if (fallingPiece) rest = fallingPiece.localPosition; }
        void Update()
        {
            if (!fallingPiece || Time.deltaTime <= 0) return;
            elapsed = (elapsed + Time.deltaTime) % 2.1f;
            float drop = elapsed < 1.5f ? elapsed * .13f : .195f + (elapsed - 1.5f) * .7f;
            fallingPiece.localPosition = rest + Vector3.down * drop;
        }
    }
}
