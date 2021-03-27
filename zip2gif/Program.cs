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
        static string path = "";
        static string output = "";
        static int delay = 30;
        static AnimatedGif.GifQuality bitDepth = AnimatedGif.GifQuality.Bit8;
        static bool ignore = false;
        static bool recursive = false;

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
                new Argument<string>("path", getDefaultValue: Directory.GetCurrentDirectory, description: "Path to eather the folder containgig zip files or a path to a specific zip file"),
                new Option<string>(new[] { "-o", "--output" }, () => "", description: "set to change output path, default same as zip files"), 
                new Option<int>(new[] { "-fps", "--framerate" }, () => -1, description: "set Framerate"),
                new Option<int>(new[] { "--delay" }, () => -1, description: "set delay between frames"),  
                new Option<int>(new[] { "--bit" }, () => 8, description: "Collor deapth, Eather 4 or 8 bit"),
                new Option<bool>(new[] { "-r", "--recursive" }, description: "If set subdirektory are search for zip files"),
                new Option<bool>(new[] { "-i", "--ignore" }, description: "if set animation.json is ignored"),
            };
                root.Handler = CommandHandler.Create((string path, string output, int framerate, int delay, bool ignore, int bit, bool recursive) =>
                {
                    Console.WriteLine(path);
                    if (framerate > 0)
                        Program.delay = 1000 / framerate;
                    else if (delay > 0)
                        Program.delay = delay;
                    Program.ignore = ignore;
                    Program.path = Path.GetFullPath(path);
                    Program.recursive = recursive;
                    Program.output = output;
                    switch (bit)
                    {
                        case 0:
                            Program.bitDepth = AnimatedGif.GifQuality.Grayscale;
                            break;
                        case 4: 
                            Program.bitDepth = AnimatedGif.GifQuality.Bit4;
                            break;
                        case 8:
                            Program.bitDepth = AnimatedGif.GifQuality.Bit8;
                            break;
                        default:
                            Console.WriteLine("Invalid Pixel deapth");
                            return;
                    }
                });
                root.Invoke(unparsedArgs);
            }

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
                                gif.AddFrame(i, delay, bitDepth);
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