using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace onchangedlib {
    [Flags]
    public enum PathSearchOptions {
        Default         = 0,
        TryPathEnvVar   = 1,
        PathMustExist   = 2,
        PathMustBeAFile = 4,
    }

    public class FileUtil {
        static bool TryPath(
            PathSearchOptions opts,
            string path)
        {
            if ((opts & PathSearchOptions.PathMustExist) == 0) {
                return true;
            }

            if (File.Exists(path)) {
                return true;
            }

            if ((opts & PathSearchOptions.PathMustBeAFile) == 0) {
                if (Directory.Exists(path)) {
                    return true;
                }
            }

            return false;
        }

        public static string GetValidPath(
            string path,
            string workingDir, // pass null to not check relative path
            PathSearchOptions opts)
        {
            try {
                if (Path.IsPathRooted(path)) {
                    if (TryPath(opts, path)) {
                        return path;
                    }
                } else {
                    // First try from working directory
                    if (workingDir != null) {
                        var path2 = Path.Combine(workingDir, path);
                        if (TryPath(opts, path2)) {
                            return path2;
                        }
                    }

                    // Then try from current directory (will still run from working directory)
                    {
                        var path2 = Path.Combine(Environment.CurrentDirectory, path);
                        if (TryPath(opts, path2)) {
                            return path2;
                        }
                    }

                    // Then try the path, but only if the path is a non-relative, file name only path
                    if ((opts & PathSearchOptions.TryPathEnvVar) != 0 &&
                        Path.GetDirectoryName(path) == "") {
                        var pathEnvVar = Environment.GetEnvironmentVariable("PATH");
                        var pathEnvVarDirs = pathEnvVar.Split(new char[] { ';' });
                        foreach (var dir in pathEnvVarDirs) {
                            var path2 = Path.Combine(dir, path);
                            if (TryPath(opts, path2)) {
                                return path2;
                            }
                        }
                    }
                }
            }
            catch (System.ArgumentException) {} // e.g., invalid path characters in string
            return null;
        }
    }

    public class ConsoleUtil {
        public static ConsoleColor HeaderColor;
        public static ConsoleColor ErrorColor;
        public static ConsoleColor WarningColor;

        public static void SetColors()
        {
            HeaderColor = ConsoleColor.Cyan;
            ErrorColor = ConsoleColor.Red;
            WarningColor = ConsoleColor.Yellow;

            // Background colors that look bad with Cyan text
            if (Array.IndexOf(new ConsoleColor[] {
                ConsoleColor.Gray,
                ConsoleColor.Green,
                ConsoleColor.Cyan,
                ConsoleColor.Yellow,
                ConsoleColor.White,
                }, Console.BackgroundColor) != -1) {
                HeaderColor = ConsoleColor.DarkCyan;
            }

            // Background colors that look bad with Red text
            if (Array.IndexOf(new ConsoleColor[] {
                ConsoleColor.DarkGreen,
                ConsoleColor.DarkCyan,
                ConsoleColor.DarkYellow,
                ConsoleColor.DarkGray,
                ConsoleColor.Red,
                ConsoleColor.Magenta,
                }, Console.BackgroundColor) != -1) {
                ErrorColor = ConsoleColor.DarkRed;
            }

            // Background colors that look bad with Yellow text
            if (Array.IndexOf(new ConsoleColor[] {
                ConsoleColor.Gray,
                ConsoleColor.Green,
                ConsoleColor.Cyan,
                ConsoleColor.Yellow,
                ConsoleColor.White,
                }, Console.BackgroundColor) != -1) {
                WarningColor = ConsoleColor.DarkYellow;
            }
        }

        public static void Warning(string message, params object[] objs)
        {
            Console.ForegroundColor = WarningColor;
            Console.Error.WriteLine(String.Format(message, objs));
            Console.ResetColor();
        }

        public static void Error(string message, params object[] objs)
        {
            Console.ForegroundColor = ErrorColor;
            Console.Error.WriteLine(String.Format(message, objs));
            Console.ResetColor();
        }

        public static void Abort(string message, params object[] objs)
        {
            Error(message, objs);
            Environment.Exit(1);
        }

        public static void Header(string message, params object[] objs)
        {
            Console.ForegroundColor = HeaderColor;
            Console.WriteLine(String.Format(message, objs));
            Console.ResetColor();
        }

        public static void WriteLine(string s)
        {
            for (int i0 = 0; ;)
            {
                int i = s.IndexOf("\u001b[", i0);
                if (i == -1)
                {
                    Console.WriteLine(s.Substring(i0));
                    return;
                }

                Console.Write(s.Substring(i0, i - i0));

                i += 2;
                int j = s.IndexOf('m', i);
                if (j == -1 || !int.TryParse(s.Substring(i, j - i), out var code))
                {
                    i0 = i;
                    continue;
                }
                i0 = j + 1;

                switch (code)
                {
                    case 0: Console.ResetColor(); break;
                    case 90: Console.ForegroundColor = ConsoleColor.DarkGray; break;
                    case 91: Console.ForegroundColor = ConsoleColor.Red; break;
                    case 92: Console.ForegroundColor = ConsoleColor.Green; break;
                    case 93: Console.ForegroundColor = ConsoleColor.Yellow; break;
                    default: Console.Write($"\u001b[{code}m"); break;
                }
            }
        }

        public static string RemoveEscapeCodes(string s)
        {
            for (int i = 0;;)
            {
                i = s.IndexOf("\u001b[", i);
                if (i == -1)
                {
                    return s;
                }

                int j = s.IndexOf('m', i + 2);
                if (j == -1)
                {
                    j = s.Length - 1;
                }
                s = s.Remove(i, j - i + 1);
            }
        }

        public static string EscapeBraces(string s)
        {
            return s
                .Replace("{", "{{")
                .Replace("}", "}}")
                ;
        }
    }

    public class RedirectOperators : List<RedirectOperators.Operator> {
        public class Operator {
            public string TargetPath;   // if null, target=stdout
            public bool AppendToTarget;
            public bool SourceIsStdout; // else stderr
            public Operator(string targetPath, bool appendToTarget, bool sourceIsStdout)
            {
                TargetPath     = targetPath;
                AppendToTarget = appendToTarget;
                SourceIsStdout = sourceIsStdout;
            }
        }

        public string Arguments { get {
            string arguments = "";
            foreach (var op in this) {
                if (op.TargetPath != null) {
                    var opr = op.AppendToTarget ? ">>" : ">";
                    var src = op.SourceIsStdout ? '1' : '2';
                    arguments += String.Format(" {0}{1} {2}", src, opr, op.TargetPath);
                }
            }
            return arguments;
        }}
    }

    public class Command {
        public string WorkingDir;
        public string ExePath;
        public string ExeArgs;
        public RedirectOperators StdoutRedirectOperators;
        public RedirectOperators StderrRedirectOperators;
        public bool exeFound_;

        public Command(
            string workingDir,
            string[] args,
            int argIndex)
        {
            WorkingDir              = workingDir;
            ExePath                 = args[argIndex++];
            ExeArgs                 = "";
            StdoutRedirectOperators = new RedirectOperators();
            StderrRedirectOperators = new RedirectOperators();
            exeFound_               = false;

            var redirectArgs = new List<string>();
            for ( ; argIndex < args.Length; ++argIndex) {
                AddArgument(args[argIndex], redirectArgs);
            }

            // Create redirect operator lists from arguments
            var numRedirectArgs = redirectArgs.Count;
            if ((numRedirectArgs & 1) == 1) { --numRedirectArgs; }
            for (int i = 0; i < numRedirectArgs; i += 2) {
                var op = redirectArgs[i];
                var stdoutNew = op == ">"  || op == "1>";
                var stdoutApp = op == ">>" || op == "1>>";
                var stderrNew =               op == "2>";
                var stderrApp =               op == "2>>";
                if (stdoutNew || stdoutApp) {
                    StdoutRedirectOperators.Add(new RedirectOperators.Operator(
                        redirectArgs[i + 1],
                        stdoutApp,
                        true));
                } else {
                    StderrRedirectOperators.Add(new RedirectOperators.Operator(
                        redirectArgs[i + 1],
                        stderrApp,
                        false));
                }
            }

            // If they aren't redirected, redirect stdout and stderr to console
            if (StdoutRedirectOperators.Count == 0) {
                StdoutRedirectOperators.Add(new RedirectOperators.Operator(null, false, true));
            }
            if (StderrRedirectOperators.Count == 0) {
                StderrRedirectOperators.Add(new RedirectOperators.Operator(null, false, false));
            }
        }

        void AddArgument(string arg, List<string> redirectArgs)
        {
            var expectingRedirectTarget = (redirectArgs.Count & 1) == 1;
            if (expectingRedirectTarget) {
                redirectArgs.Add(arg);
                return;
            }

            var redirectOperators = new string[] {
                ">",
                "1>",
                ">>",
                "1>>",
                "2>",
                "2>>",
            };
            if (Array.IndexOf(redirectOperators, arg) != -1) {
                redirectArgs.Add(arg);
                return;
            }

            if (arg.Contains(' ')) {
                arg = $"\"{arg}\"";
            }

            if (ExeArgs != "") {
                ExeArgs += ' ';
            }
            ExeArgs += arg;
        }

        public bool ValidateExePath()
        {
            if (exeFound_) {
                return File.Exists(ExePath);
            }

            // Search for the file as provided
            var searchOpts =
                PathSearchOptions.TryPathEnvVar |
                PathSearchOptions.PathMustExist |
                PathSearchOptions.PathMustBeAFile;
            var validExePath = FileUtil.GetValidPath(ExePath, WorkingDir, searchOpts);
            if (validExePath != null) {
                ExePath = Path.GetFullPath(validExePath);
                exeFound_ = true;
                return true;
            }

            // If it couldn't be found and the provided path has no extension,
            // try adding common executable extensions.
            var hasExtension = true;
            try { hasExtension = Path.GetExtension(ExePath) != ""; }
            catch (System.ArgumentException) { // e.g., invalid path characters in string
                return false;
            }

            if (!hasExtension) {
                var executableExtensions = new string[] {
                    ".exe", ".com", ".bat", ".cmd", ".vbs", ".vbe",
                    ".js", ".jse", ".wsf", ".wsh", ".msc"
                };
                foreach (var ext in executableExtensions) {
                    validExePath = FileUtil.GetValidPath(ExePath + ext, WorkingDir, searchOpts);
                    if (validExePath != null) {
                        ExePath = Path.GetFullPath(validExePath);
                        exeFound_ = true;
                        return true;
                    }
                }
            }

            return false;
        }

        public string Description { get {
            return String.Format("{0}>\"{1}\"{2}{3}{4}",
                WorkingDir,
                ExePath,
                ExeArgs == "" ? "" : (" " + ExeArgs),
                StdoutRedirectOperators.Arguments,
                StderrRedirectOperators.Arguments);
        }}
    }

    public class CommandRunner {
        protected Command command_;
        StreamWriter[] stdoutRedirectWriters_;
        StreamWriter[] stderrRedirectWriters_;

        public string Description { get { return command_.Description; } }

        public CommandRunner(
            Command cmd)
        {
            command_ = cmd;
            stdoutRedirectWriters_ = new StreamWriter [cmd.StdoutRedirectOperators.Count];
            stderrRedirectWriters_ = new StreamWriter [cmd.StderrRedirectOperators.Count];
        }

        static void OpenRedirectTargets(RedirectOperators ops, StreamWriter[] writers)
        {
            for (int i = 0; i < writers.Length; ++i) {
                var path = ops[i].TargetPath;
                if (path != null) {
                    writers[i] = new StreamWriter(path);
                }
            }
        }

        static void CloseRedirectTargets(StreamWriter[] writers)
        {
            for (int i = 0; i < writers.Length; ++i) {
                var writer = writers[i];
                if (writer != null) {
                    writer.Close();
                    writer.Dispose();
                    writers[i] = null;
                }
            }
        }

        public virtual int Run(bool printTime = false)
        {
            if (!command_.ValidateExePath()) {
                ConsoleUtil.Error("error: specified executable not found: {0}", command_.ExePath);
                return -1;
            }

            Console.ForegroundColor = ConsoleUtil.HeaderColor;
            if (printTime) {
                Console.Write("{0}: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            }
            Console.WriteLine(ConsoleUtil.EscapeBraces(command_.Description));
            Console.ResetColor();

            var proc = new Process();
            proc.StartInfo.FileName         = command_.ExePath;
            proc.StartInfo.Arguments        = command_.ExeArgs;
            proc.StartInfo.UseShellExecute  = false;
            proc.StartInfo.CreateNoWindow   = true;
            proc.StartInfo.WorkingDirectory = command_.WorkingDir;

            if (command_.StdoutRedirectOperators.Count > 0) {
                proc.StartInfo.RedirectStandardOutput = true;
                proc.OutputDataReceived += StdoutDataReceived;
                OpenRedirectTargets(command_.StdoutRedirectOperators, stdoutRedirectWriters_);
            }
            if (command_.StderrRedirectOperators.Count > 0) {
                proc.StartInfo.RedirectStandardError = true;
                proc.ErrorDataReceived += StderrDataReceived;
                OpenRedirectTargets(command_.StderrRedirectOperators, stderrRedirectWriters_);
            }

            try { proc.Start(); }
            catch (System.ComponentModel.Win32Exception) {
                ConsoleUtil.Error("error: could not execute command: {0}", command_.ExePath);
                return -1;
            }

            if (proc.StartInfo.RedirectStandardError) {
                proc.BeginErrorReadLine();
            }
            if (proc.StartInfo.RedirectStandardOutput) {
                proc.BeginOutputReadLine();
            }

            proc.WaitForExit();

            if (proc.StartInfo.RedirectStandardOutput) {
                CloseRedirectTargets(stdoutRedirectWriters_);
            }
            if (proc.StartInfo.RedirectStandardError) {
                CloseRedirectTargets(stderrRedirectWriters_);
            }

            var exitCode = proc.ExitCode;
            ConsoleUtil.Header("exit code = {0}", exitCode);
            return exitCode;
        }

        void StdoutDataReceived(object sender, DataReceivedEventArgs e) { Output(e.Data, stdoutRedirectWriters_); }
        void StderrDataReceived(object sender, DataReceivedEventArgs e) { Output(e.Data, stderrRedirectWriters_); }
        void Output(string line, StreamWriter[] streamWriters)
        {
            if (string.IsNullOrEmpty(line)) return;

            var stdout = streamWriters == stdoutRedirectWriters_;
            var redirected = Array.Exists(streamWriters, w => w != null);

            FilterOutput(ref line, stdout, redirected);

            if (string.IsNullOrEmpty(line)) return;

            if (redirected)
            {
                line = ConsoleUtil.RemoveEscapeCodes(line);
                foreach (var writer in streamWriters)
                {
                    writer.WriteLine(line);
                }
            }
            else
            {
                ConsoleUtil.WriteLine(line);
            }
        }

        // Modify string prior to output, return false to cancel output
        protected virtual void FilterOutput(ref string line, bool stdout, bool redirected)
        {
        }
    }

    public class KeyPressCommandRunner : CommandRunner {
        public KeyPressCommandRunner(Command cmd)
            : base(cmd)
        {
            var th = new Thread(new ThreadStart(KeyPressThread));
            th.Start();
        }

        void KeyPressThread()
        {
            while (true) {
                Console.ReadKey(false);
                Run();
            }
        }
    }

    // NOTE: Change.Type values are relevant, if multiple change notifications are received during
    // the same batching window, notifications with a smaller Type value are overwritten.
    public class Change {
        public enum Type {
            Changed,
            Renamed,
            Deleted,
        };

        public Type type_;
        public string newPath_;
    };

    public class Watcher {
        List<FileSystemWatcher> watchers_ = new List<FileSystemWatcher>();
        ManualResetEvent changeEvent_ = new ManualResetEvent(false);
        Dictionary<string, Change> changes_ = new Dictionary<string, Change>();

        public bool PrintNotifications { get; set; }

        public bool Add(string watchPath)
        {
            var w = new FileSystemWatcher();

            try {
                w.Path = Path.GetDirectoryName(watchPath);
            } catch (ArgumentException) {
                ConsoleUtil.Error("error: invalid path for monitoring: {0}", watchPath);
                return false;
            }

            w.Filter = Path.GetFileName(watchPath);
            w.IncludeSubdirectories = false;
            w.NotifyFilter = NotifyFilters.LastWrite;
            w.Changed += Changed;
            w.Deleted += Deleted;
            w.Renamed += Renamed;

            try {
                var attr = File.GetAttributes(watchPath);
                if ((attr & FileAttributes.Directory) == FileAttributes.Directory) {
                    w.Path = watchPath;
                    w.Filter = "*.*";
                    w.IncludeSubdirectories = true;
                }
            } catch (ArgumentException) {}

            watchers_.Add(w);
            return true;
        }

        public void Clear()
        {
            EnableEvents(false);
            watchers_.Clear();
        }

        public void EnableEvents(bool enable)
        {
            foreach (var w in watchers_) {
                w.EnableRaisingEvents = enable;
            }
        }

        // FileSystemWatch is difficult to use because many common file events (even simple writes)
        // can trigger multiple change notifications, including duplicates.  Further,
        // writes/renames/etc. may not yet be completed when the notification is received, in which
        // case attempting to open the file may fail because the file is still locked by the
        // modifying process.
        //
        // To deal with this, notifications are queued over a period of time, and handled later.
        void HandleChange(Change.Type type, string path, string newPath)
        {
            if (PrintNotifications) {
                ConsoleUtil.Header(
                    "{0}: {1}: {2}",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    type,
                    path);
            }

            lock (changes_) {
                if (changes_.ContainsKey(path)) {
                    var change = changes_[path];
                    if ((int) type > (int) change.type_) {
                        change.type_    = type;
                        change.newPath_ = newPath;
                    }
                } else {
                    var change = new Change();
                    change.type_    = type;
                    change.newPath_ = newPath;
                    changes_.Add(path, change);
                }
            }

            // Notify WaitForChange() that we've received a change
            changeEvent_.Set();
        }

        void Changed(object sender, FileSystemEventArgs e) { HandleChange(Change.Type.Changed, e.FullPath,    e.FullPath); }
        void Renamed(object sender, RenamedEventArgs    e) { HandleChange(Change.Type.Renamed, e.OldFullPath, e.FullPath); }
        void Deleted(object sender, FileSystemEventArgs e) { HandleChange(Change.Type.Deleted, e.FullPath,    e.FullPath); }

        public Dictionary<string, Change> WaitForChange(TimeSpan notificationDelay)
        {
            // Wait until one change was observed, then wait for the notification delay to try and
            // collect all of the multiple change events that get triggered from one logical change,
            // then reset the change event.
            changeEvent_.WaitOne();
            Thread.Sleep(notificationDelay);
            changeEvent_.Reset();

            Dictionary<string, Change> changes = null;
            lock (changes_) {
                changes = new Dictionary<string, Change>(changes_);
                changes_.Clear();
            }
            return changes;
        }
    }
}
