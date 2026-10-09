using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Achieve.UnitySkillManager.Editor
{
    [InitializeOnLoad]
    public static class SkillManagerService
    {
        private static bool queued;
        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        public static event Action Changed;
        public static SkillSyncReport LastReport { get; private set; }

        static SkillManagerService()
        {
            Events.registeredPackages += _ => QueueSynchronization();
            QueueSynchronization();
        }

        public static List<PackageSkill> Discover() => SkillCatalog.Discover(
            PackageInfo.GetAllRegisteredPackages().Select(package =>
                new SkillPackage(package.name, package.version, package.resolvedPath, package.displayName)));

        public static SkillSyncReport SynchronizeAll()
        {
            LastReport = new SkillSynchronizer(ProjectRoot).Synchronize(Discover());
            Changed?.Invoke();
            return LastReport;
        }

        private static void QueueSynchronization()
        {
            if (queued || Application.isBatchMode) return;
            queued = true;
            EditorApplication.delayCall += AutomaticSynchronization;
        }

        private static void AutomaticSynchronization()
        {
            queued = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { QueueSynchronization(); return; }
            try
            {
                if (PackageInfo.GetAllRegisteredPackages().Length == 0) return;
                if (new SkillSynchronizer(ProjectRoot).Settings.autoSync) SynchronizeAll();
            }
            catch (Exception ex) { Debug.LogWarning("Unity Skill Manager: " + ex.Message); }
        }
    }
}
