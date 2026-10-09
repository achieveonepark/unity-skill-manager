using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Achieve.UnitySkillManager.Editor.Tests
{
    public sealed class SkillManagerWindowTests
    {
        [Test]
        public void ToolkitWindowPreservesSearchAcrossLanguageChanges()
        {
            var hadPreference = EditorPrefs.HasKey("Achieve.Editor.Language");
            var previous = EditorPrefs.GetString("Achieve.Editor.Language");
            var window = ScriptableObject.CreateInstance<SkillManagerWindow>();
            try
            {
                SkillLocalization.SetLanguage(false);
                window.CreateGUI();
                var search = window.rootVisualElement.Q<ToolbarSearchField>("skill-search");
                Assert.That(search, Is.Not.Null);
                window.SetSearchFilter("event-tracer");
                Assert.That(window.rootVisualElement.Q<ListView>("skill-list").itemsSource.Count, Is.EqualTo(1));
                SkillLocalization.SetLanguage(true);
                window.CreateGUI();
                Assert.That(window.rootVisualElement.Q<ToolbarSearchField>("skill-search").value, Is.EqualTo("event-tracer"));
                Assert.That(window.rootVisualElement.Q<Button>("skill-sync-all").text, Is.EqualTo("모두 동기화"));
                Assert.That(window.rootVisualElement.Q<TextField>("skill-preview").value, Does.Contain("achieve-event-tracer"));
                Assert.That(window.titleContent.text, Is.EqualTo("Unity 스킬 관리자"));
            }
            finally
            {
                Object.DestroyImmediate(window);
                if (hadPreference) EditorPrefs.SetString("Achieve.Editor.Language", previous);
                else EditorPrefs.DeleteKey("Achieve.Editor.Language");
            }
        }
    }
}
