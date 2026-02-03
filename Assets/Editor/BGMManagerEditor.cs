#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(BGMManager))]
public class BGMManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 기본 인스펙터 먼저 그려주기
        DrawDefaultInspector();

        EditorGUILayout.Space();
        var mgr = (BGMManager)target;

        // Resources/bgm에서 다시 읽어오는 버튼
        EditorGUILayout.Space();
        if (GUILayout.Button("Resources/bgm에서 다시 불러오기 (Refresh)"))
        {
            mgr.RefreshFromResources();
            EditorUtility.SetDirty(mgr);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("BGM 리스트 (클릭해서 재생)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Play 버튼은 재생 중(Play Mode)에서 BGMManager에서 AudioSource로 재생됩니다.", MessageType.Info);

        if (mgr.bgmList == null || mgr.bgmList.Count == 0)
        {
            EditorGUILayout.LabelField("로드된 bgm가 없습니다. Resources/bgm 폴더를 확인하세요.");
            return;
        }

        // 각 bgm마다 한 줄씩: [키 이름] [클립 이름] [Play 버튼]
        for (int i = 0; i < mgr.bgmList.Count; i++)
        {
            var entry = mgr.bgmList[i];
            if (entry == null) continue;

            EditorGUILayout.BeginHorizontal();

            // key
            EditorGUILayout.LabelField(entry.key, GUILayout.Width(120));

            // 클립 필드 (읽기 전용)
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(entry.clip, typeof(AudioClip), false);
            EditorGUI.EndDisabledGroup();

            // Play 버튼
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("Play", GUILayout.Width(60)))
            {
                mgr.PlayByIndex(i);
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
