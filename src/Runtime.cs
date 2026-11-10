using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DesktopAgenda
{
    internal static class DataLocation
    {
        public static string ResolvePortable(string appDirectory)
        {
            return Path.GetFullPath(Path.Combine(appDirectory, "PortableData"));
        }

        public static string PortableInstanceToken(string directory)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] path = Encoding.UTF8.GetBytes(directory.ToUpperInvariant());
                return "DesktopAgenda.Portable." + BitConverter.ToString(hash.ComputeHash(path)).Replace("-", "");
            }
        }

        public static string Resolve(bool testing, string appDirectory, string overrideDirectory)
        {
            if (testing) return Path.GetFullPath(String.IsNullOrEmpty(overrideDirectory) ? Path.Combine(appDirectory, "data") : overrideDirectory);
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (String.IsNullOrEmpty(root)) throw new IOException("无法找到当前用户的数据文件夹。");
            return Path.Combine(root, "DesktopAgenda", "data");
        }
        public static bool CopyLegacyIfMissing(string destinationDirectory, string oldDirectory)
        {
            string destination = Path.Combine(destinationDirectory, "agenda.json"), source = Path.Combine(oldDirectory, "agenda.json");
            if (String.Equals(Path.GetFullPath(destinationDirectory), Path.GetFullPath(oldDirectory), StringComparison.OrdinalIgnoreCase)) return false;
            if (File.Exists(destination) || File.Exists(destination + ".bak")) return false;
            if (!File.Exists(source) && !File.Exists(source + ".bak")) return false;
            Store reader = new Store(oldDirectory); string selected = source;
            try { reader.ReadBackup(source); }
            catch { if (!File.Exists(source + ".bak")) throw; reader.ReadBackup(source + ".bak"); selected = source + ".bak"; }
            Directory.CreateDirectory(destinationDirectory);
            string temporary = destination + ".importing-" + Guid.NewGuid().ToString("N");
            File.Copy(selected, temporary, false); File.Move(temporary, destination);
            if (File.Exists(source + ".bak")) File.Copy(source + ".bak", destination + ".bak", false);
            return true;
        }
    }

    internal sealed class InstanceLock : IDisposable
    {
        private readonly Mutex mutex;
        private readonly EventWaitHandle reveal;
        private RegisteredWaitHandle listener;
        public bool IsPrimary { get; private set; }
        public InstanceLock(string token)
        {
            bool created; mutex = new Mutex(true, "Local\\" + token + ".Mutex", out created); IsPrimary = created;
            reveal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\" + token + ".Reveal");
            if (!IsPrimary) reveal.Set();
        }
        public bool ReceiveSignal(int milliseconds) { return reveal.WaitOne(milliseconds); }
        public void Listen(Form window, Action show)
        {
            if (!IsPrimary) return;
            listener = ThreadPool.RegisterWaitForSingleObject(reveal, delegate
            {
                try { if (!window.IsDisposed && window.IsHandleCreated) window.BeginInvoke((MethodInvoker)delegate { if (!window.IsDisposed) show(); }); }
                catch (InvalidOperationException) { }
            }, null, Timeout.Infinite, false);
        }
        public void Dispose()
        {
            if (listener != null) listener.Unregister(null);
            reveal.Dispose(); if (IsPrimary) mutex.ReleaseMutex(); mutex.Dispose();
        }
    }

    internal static class TimeText
    {
        private static bool Digits(string text) { if (text.Length == 0) return false; foreach (char c in text) if (c < '0' || c > '9') return false; return true; }
        public static bool TryParse(string value, out int hour, out int minute)
        {
            hour = minute = 0; string text = (value ?? "").Trim();
            text = text.Replace("：", ":").Replace("点", ":").Replace("分", "").Replace("。", ":").Replace(".", ":").Replace(" ", "");
            if (text.IndexOf(':') >= 0)
            {
                string[] parts = text.Split(':');
                if (parts.Length != 2 || !Digits(parts[0]) || !Digits(parts[1])) return false;
                if (!int.TryParse(parts[0], out hour) || !int.TryParse(parts[1], out minute)) return false;
            }
            else
            {
                if (!Digits(text) || text.Length > 4) return false;
                if (text.Length <= 2) { if (!int.TryParse(text, out hour)) return false; }
                else if (!int.TryParse(text.Substring(0, text.Length - 2), out hour) || !int.TryParse(text.Substring(text.Length - 2), out minute)) return false;
            }
            return hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59;
        }
    }
}
