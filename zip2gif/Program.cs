using Gif.Components;
using ShellProgressBar;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace zip2gif
{
    internal class Program
    {
        static string path = "";
        static string output = "";
        static int delay = 30;
        static bool ignore = false;
        static bool recursive = false;
        static bool keep = false;

        private static readonly ProgressBarOptions options = new ProgressBarOptions
        {
            ForegroundColor = ConsoleColor.Yellow,
            ForegroundColorDone = ConsoleColor.DarkGreen,
            BackgroundColor = ConsoleColor.DarkGray,
            BackgroundCharacter = '\u2593',
            EnableTaskBarProgress = true,
        };
        private static readonly ProgressBarOptions childOptions = new ProgressBarOptions
        {
            ForegroundColor = ConsoleColor.Yellow,
            ForegroundColorDone = ConsoleColor.DarkGreen,
            BackgroundColor = ConsoleColor.DarkGray,
            BackgroundCharacter = '\u2593',
            CollapseWhenFinished = true,
            DenseProgressBar = true,
        };

        private static void Main(string[] unparsedArgs)
        {
            {
                RootCommand root = new RootCommand("A simple Program to convert zip -> gif"){
                new Argument<string>("path", getDefaultValue: Directory.GetCurrentDirectory, description: "Path to eather the folder containing zip files or a path to a specific zip file"),
                new Option<string>(new[] { "-o", "--output" }, Directory.GetCurrentDirectory, description: "Change output path"),
                new Option<int>(new[] { "-fps", "--framerate" }, () => -1, description: "set Framerate"),
                new Option<int>(new[] { "--delay" }, () => 30, description: "set delay between frames"),
                new Option<bool>(new[] { "-r", "--recursive" }, description: "If set subdirectory are search for zip files"),
                new Option<bool>(new[] { "-i", "--ignore" }, description: "if set animation.json is ignored"),
                new Option<bool>(new[]{ "-k", "--keep"}, description:"NotImplemented. keep file structure")
                };
                root.Handler = CommandHandler.Create((string path, string output, int framerate, int delay, bool ignore, bool recursive, bool keep) =>
                {
                    if (framerate > 0)
                        Program.delay = 1000 / framerate;
                    else
                        Program.delay = delay;
                    Program.ignore = ignore;
                    Program.path = Path.GetFullPath(path);
                    Program.recursive = recursive;
                    if (File.Exists(path) && path.EndsWith(".zip"))
                    {
                        Program.recursive = false;
                        Console.WriteLine("Recursive will be ignored");
                    }
                    Program.output = Path.GetFullPath(output) + (output.EndsWith('\\')?"":'\\');
                    Program.keep = keep;
                });
                root.InvokeAsync(unparsedArgs).Wait();
            }

            ProgressBar pbar = new ProgressBar(1, "Total", options);
            if (File.Exists(path) && path.EndsWith(".zip"))
                CreateGif((path, pbar, (CountdownEvent)null));
            else if (Directory.Exists(path))
            {
                string[] directorys = { Program.path };
                if (Program.recursive)
                    directorys = directorys.Concat(Directory.GetDirectories(path, "*", SearchOption.AllDirectories)).ToArray();
                using (CountdownEvent countdown = new CountdownEvent(1))
                {
                    foreach (var directory in directorys)
                    {
                        string[] files = Directory.GetFiles(directory);
                        foreach (string file in files)
                        {
                            if (file.EndsWith(".zip"))
                            {
                                countdown.AddCount();
                                ThreadPool.QueueUserWorkItem(CreateGif, (file, pbar, countdown));
                            }
                        }
                        pbar.MaxTicks = countdown.CurrentCount - 1;
                    }
                    pbar.Message = "Scan complete";
                    countdown.Signal();
                    countdown.Wait();
                }
            }

        }

        private static void CreateGif(object stateInfo)
        {
            (string, ProgressBar, CountdownEvent) data = ((string, ProgressBar, CountdownEvent))stateInfo;
            string path = data.Item1;

            if (!File.Exists(path))
                return;

            using (ZipArchive archive = ZipFile.OpenRead(path))
            {
                using (ChildProgressBar pbar = data.Item2.Spawn(archive.Entries.Count, "Waiting: " + Path.GetFileName(path), childOptions))
                {
                    int delay = Program.delay;

                    ZipArchiveEntry jsonFile = archive.GetEntry("animation.json");
                    if (jsonFile != null && !Program.ignore)
                    {
                        JsonEntry[] frames = JsonSerializer.Deserialize<JsonEntry[]>(new StreamReader(jsonFile.Open()).ReadToEnd());
                        delay = frames[0].delay;
                    }

                    if (archive.Entries.Count == 0)
                        return;

                    AnimatedGifEncoder gif = new AnimatedGifEncoder();

                    gif.SetRepeat(0);
                    gif.SetDelay(delay);
                    using (MemoryStream memStream = new MemoryStream())
                    {
                        gif.Start(memStream);
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            pbar.Tick($"{entry.Name}: {Path.GetFileName(path)}");
                            if (entry.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                            {
                                Image i = Image.FromStream(entry.Open());
                                gif.AddFrame(i);
                            }
                            pbar.Message = "Finished";
                        }
                        gif.Finish();
                        string outFile = keep?path.Remove(0, output.Length):Path.GetFileName(path);
                        outFile = output +  outFile.Remove(outFile.Length - 3) + "gif";
                        Directory.CreateDirectory(Path.GetDirectoryName(outFile));
                        File.WriteAllBytes(outFile, memStream.ToArray());
                    }
                }
                data.Item2.Tick($"{data.Item2.MaxTicks - data.Item3?.CurrentCount - 1} out of {data.Item2.MaxTicks - 1}");
                data.Item3?.Signal();
            }
        }

        private struct JsonEntry
        {
            public string file { get; set; }
            public int delay { get; set; }
        }
    }
}