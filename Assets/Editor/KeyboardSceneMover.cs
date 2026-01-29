using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class KeyboardSceneMover
{
    private static bool _enabled = false;

    private const float BaseStep = 1f;

    static KeyboardSceneMover()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    // =========================
    // 메뉴 토글 (Ctrl + Alt + M)
    // =========================
    [MenuItem("Tools/Keyboard Move/Toggle %#&m")]
    private static void Toggle()
    {
        _enabled = !_enabled;
        Debug.Log($"[KeyboardSceneMover] {(_enabled ? "ON" : "OFF")}");
        SceneView.RepaintAll();
    }

    // =========================
    // Scene GUI
    // =========================
    private static void OnSceneGUI(SceneView sceneView)
    {
        DrawStatusOverlay();

        if (!_enabled)
            return;

        Event e = Event.current;
        if (e.type != EventType.KeyDown)
            return;

        Transform t = Selection.activeTransform;
        if (t == null)
            return;

        float step = BaseStep;
        if (e.shift) step *= 0.1f;
        if (e.control || e.command) step *= 10f;

        Vector3 pos = t.position;
        bool used = false;

        switch (e.keyCode)
        {
            case KeyCode.A: pos.x -= step; used = true; break;
            case KeyCode.D: pos.x += step; used = true; break;
            case KeyCode.W: pos.z += step; used = true; break;
            case KeyCode.S: pos.z -= step; used = true; break;
            case KeyCode.R: pos.y += step; used = true; break;
            case KeyCode.F: pos.y -= step; used = true; break;
        }

        if (used)
        {
            Undo.RecordObject(t, "Keyboard Scene Move");
            t.position = pos;

            // 기본 에디터 단축키 차단
            e.Use();
            SceneView.RepaintAll();
        }
    }

    // =========================
    // SceneView HUD 표시
    // =========================
    private static void DrawStatusOverlay()
    {
        Handles.BeginGUI();

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.alignment = TextAnchor.UpperLeft;
        boxStyle.fontSize = 11;
        boxStyle.normal.textColor = _enabled ? Color.green : Color.gray;

        Rect rect = new Rect(10, 10, 260, 80);

        string text =
            "Keyboard Scene Mover\n" +
            $"Status : {(_enabled ? "ON" : "OFF")}\n" +
            "A/D : X  |  W/S : Z  |  R/F : Y\n" +
            "Shift = x0.1 , Ctrl = x10";

        GUI.Box(rect, text, boxStyle);

        Handles.EndGUI();
    }
}
