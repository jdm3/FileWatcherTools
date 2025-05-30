# FileWatcherTools

A collection of tools based on window's file change notifications.

## onchanged

```bat
onchanged [options] path command...
```

`onchanged` will monitor changes to a `path` and, whenever the path changes or whenever a key is pressed, will run the specified command.  For example, `onchanged.exe bar.txt foo.exe bar.txt` will run `foo.exe bar.txt` whenever a key is pressed, or whenever `bar.txt` is created, modified, renamed, or deleted.

If the `path_to_watch` is a directory, then changes to the directory or any child file or directory are monitored.

To exit the program, press CTRL+C (or kill the process, or close the console window, etc.).

### Redirection operators

Redirection operators in the triggered command are not fully supported.  However, `>` and `>>` are supported provided they are escaped in
the command line using the `^` character (otherwise they apply to the onchanged command itself).  For example, `onchanged.exe bar.txt foo.exe bar.txt ^> stdout.txt` will run `foo.exe bar.txt > stdout.txt` whenever `bar.txt` is changed.

### Delaying the command

Windows can create multiple notifications for a single logical change, and the notifications can happen before the change is complete.  To deal with this, onchanged will delay a short amount of time before executing the triggered command.

In some cases, the default delay is insufficient and you can use the `--delay` command line option to override the default.  For example, the modifying process may be writing a large file and you need to wait longer to ensure that the file is completely written before running the command.  

## autobuild

```bat
autobuild [options] [command...]
options:
    --msbuild_args="arguments for msbuild"
    --working_dir="working directory to use when running command"
```

`autobuild` will monitor changes to a VisualStudio solution or project file, and all project and source dependencies listed in it.  When a change occurs, it will build the solution or project using `msbuild` and, if the build succeeds and a test command is provided, run the specified test command.

The solution or project file to monitor is found either in the `"arguments for msbuild"` or in the current directory.

Pressing the `b` key will trigger a build (and trigger the test command if successful).

Any other keys pressed will trigger the test command to run.

To exit the program, press CTRL+C (or kill the process, or close the console window, etc.).
