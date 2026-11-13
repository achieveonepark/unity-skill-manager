using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Achieve.UnitySkillManager.Editor
{
    public sealed class SkillManagerWindow : EditorWindow
    {
        [SerializeField] private string selectedKey;
        [SerializeField] private string searchText = "";
        private List<PackageSkill> skills = new List<PackageSkill>();
        private List<PackageSkill> visible = new List<PackageSkill>();
        private SkillSynchronizer synchronizer;
        private VisualElement details;
        private ListView list;
        private Label count;
        private SkillSyncReport report;
        private IVisualElementScheduledItem languageMonitor;
        private bool displayedKorean;

        [MenuItem("Tools/Achieve/Unity Skill Manager")]
        public static void Open()
        {
            var window = GetWindow<SkillManagerWindow>();
            window.titleContent = new GUIContent("Unity Skill Manager");
            window.minSize = new Vector2(720, 380);
            window.Show();
        }

        private void OnEnable() => SkillManagerService.Changed += Refresh;
        private void OnDisable()
        {
            SkillManagerService.Changed -= Refresh;
            languageMonitor?.Pause();
        }

        public void CreateGUI()
        {
            languageMonitor?.Pause();
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("sm-root");
            root.EnableInClassList("light-theme", !EditorGUIUtility.isProSkin);
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SkillManagerWindow).Assembly);
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>((package?.assetPath ?? "Packages/com.achive.unity-skill-manager") + "/Editor/UI/Styles/SkillManager.uss");
            if (style != null && !root.styleSheets.Contains(style)) root.styleSheets.Add(style);
            displayedKorean = SkillLocalization.IsKorean;
            titleContent = new GUIContent(T("Unity Skill Manager", "Unity 스킬 관리자"));
            try
            {
                synchronizer = new SkillSynchronizer(SkillManagerService.ProjectRoot);
                skills = SkillManagerService.Discover();
            }
            catch (Exception ex)
            {
                root.Add(new HelpBox(ex.Message, HelpBoxMessageType.Error));
                root.Add(new Button(CreateGUI) { text = T("Retry", "다시 시도") });
                return;
            }

            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(Refresh) { name = "skill-refresh", text = T("Refresh", "새로 고침") });
            toolbar.Add(new ToolbarButton(SyncAll) { name = "skill-sync-all", text = T("Synchronize all", "모두 동기화") });
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; toolbar.Add(spacer);
            toolbar.Add(SkillLocalization.Selector(CreateGUI));
            root.Add(toolbar);

            var settings = new VisualElement(); settings.AddToClassList("sm-settings");
            AddToggle(settings, "Codex", "skill-codex", synchronizer.Settings.codex, value => synchronizer.Settings.codex = value);
            AddToggle(settings, "Claude Code", "skill-claude", synchronizer.Settings.claude, value => synchronizer.Settings.claude = value);
            AddToggle(settings, T("Automatic sync", "자동 동기화"), "skill-auto-sync", synchronizer.Settings.autoSync, value => synchronizer.Settings.autoSync = value);
            root.Add(settings);

            var intro = new Label(T("Manage skills supplied by installed packages.", "설치된 패키지의 스킬을 프로젝트에서 관리합니다."));
            intro.AddToClassList("sm-intro"); root.Add(intro);
            if (report != null || SkillManagerService.LastReport != null)
            {
                var last = report ?? SkillManagerService.LastReport;
                var message = T($"Updated {last.Updated} · Removed {last.Removed}", $"등록·갱신 {last.Updated} · 정리 {last.Removed}");
                if (!last.Succeeded) message += "\n" + T("Some skills were preserved. Review the selected skill and existing files.", "일부 스킬을 보존했습니다. 스킬 상태와 기존 파일을 확인하세요.");
                var notice = new HelpBox(message, last.Succeeded ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning) { name = "skill-report", tooltip = string.Join("\n", last.Issues) };
                notice.AddToClassList("sm-notice"); root.Add(notice);
            }

            var split = new TwoPaneSplitView(0, 355, TwoPaneSplitViewOrientation.Horizontal);
            split.AddToClassList("sm-workspace");
            var left = new VisualElement(); left.AddToClassList("sm-list-panel");
            count = new Label(); count.AddToClassList("sm-section"); left.Add(count);
            var search = new ToolbarSearchField { name = "skill-search", value = searchText, tooltip = T("Search skills and packages", "스킬·패키지 검색") };
            search.AddToClassList("sm-search");
            search.RegisterValueChangedCallback(change => SetSearchFilter(change.newValue)); left.Add(search);
            list = new ListView { name = "skill-list", fixedItemHeight = 66, selectionType = SelectionType.Single, makeItem = MakeRow, bindItem = BindRow };
            list.AddToClassList("sm-list");
            list.selectionChanged += items =>
            {
                var selected = items.OfType<PackageSkill>().FirstOrDefault();
                selectedKey = selected?.Key;
                DrawDetails(selected);
            };
            left.Add(list); split.Add(left);
            details = new ScrollView(); details.AddToClassList("sm-details"); split.Add(details);
            root.Add(split);
            Filter();
            languageMonitor = root.schedule.Execute(() => { if (displayedKorean != SkillLocalization.IsKorean) CreateGUI(); }).Every(400);
        }

        private void AddToggle(VisualElement parent, string label, string name, bool value, Action<bool> change)
        {
            var toggle = new Toggle(label) { name = name, value = value };
            toggle.RegisterValueChangedCallback(evt =>
            {
                change(evt.newValue);
                synchronizer.SaveSettings();
                if (synchronizer.Settings.autoSync) SyncAll();
            });
            parent.Add(toggle);
        }

        private void Filter()
        {
            visible = skills.Where(skill => string.IsNullOrEmpty(searchText) ||
                (skill.Name + " " + skill.Package.Name + " " + skill.Description).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            count.text = T("Package skills", "패키지 스킬") + "  " + visible.Count;
            list.itemsSource = visible;
            list.Rebuild();
            var index = visible.FindIndex(skill => skill.Key == selectedKey);
            if (index < 0 && visible.Count > 0) index = 0;
            if (index >= 0) { selectedKey = visible[index].Key; list.SetSelection(index); DrawDetails(visible[index]); }
            else DrawDetails(null);
        }

        private VisualElement MakeRow()
        {
            var row = new VisualElement(); row.AddToClassList("sm-row");
            foreach (var name in new[] { "skill-name", "skill-package", "skill-state" }) row.Add(new Label { name = name });
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            var skill = visible[index];
            row.Q<Label>("skill-name").text = skill.Name;
            row.Q<Label>("skill-package").text = skill.Package.DisplayName + "  " + skill.Package.Version;
            row.Q<Label>("skill-state").text = !synchronizer.IsIncluded(skill) ? T("Excluded", "동기화 제외") :
                "Codex: " + State(skill, SkillTarget.Codex) + "  /  Claude: " + State(skill, SkillTarget.Claude);
            row.tooltip = skill.Error ?? skill.Description;
        }

        private string State(PackageSkill skill, SkillTarget target)
        {
            if (!skill.Applies(target)) return T("Not provided", "제공 안 함");
            try { return SkillLocalization.State(synchronizer.GetState(skill, target)); }
            catch (IOException) { return T("Path conflict", "경로 충돌"); }
            catch (UnauthorizedAccessException) { return T("Access denied", "접근 불가"); }
        }

        private void DrawDetails(PackageSkill skill)
        {
            details.Clear();
            if (skill == null)
            {
                var empty = new Label(T("No package skills found.\nAdd a package containing Skills~/<name>/SKILL.md.", "패키지 스킬이 없습니다.\nSkills~/<이름>/SKILL.md를 포함한 패키지를 설치하세요."));
                empty.AddToClassList("sm-empty"); details.Add(empty); return;
            }
            var title = new Label(skill.Name); title.AddToClassList("sm-title"); details.Add(title);
            var provider = new Label(skill.Package.DisplayName + "  " + skill.Package.Version);
            provider.AddToClassList("sm-provider"); details.Add(provider);
            var description = new Label(skill.Description ?? skill.Error); description.AddToClassList("sm-description"); details.Add(description);
            if (!skill.IsValid) { details.Add(new HelpBox(skill.Error, HelpBoxMessageType.Error)); return; }
            var states = new VisualElement(); states.AddToClassList("sm-states");
            states.Add(new Label("Codex · " + State(skill, SkillTarget.Codex)) { name = "skill-codex-state" });
            states.Add(new Label("Claude Code · " + State(skill, SkillTarget.Claude)) { name = "skill-claude-state" });
            details.Add(states);
            var actions = new VisualElement(); actions.AddToClassList("sm-actions");
            actions.Add(new Button(() =>
            {
                synchronizer.SetIncluded(skill, true);
                report = synchronizer.Synchronize(skills.Where(item => item.Key == skill.Key).ToList(), false);
                Refresh();
            }) { name = "skill-sync-selected", text = T("Register / update", "등록·갱신") });
            actions.Add(new Button(() => { report = synchronizer.RemoveSkill(skill); Refresh(); })
            { name = "skill-remove-selected", text = T("Remove from project", "프로젝트에서 제거") });
            details.Add(actions);
            var hint = new Label(T("Locally modified and conflicting files are preserved. Removed skills stay excluded until registered again.",
                "직접 수정한 파일과 충돌하는 파일은 보존합니다. 제거한 스킬은 다시 등록할 때까지 동기화에서 제외됩니다."));
            hint.AddToClassList("sm-description"); details.Add(hint);
            var preview = new TextField(T("SKILL.md preview", "SKILL.md 미리보기"))
            { name = "skill-preview", multiline = true, isReadOnly = true, value = File.ReadAllText(Path.Combine(skill.SourcePath, "SKILL.md")) };
            preview.AddToClassList("sm-preview"); details.Add(preview);
        }

        private void SyncAll()
        {
            report = SkillManagerService.SynchronizeAll();
            Refresh();
        }

        public void SetSearchFilter(string filter)
        {
            searchText = filter ?? "";
            rootVisualElement.Q<ToolbarSearchField>("skill-search")?.SetValueWithoutNotify(searchText);
            if (list != null) Filter();
        }

        private void Refresh() { if (rootVisualElement != null) CreateGUI(); }
        private static string T(string english, string korean) => SkillLocalization.Text(english, korean);
    }
}
