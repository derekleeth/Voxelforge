using System.IO;
using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;

namespace Voxelforge.Editor
{
    /// <summary>Menu items for world saves (Roadmap 3.4, D-017).</summary>
    public static class SaveFolderMenu
    {
        [MenuItem("Voxelforge/Saves/Open Save Folder", priority = 200)]
        static void OpenSaveFolder()
        {
            // The editor's saves (Saves-Editor) and a build's (Saves) sit side by side here.
            Directory.CreateDirectory(WorldSaveManager.SavesRoot);
            EditorUtility.RevealInFinder(WorldSaveManager.SavesRoot);
        }

        [MenuItem("Voxelforge/Saves/Delete Editor Saves", priority = 201)]
        static void DeleteEditorSaves()
        {
            string root = WorldSaveManager.SavesRoot;
            if (!Directory.Exists(root))
            {
                Debug.Log($"[Voxelforge] No editor saves to delete in {root}.");
                return;
            }
            if (!EditorUtility.DisplayDialog("Delete editor saves?",
                    $"This deletes every world saved while playing in the editor:\n{root}\n\nBuild saves are kept.", "Delete", "Cancel"))
                return;

            Directory.Delete(root, recursive: true);
            Debug.Log($"[Voxelforge] Deleted the editor saves in {root}. The next Play starts a new world.");
        }

        [MenuItem("Voxelforge/Saves/Delete Editor Saves", validate = true)]
        static bool CanDeleteEditorSaves() => !EditorApplication.isPlaying;
    }
}
