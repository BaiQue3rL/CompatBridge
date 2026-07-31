using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace CompatBridge.Core
{
    internal static class JsonFileStore
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string Serialize<T>(T value)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Utf8NoBom.GetString(stream.ToArray());
            }
        }

        public static T Deserialize<T>(string json)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream stream = new MemoryStream(Utf8NoBom.GetBytes(json)))
            {
                return (T)serializer.ReadObject(stream);
            }
        }

        public static T Read<T>(string path)
        {
            return Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
        }

        public static void WriteAtomic<T>(string path, T value)
        {
            WriteTextAtomic(path, Serialize(value));
        }

        public static void WriteTextAtomic(string path, string content)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, Path.GetRandomFileName());
            try
            {
                File.WriteAllText(temporaryPath, content, Utf8NoBom);
                ReplaceOrMove(temporaryPath, fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        public static void CopyAtomic(string source, string destination)
        {
            string fullDestination = Path.GetFullPath(destination);
            string directory = Path.GetDirectoryName(fullDestination);
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, Path.GetRandomFileName());
            try
            {
                File.Copy(source, temporaryPath, true);
                ReplaceOrMove(temporaryPath, fullDestination);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static void ReplaceOrMove(string temporaryPath, string destination)
        {
            if (File.Exists(destination))
            {
                string replaceBackup = destination + ".replace-backup";
                try
                {
                    File.Replace(temporaryPath, destination, replaceBackup, true);
                }
                finally
                {
                    if (File.Exists(replaceBackup))
                    {
                        File.Delete(replaceBackup);
                    }
                }
            }
            else
            {
                File.Move(temporaryPath, destination);
            }
        }

        public static string Sha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
