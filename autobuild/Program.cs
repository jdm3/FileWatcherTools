using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

using onchangedlib;

namespace autobuild
{
    class Program
    {
        static void AbortWithUsage(string message, params object[] objs)
        {
            if (message != null) {
                ConsoleUtil.Error(message, objs);
            }

            Console.Error.WriteLine("usage: autobuild [options] [command]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("options:");
            Console.Error.WriteLine("    --msbuild_args=\"arguments for msbuild\"");
            Console.Error.WriteLine("    --working_dir=\"working directory to use when running command\"");
            Console.Error.WriteLine();
            Console.Error.WriteLine("e.g., autobuild.exe --msbuild_args=\"/property:Configuration=Release\" --working_dir=\"c:\\\" foo.exe bar baz");

            Environment.Exit(1);
        }

        static string GetMSBuildPath()
        {
            var path = ConfigurationManager.AppSettings["MSBuildPath"];
            if (!File.Exists(path)) {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft Visual Studio",
                    "Installer",
                    "vswhere.exe");

                var proc = new Process();
                proc.StartInfo.FileName               = path;
                proc.StartInfo.Arguments              = "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe";
                proc.StartInfo.UseShellExecute        = false;
                proc.StartInfo.CreateNoWindow         = true;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.OutputDataReceived += new DataReceivedEventHandler((sender, e) => {
                    if (!String.IsNullOrEmpty(e.Data)) {
                        path = e.Data;
                    }
                });

                try { proc.Start(); }
                catch (System.ComponentModel.Win32Exception) {
                    ConsoleUtil.Abort("error: failed to locate MSBuild.exe, manually set path in autobuild.exe.config.");
                    return null;
                }

                proc.BeginOutputReadLine();
                proc.WaitForExit();

                if (!File.Exists(path)) {
                    ConsoleUtil.Abort("error: failed to locate MSBuild.exe, manually set path in autobuild.exe.config.");
                    return null;
                }

                ConsoleUtil.Warning("msbuild path: {0}", path);

                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                config.AppSettings.Settings.Remove("MSBuildPath");
                config.AppSettings.Settings.Add("MSBuildPath", path);
                config.Save();
            }

            return path;
        }

        class SlnWatcher
        {
            Watcher watcher_ = new Watcher();
            string slnPath_ = null;
            List<string> prjPath_ = new List<string>();
            List<string> srcPath_ = new List<string>();

            public bool PrintNotifications
            {
                get { return watcher_.PrintNotifications; }
                set { watcher_.PrintNotifications = value; }
            }

            public void WatchSolution(string slnPath)
            {
                watcher_.Clear();
                slnPath_ = null;
                prjPath_.Clear();
                srcPath_.Clear();

                ConsoleUtil.Header("Solution to watch:");
                Console.WriteLine("    {0}", slnPath);

                slnPath_ = slnPath;
                watcher_.Add(slnPath);

                ParseSolution(slnPath);

                watcher_.EnableEvents(true);
            }

            void ParseSolution(string slnPath)
            {
                string contents;
                using (var slnReader = new StreamReader(slnPath)) {
                    contents = slnReader.ReadToEnd();
                    slnReader.Close();
                }

                var match = Regex.Match(
                    contents,
                    "Project\\(\"\\{([^}]*)\\}\"\\) = \"[^\"]*\", \"([^\"]*)\", \"\\{[^}]*\\}\"");
                if (match.Success) {
                    var ignoredProjectTypes = new List<string> {
                        "2150E333-8FDC-42A3-9474-1A3956D46DE8", // Solution folder
                    };

                    // Add all found found projects...
                    do
                    {
                        var prjType = match.Groups[1].ToString();
                        var prjRelPath = match.Groups[2].ToString();
                        match = match.NextMatch();

                        if (!ignoredProjectTypes.Contains(prjType, StringComparer.OrdinalIgnoreCase)) {
                            var prjPath = Path.Combine(Path.GetDirectoryName(slnPath), prjRelPath);
                            prjPath_.Add(prjPath);
                            watcher_.Add(prjPath);
                            ParseProject(prjPath);
                        }
                    } while (match.Success);
                } else {
                    // No Projects found in file, maybe this is a project file?
                    ParseProject(slnPath);
                }

                if (prjPath_.Count + srcPath_.Count > 0) {
                    ConsoleUtil.Header("Dependencies to watch:");
                    foreach (var fullpath in prjPath_) {
                        var relpath = fullpath.Replace(Environment.CurrentDirectory, ".");
                        Console.WriteLine("    {0}", relpath);
                    }
                    foreach (var fullpath in srcPath_) {
                        var relpath = fullpath.Replace(Environment.CurrentDirectory, ".");
                        Console.WriteLine("    {0}", relpath);
                    }
                }
            }

            void ParseProject(string prjPath)
            {
                var prjDir = Path.GetDirectoryName(prjPath);

                try {
                    using (var prjReader = new StreamReader(prjPath)) {
                        using (var xmlReader = XmlReader.Create(prjReader)) {
                            while (xmlReader.Read()) {
                                var srcPath = xmlReader.GetAttribute("Include");
                                if (srcPath == null) continue;
                                try { srcPath = Path.Combine(prjDir, srcPath); }
                                catch (ArgumentException) { continue; }
                                if (!File.Exists(srcPath)) continue;

                                srcPath_.Add(srcPath);
                                watcher_.Add(srcPath);
                            }
                            xmlReader.Close();
                        }
                        prjReader.Close();
                    }
                }
                catch(Exception e) {
                    ConsoleUtil.Abort(e.Message);
                }
            }

            public void WaitForChange()
            {
                var notificationDelay = new TimeSpan(200 * 1000 * 10);

                for (;;) {
                    var changes = watcher_.WaitForChange(notificationDelay);

                    var build = false;
                    var parse = false;
                    foreach (var e in changes) {
                        var path = e.Key;
                        var change = e.Value;

                        if (path == slnPath_) {
                            switch (change.type_) {
                            case Change.Type.Changed:
                                parse = true;
                                build = true;
                                break;
                            case Change.Type.Renamed:
                                slnPath_ = change.newPath_;
                                parse = true;
                                build = true;
                                break;
                            case Change.Type.Deleted:
                                ConsoleUtil.Abort("Solution '{0}' was deleted; you must restart to use a different solution.", path);
                                break;
                            }
                        } else if (prjPath_.Contains(path)) {
                            switch (change.type_) {
                            case Change.Type.Changed:
                                parse = true;
                                build = true;
                                break;
                            case Change.Type.Renamed:
                            case Change.Type.Deleted:
                                // wait for a solution change to know what to do...
                                break;
                            }
                        } else {
                            switch (change.type_) {
                            case Change.Type.Changed:
                                build = true;
                                break;
                            case Change.Type.Renamed:
                            case Change.Type.Deleted:
                                // wait for a solution change to know what to do...
                                break;
                            }
                        }
                    }

                    if (parse) {
                        WatchSolution(slnPath_);
                    }

                    if (build) {
                        break;
                    }
                }
            }
        }

        class BuildProcRunner : CommandRunner
        {
            public CommandRunner TestCommand = null;

            Regex errorRegex_ = new Regex(": (warning|error) C[0123456789]+:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            bool outputContainsError_;
            int numLinesOutputSinceFirstError_;

            public BuildProcRunner(string msbuildExe, string msbuildArgs, string slnPath)
                : base(new Command(
                    Environment.CurrentDirectory,
                    new string[] { msbuildExe },
                    0))
            {
                base.command_.ExeArgs = String.Format(
                    "/nologo /maxcpucount /verbosity:minimal {0} {1}",
                    msbuildArgs,
                    slnPath);

                var th = new Thread(new ThreadStart(KeyPressThread));
                th.Start();
            }

            void KeyPressThread()
            {
                while (true) {
                    var keyInfo = Console.ReadKey(false);
                    if (keyInfo.Key == ConsoleKey.B) {
                        Run(true);
                    } else {
                        if (TestCommand != null) {
                            TestCommand.Run(true);
                        }
                    }
                }
            }

            public override int Run(bool printTime = false)
            {
                outputContainsError_ = false;
                numLinesOutputSinceFirstError_ = 2; // initialize to 2 to leave space for final status output

                var exitCode = base.Run(printTime);
                if (exitCode != 0) {
                    ConsoleUtil.Error("FAIL!");
                }

                // Woken up by change to build dependency; start a build.  If
                // it succeeds, then run the test command.
                if (exitCode == 0 && TestCommand != null) {
                    TestCommand.Run(printTime);
                }

                return exitCode;
            }

            protected override void FilterOutput(ref string line, bool stdout, bool redirected)
            {
                // Stop outputing if we fill the window with errors... assumes we only care about
                // the first errors.
                if (numLinesOutputSinceFirstError_ >= Console.WindowHeight) {
                    line = null;
                    return;
                }

                var hasWarning = line.IndexOf(": warning ") != -1;
                var hasError   = line.IndexOf(": error ") != -1;

                if (!outputContainsError_ && (hasWarning || hasError)) {
                    outputContainsError_ = true;
                }

                if (outputContainsError_) {
                    numLinesOutputSinceFirstError_ += (line.Length + Console.WindowWidth - 1) / Console.WindowWidth;
                }

                if (hasError) {
                    line = $"\u001b[91m{line}\u001b[0m";
                } else if (hasWarning) {
                    line = $"\u001b[93m{line}\u001b[0m";
                }

                if (numLinesOutputSinceFirstError_ >= Console.WindowHeight) {
                    line = $"\u001b[93mwarning: further output excluded...\u001b[0m";
                }
            }
        }

        static void Main(string[] args)
        {
            ConsoleUtil.SetColors();

            string msbuildArgs = "";
            string testWorkingDir = Environment.CurrentDirectory;
            string slnPath = null;
            int argIndex = 0;
            for (; argIndex < args.Length; ++argIndex) {
                // Convert to lower-case to simplify search
                var arg = args[argIndex].ToLower();

                // Extract the msbuild command line arguments.  This may be a
                // quoted string if the user wants to provide multiple msbuild
                // arguments.  Also, it may include a particular solution file
                // path to use.
                if (arg.StartsWith("--msbuild_args=")) {
                    var margs = args[argIndex].Substring(15);
                    margs.Trim();
                    margs.Trim(new char[] { '\"' });

                    foreach (var marg in margs.Split(' ')) {
                        if (File.Exists(marg)) {
                            if (slnPath != null) throw new Exception();
                            slnPath = Path.GetFullPath(marg);
                        } else {
                            msbuildArgs = msbuildArgs + " " + marg;
                        }
                    }

                    continue;
                }

                // Extract a working directory for the test command
                if (arg.StartsWith("--working_dir=")) {
                    testWorkingDir = arg.Substring(14);
                    testWorkingDir.Trim();
                    testWorkingDir.Trim(new char[] { '\"' });
                    continue;
                }

                if (arg == "--help" || arg == "-h" || arg == "/?") {
                    AbortWithUsage(null);
                }

                break;
            }

            // If the user didn't specify a solution file in the msbuild
            // arguments, search the working directory for the first .sln file
            // to use.
            if (slnPath == null) {
                var files = Directory.GetFiles(Environment.CurrentDirectory);
                slnPath = Array.Find(files, s => Path.GetExtension(s) == ".sln");
                if (slnPath == null) {
                    AbortWithUsage(
                        "error: no solution provided in msbuild arguments, and could not find\n" +
                        "       any '.sln' files in the current working directory.");
                }
            }

            // Create a build process runner with the msbuild parameters
            var msbuild = GetMSBuildPath();
            var build = new BuildProcRunner(msbuild, msbuildArgs, slnPath);

            // Create a test process runner with any remaining arguments
            if (argIndex < args.Length) {
                build.TestCommand = new CommandRunner(new Command(
                    testWorkingDir,
                    args,
                    argIndex));

                // Don't validate exe path yet, likely it won't be created
                // until after the build.  Validation will be deferred to the
                // first run.

                ConsoleUtil.Header("Test command:");
                Console.WriteLine("    {0}", build.TestCommand.Description);
            }

            // Create a file watcher and start watching the provided solution
            // file and any dependencies.
            var w = new SlnWatcher();
            w.PrintNotifications = true;
            w.WatchSolution(slnPath);

            // Enter main loop of the application, then immediately go to sleep
            // until there is a change to the solution file or one of its
            // dependencies.
            for (;;) {
                w.WaitForChange();

                // Woken up by change to build dependency; start a build.  If
                // it succeeds, it will also run the test command.
                build.Run(true);
            }
        }
    }
}
