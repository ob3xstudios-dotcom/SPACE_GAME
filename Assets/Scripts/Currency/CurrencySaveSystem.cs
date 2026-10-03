using System;
using System.IO;
using UnityEngine;

namespace Game.Currency
{
    public static class CurrencySaveSystem
    {
        private const string SaveFileName = "spaceworld_economy.json";

        public static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public static CurrencySaveData LoadOrCreate()
        {
            string path = SavePath;
            if (!File.Exists(path))
                return CreateEmptyData();

            try
            {
                string json = File.ReadAllText(path);
                CurrencySaveData data = JsonUtility.FromJson<CurrencySaveData>(json);
                if (data == null)
                    throw new InvalidDataException("The economy save did not contain currency data.");

                data.Normalize();
                return data;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[CURRENCY SAVE] Could not load '{path}'. Starting with empty economy data. " +
                    exception.Message);
                return CreateEmptyData();
            }
        }

        public static bool Save(CurrencySaveData data)
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
                Debug.LogWarning($"[CURRENCY SAVE] Could not write '{path}'. {exception.Message}");
                TryDeleteTemporaryFile(temporaryPath);
                return false;
            }
        }

        private static CurrencySaveData CreateEmptyData()
        {
            CurrencySaveData data = new CurrencySaveData();
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
                // Preserve the original save. A stale temporary file is harmless.
            }
        }
    }
}
