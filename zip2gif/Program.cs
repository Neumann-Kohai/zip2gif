using ShellProgressBar;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;


namespace zip2gif
{
    internal class Program
    {
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

        private static void Main(string[] args)
        {
            Argument<string> a = new Argument<string>("path", Directory.GetCurrentDirectory).LegalFilePathsOnly();
            a.Description = "Path to a folder containing zip files or to a zip file";
            RootCommand root = new RootCommand
            {
                a,
                new Option<bool>(
                     new [] {"-r", "--recursive" },
                    description:"NotImplemented     if set the Program will also look in subdirectory for zip files")
            };
            root.Description = "A simple Program to convert zip -> gif";

            root.Handler = CommandHandler.Create<string, bool>(XxX);
            root.InvokeAsync(args).Wait();
        }

        private static void XxX(string path, bool recursive)
        {
            path = Path.GetFullPath(path);
            Console.WriteLine(recursive);
            Console.WriteLine(path);
            ProgressBar pbar = new ProgressBar(1, "Total", options);
            if (File.Exists(path) && path.EndsWith(".zip"))
                CreateGif((path, pbar, (CountdownEvent)null));
            else if (Directory.Exists(path))
            {
                string[] files = Directory.GetFiles(path);

                using (CountdownEvent countdown = new CountdownEvent(1))
                {
                    foreach (string file in files)
                    {
                        if (file.EndsWith(".zip"))
                        {
                            countdown.AddCount();
                            ThreadPool.QueueUserWorkItem(CreateGif, (file, pbar, countdown));
                        }
                    }
                    countdown.Signal();
                    pbar.MaxTicks = countdown.CurrentCount;
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
                    int delay = 30;

                    ZipArchiveEntry jsonFile = archive.GetEntry("animation.json");
                    if (jsonFile != null)
                    {
                        JsonEntry[] frames = JsonSerializer.Deserialize<JsonEntry[]>(new StreamReader(jsonFile.Open()).ReadToEnd());
                        delay = frames[0].delay;
                    }

                    if (archive.Entries.Count == 0)
                        return;

                    using (AnimatedGif.AnimatedGifCreator gif = AnimatedGif.AnimatedGif.Create(path.Remove(path.Length - 3) + "gif", delay, -1))
                    {
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
                    }
                    data.Item2.Tick();
                }
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