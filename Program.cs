using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ImageLoader
{
    class Program
    {
        const string BASE_URL = "https://img.tyt.by/i/by5/weather/d/";
        const string OUTPUT_DIR = "downloads\\";
        const int MIN_NUMBER = 1;
        const int MAX_NUMBER = 35;
        static readonly ConcurrentDictionary<string, object> _fileLocks = new ConcurrentDictionary<string, object>();

        static void Main(string[] args)
        {
            InitArgs(args, out var requestsCount, out var bulkSize);

            var random = new Random();
            if (!Directory.Exists(OUTPUT_DIR))
            {
                Directory.CreateDirectory(OUTPUT_DIR);
            }

            var requestsInfo = new ConcurrentQueue<RequestInfo>();
            var semaphore = new SemaphoreSlim(bulkSize);
            var stopwatchTotal = new Stopwatch();
            var doneRequestsCount = 0;

            stopwatchTotal.Start();

            Parallel.ForEach(Enumerable.Range(0, requestsCount), async i =>
            {
                var stopwatch = new Stopwatch();
                var fileName = $"{random.Next(MIN_NUMBER, MAX_NUMBER + 1)}.png";
                var startThreadId = Thread.CurrentThread.ManagedThreadId;

                await semaphore.WaitAsync();
                stopwatch.Start();

                var webClient = new WebClient() { BaseAddress = BASE_URL };
                var data = await webClient.DownloadDataTaskAsync(fileName);

                stopwatch.Stop();
                semaphore.Release();

                var contentLength = webClient.ResponseHeaders[HttpResponseHeader.ContentLength];
                var endThreadId = Thread.CurrentThread.ManagedThreadId;
                var savePath = OUTPUT_DIR + fileName;

                requestsInfo.Enqueue(new RequestInfo(i, startThreadId, endThreadId));
                Console.WriteLine($"Request #{i}\tStart thread ID: {startThreadId}\tEnd thread ID: {endThreadId}\tURL: .../{fileName}\t    Size: {contentLength} bytes\tTime: {stopwatch.ElapsedMilliseconds} ms");
                WriteToFile(savePath, data);
                Interlocked.Increment(ref doneRequestsCount);
            });

            while (doneRequestsCount < requestsCount)
            {
                Thread.Sleep(100);
            }

            stopwatchTotal.Stop();

            var startThreads = string.Join(", ", requestsInfo.Select(x => x.StartThreadId).Distinct().OrderBy(x => x));
            var endThreads = string.Join(", ", requestsInfo.Select(x => x.EndThreadId).Distinct().OrderBy(x => x));
            
            Console.WriteLine($"Start thread IDs: {startThreads}");
            Console.WriteLine($"End thread IDs:   {endThreads}");
            Console.WriteLine($"Total time: {stopwatchTotal.ElapsedMilliseconds} ms");
        }

        static void InitArgs(string[] args, out int requestsCount, out int bulkSize)
        {
            requestsCount = InitArgument(args, 0, "Requests count", 100);
            bulkSize = InitArgument(args, 1, "Bulk size", 100);
            Console.WriteLine($"Requests count: {requestsCount}, Bulk size: {bulkSize}");
        }

        //Try to get parameter from command line arguments or ask user to type it
        static int InitArgument(string[] args, int order, string name, int defaultValue)
        {
            var result = defaultValue;

            if (args.Length > order && int.TryParse(args[order], out var arg))
            {
                result = arg;
            }
            else
            {
                Console.Write($"{name}: ");
                if (int.TryParse(Console.ReadLine(), out var r))
                {
                    result = r;
                }
            }
            return result;
        }

        static void WriteToFile(string path, byte[] data)
        {
            lock (_fileLocks.GetOrAdd(path, new object()))
            {
                var hasError = false;
                var attemptsCount = 10;
                for (var i = 0; i < attemptsCount; i++)
                {
                    hasError = false;
                    try
                    {
                        using (var writer = new BinaryWriter(File.OpenWrite(path)))
                        {
                            writer.Write(data);
                        }
                    }
                    catch
                    {
                        hasError = true;
                    }
                    if (!hasError)
                    {
                        if (i > 0)
                        {
                            Console.WriteLine($"Successful write to {path} after {i + 1} attempts");
                        }
                        break;
                    }
                }
                if (hasError)
                {
                    Console.WriteLine($"Can not write to {path} after {attemptsCount} attempts ");
                }
            }
        }

        class RequestInfo
        {
            public int RequestId { get; set; }
            public int StartThreadId { get; set; }
            public int EndThreadId { get; set; }

            public RequestInfo(int requestId, int startThreadId, int endThreadId)
            {
                RequestId = requestId;
                StartThreadId = startThreadId;
                EndThreadId = endThreadId;
            }
        }
    }
}
