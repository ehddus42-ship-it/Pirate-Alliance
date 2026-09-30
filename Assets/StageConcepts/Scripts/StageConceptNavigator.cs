using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace AcRoguelike.StageConcepts
{
    /// <summary>Small, opt-in lab controls shared by the four standalone concept scenes.</summary>
    public sealed class StageConceptNavigator : MonoBehaviour
    {
        public int theme;
        public Font font;
        public bool panelOpen;
        public static readonly string[] Keys = { "Forest", "Digital", "Ruins", "Cave" };
        public static readonly string[] Titles = { "달빛 고목의 숲", "프로그램 감옥", "멸망한 지구", "심연의 수정 동굴" };
        GUIStyle label, button;

        void Update()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.tabKey.wasPressedThisFrame) panelOpen = !panelOpen;
            if (keys.f1Key.wasPressedThisFrame) Open(0);
            else if (keys.f2Key.wasPressedThisFrame) Open(1);
            else if (keys.f3Key.wasPressedThisFrame) Open(2);
            else if (keys.f4Key.wasPressedThisFrame) Open(3);
        }

        void Open(int index)
        {
            Time.timeScale = 1;
            SceneManager.LoadScene("StageConcept_" + Keys[index]);
        }

        void OnGUI()
        {
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, wordWrap = true };
                label.normal.textColor = new Color(.79f, .89f, .92f);
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16 };
            }
            float x = Screen.width - 264;
            float y = Mathf.Min(152, Mathf.Max(16, Screen.height - 340));
            if (GUI.Button(new Rect(x, y, 246, 30), "테마 실험실  [TAB]", button)) panelOpen = !panelOpen;
            if (!panelOpen) return;
            GUI.Box(new Rect(x, y + 34, 246, 244), GUIContent.none);
            GUI.Label(new Rect(x + 12, y + 41, 222, 52), "스테이지 1 규모 · 7개 방 중 5개\n선택하면 해당 던전을 새로 시작합니다.", label);
            for (int i = 0; i < Keys.Length; i++)
                if (GUI.Button(new Rect(x + 12, y + 98 + i * 32, 222, 28), "F" + (i + 1) + "  " + Titles[i] + (i == theme ? "  •" : ""), button)) Open(i);
            GUI.Label(new Rect(x + 12, y + 232, 222, 42), "WASD 이동 · SHIFT 대시 · 휠 확대", label);
        }
    }
}
