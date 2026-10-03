using System;
using System.IO;
using UnityEngine;

namespace Game.Progress
{
    public static class GameSaveSystem
    {
        private const string SaveFileName = "spaceworld_save.json";

        public static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public static GameProgressData LoadOrCreate()
        {
            string path = SavePath;
            if (!File.Exists(path))
                return CreateEmptyData();

            try
            {
                string json = File.ReadAllText(path);
                GameProgressData data = JsonUtility.FromJson<GameProgressData>(json);
                if (data == null)
                    throw new InvalidDataException("The save JSON did not contain progress data.");

                data.Normalize();
                return data;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SAVE] Could not load '{path}'. Starting with empty progress. {exception.Message}");
                return CreateEmptyData();
            }
        }

        public static bool Save(GameProgressData data)
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
                Debug.LogWarning($"[SAVE] Could not write '{path}'. {exception.Message}");
                TryDeleteTemporaryFile(temporaryPath);
                return false;
            }
        }

        private static GameProgressData CreateEmptyData()
        {
            GameProgressData data = new GameProgressData();
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
                // Preserve the original save; a stale temporary file is harmless.
            }
        }
    }
}
