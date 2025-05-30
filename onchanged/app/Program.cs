using System;
using System.IO;

using onchangedlib;

namespace onchanged {
    class Program {
        static void AbortWithUsage(string message, params object[] objs)
        {
            if (message != null) {
                ConsoleUtil.Error(message, objs);
            } else {
                Console.Error.WriteLine("Run a command whenever a file (or files in a directory) are changed.");
                Console.Error.WriteLine();
            }

            Console.Error.WriteLine("usage: onchanged [options] path command...");
            Console.Error.WriteLine("options:");
            Console.Error.WriteLine("    --delay=T   Batch up notifications for T milliseconds before handling them.");
            Environment.Exit(1);
        }

        static void Main(string[] args)
        {
            ConsoleUtil.SetColors();

            var watcher = new Watcher();
            watcher.PrintNotifications = true;

            var notificationDelay = new TimeSpan(200 * 1000 * 10);

            int argIndex = 0;
            for (; argIndex < args.Length; argIndex += 1) {
                var arg = args[argIndex].ToLower();
                if (arg.StartsWith("--delay=")) {
                    var delayS = arg.Substring(8);
                    if (!int.TryParse(delayS, out var delay)) {
                        AbortWithUsage("error: invalid delay milliseconds: {0}", delayS);
                    }
                    notificationDelay = new TimeSpan(delay * 1000 * 10);
                    continue;
                }
                if (arg == "-h" || arg == "--help") { AbortWithUsage(null); }
                break;
            }

            if (args.Length - argIndex < 2) {
                AbortWithUsage(null);
            }

            // Get path to file/directory to watch
            //
            // TODO: Currently only support already-existing paths (otherwise, it's not properly
            // handling when a file vs directory gets created later).
            var watchPath = FileUtil.GetValidPath(
                args[argIndex],
                Environment.CurrentDirectory,
                PathSearchOptions.PathMustExist);
            if (watchPath == null) {
                AbortWithUsage("error: the watch path must be an existing, valid path: {0}", args[argIndex]);
            }

            // Get command to execute when change observed
            var command = new Command(
                Environment.CurrentDirectory,
                args, argIndex + 1);
            if (!command.ValidateExePath()) {
                AbortWithUsage("error: the command must start with an executable file: {0}", args[argIndex + 1]);
            }

            ConsoleUtil.Header("Watching: {0}", watchPath);
            ConsoleUtil.Header("Command:  {0}", command.Description);

            if (!watcher.Add(watchPath)) {
                Environment.Exit(1);
            }

            var cmdRunner = new KeyPressCommandRunner(command);

            watcher.EnableEvents(true);
            for (;;) {
                var changes = watcher.WaitForChange(notificationDelay);

                cmdRunner.Run(true);
            }
        }
    }
}
