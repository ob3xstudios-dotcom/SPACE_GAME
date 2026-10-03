using System;
using System.IO;
using UnityEngine;

namespace Game.Progression
{
    public static class ProgressionSaveSystem
    {
        private const string SaveFileName = "spaceworld_progression.json";

        public static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public static ProgressionSaveData LoadOrCreate()
        {
            string path = SavePath;
            if (!File.Exists(path))
                return CreateEmptyData();

            try
            {
                string json = File.ReadAllText(path);
                ProgressionSaveData data = JsonUtility.FromJson<ProgressionSaveData>(json);
                if (data == null)
                    throw new InvalidDataException("The progression save did not contain data.");

                data.Normalize();
                return data;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[PROGRESSION SAVE] Could not load '{path}'. Starting with empty data. " +
                    exception.Message);
                return CreateEmptyData();
            }
        }

        public static bool Save(ProgressionSaveData data)
        {
            if (data == null)
                return false;

            data.Normalize();
            string path = SavePath;
            string temporaryPath = path + ".tmp";

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temporaryPath, path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        ReplaceWithCopy(temporaryPath, path);
                    }
                    catch (IOException)
                    {
                        ReplaceWithCopy(temporaryPath, path);
                    }
                }
                else
                {
                    File.Move(temporaryPath, path);
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[PROGRESSION SAVE] Could not write '{path}'. {exception.Message}");
                TryDeleteTemporaryFile(temporaryPath);
                return false;
            }
        }

        private static ProgressionSaveData CreateEmptyData()
        {
            ProgressionSaveData data = new ProgressionSaveData();
            data.Normalize();
            return data;
        }

        private static void ReplaceWithCopy(string temporaryPath, string path)
        {
            File.Copy(temporaryPath, path, true);
            File.Delete(temporaryPath);
        }

        private static void TryDeleteTemporaryFile(string temporaryPath)
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception)
            {
                // Preserve the real save. A stale temporary file is harmless.
            }
        }
    }
}
