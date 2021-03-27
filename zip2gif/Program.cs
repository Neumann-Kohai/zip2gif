﻿using ShellProgressBar;
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
        static int delay = 30;
        static int bitDepth = 8;
        static bool ignore = false;

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
            string path = "";
            string output = "";
            bool recursive = false;
            {
                RootCommand root = new RootCommand("A simple Program to convert zip -> gif");
                root.AddArgument(new Argument<string>("path", getDefaultValue: Directory.GetCurrentDirectory, description: "Path to eather the folder containgig zip files or a path to a specific zip file"));
                root.AddOption(new Option<bool>(new[] { "-r", "--recursive" }, description: "If set subdirektory are search for zip files"));
                root.AddOption(new Option<int>(new[] { "-fps", "--framerate" },() => -1, description: "set Framerate"));
                root.AddOption(new Option<int>(new[] {"--delay"},() => -1, description:"set delay between frames"));
                root.AddOption(new Option<bool>(new[] { "-i", "--ignore" }, description: "if set animation.json is ignored"));
                root.AddOption(new Option<string>(new[] { "-o", "--output" }, () => "", description: "set to change output path, default same as zip files"));
                root.AddOption(new Option<int>(new[] { "-bit" }, () => 8, description: "Collor deapth, Eather 4 or 8 bit"));

                root.Handler = CommandHandler.Create<string, bool, int, int, bool, string, int>((string p, bool r, int f, int d, bool i, string o, int b)=> 
                {
                    if (f > 0)
                        delay = 1000 / f;
                    else if (d > 0)
                        delay = d;
                    ignore = i;
                    path = Path.GetFullPath(p);
                    recursive = r;
                    output = o;
                    if (b != 4 || b != 8)
                    {
                        Console.WriteLine("Invalid Pixel deapth");
                        return;
                    }
                    bitDepth = b / 4;
                });
                root.Invoke(unparsedArgs);
            }

            Console.WriteLine("Debug");
            Console.WriteLine(path);
            Console.WriteLine(recursive);
            Console.WriteLine(delay);
            Console.WriteLine(ignore);
            Console.WriteLine(output);
            Console.WriteLine(bitDepth);


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