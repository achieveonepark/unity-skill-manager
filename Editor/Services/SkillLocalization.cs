using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Achieve.UnitySkillManager.Editor
{
    public static class SkillLocalization
    {
        private const string PreferenceKey = "Achieve.Editor.Language";
        public static bool IsKorean => EditorPrefs.GetString(PreferenceKey,
            Application.systemLanguage == SystemLanguage.Korean ? "ko" : "en") == "ko";
        public static void SetLanguage(bool korean) => EditorPrefs.SetString(PreferenceKey, korean ? "ko" : "en");

        public static string Text(string english, string korean) => IsKorean ? korean : english;
        public static string State(SkillState state)
        {
            switch (state)
            {
                case SkillState.Missing: return Text("Not registered", "미등록");
                case SkillState.Current: return Text("Up to date", "최신");
                case SkillState.UpdateAvailable: return Text("Update available", "등록·갱신 필요");
                case SkillState.LocallyModified: return Text("Locally modified", "직접 수정됨");
                case SkillState.Conflict: return Text("Name conflict", "이름 충돌");
                default: return Text("Invalid skill", "잘못된 스킬");
            }
        }

        public static ToolbarMenu Selector(System.Action changed)
        {
            var menu = new ToolbarMenu { name = "skill-language", text = IsKorean ? "한국어" : "English" };
            menu.menu.AppendAction("한국어", _ => { SetLanguage(true); changed(); },
                _ => IsKorean ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            menu.menu.AppendAction("English", _ => { SetLanguage(false); changed(); },
                _ => !IsKorean ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            return menu;
        }
    }
}
