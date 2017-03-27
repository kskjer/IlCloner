using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IlCloningTests;

namespace IlCloningGenerator.Benchmarking
{
    public class Benchmarker
    {
        public static Action Benchmark<T>(T obj, int countToRun = 1000000, bool cloneProps = true)
            where T : class, ICloneable
        {
            var message = "";
            var timer = new Stopwatch();
            timer.Start();

            IlCloner.Default.ShouldCloneProperties = cloneProps;
            IlCloner.Default.UseExistingCloners = false;

            var example = obj;

            var cloneGenStart = timer.Elapsed;
            var cloner = IlCloner.CreateCloner<T>();

            message +=
                $"{(timer.Elapsed - cloneGenStart).TotalMilliseconds.ToString("n2")}ms to generate cloner." +
                Environment.NewLine;

            // Dry run
            cloner(example);
            example.Clone();

            var actions = new[]
            {
                new { Name = "IL Generated", Action = new Action(() => cloner(example)) },
                new { Name = "C# Clone() method", Action = new Action(() => example.Clone()) }
            };

            Action iteration = () =>
            {
                var deferred = new List<Action>();
                var maxClonesSec = 0.0;
                double? minClonesSec = null;

                var messages = actions.ToDictionary(a => a.Name, a => new List<string>());

                string fastest = null;

                foreach (var a in actions)
                {
                    GC.Collect();
                    System.Threading.Thread.Sleep(1000);

                    var startTime = timer.Elapsed;

                    for (var i = 0; i < countToRun; i++)
                        a.Action();

                    var totalTime = timer.Elapsed - startTime;
                    var clonesSec = (countToRun / totalTime.TotalSeconds);

                    maxClonesSec = Math.Max(maxClonesSec, clonesSec);

                    if (minClonesSec.HasValue)
                        minClonesSec = Math.Min(minClonesSec.Value, clonesSec);
                    else
                        minClonesSec = clonesSec;

                    deferred.Add(
                        () =>
                        {
                            messages[a.Name].Add($"{clonesSec.ToString("n2")} clones per second.");

                            if (clonesSec != maxClonesSec)
                            {
                                var frac = (clonesSec / maxClonesSec);

                                messages[a.Name].Add($"{(100.0 - frac * 100.0).ToString("0.00")}% ({frac.ToString("0.00")} times) slower than best performer.");
                            }

                            if (clonesSec != minClonesSec.Value)
                            {
                                var frac = (clonesSec / minClonesSec).Value;

                                messages[a.Name].Add($"{(frac * 100.0 - 100.0).ToString("0.00")}% ({frac.ToString("0.00")} times) faster than worst performer.");
                            }

                            if (clonesSec == maxClonesSec)
                                fastest = a.Name;
                        }
                    );
                }

                deferred.ForEach(d => d());

                var nameFieldLength = actions.Max(a => a.Name.Length) + 4;

                message += string.Join(
                    Environment.NewLine + Environment.NewLine,
                    actions
                        .Select(a =>
                        {
                            return string.Join(
                                Environment.NewLine,
                                messages[a.Name].Select((m, idx) =>
                                {
                                    if (idx == 0)
                                        return $"{a.Name}:".PadRight(nameFieldLength) + m;
                                    else if (idx == 1 && a.Name == fastest)
                                        return "".PadRight(a.Name.Length, '^').PadRight(nameFieldLength) + m;
                                    else
                                        return "".PadRight(nameFieldLength) + m;
                                })
                            );
                        })
                );

                Console.WriteLine(message);
                Console.ReadLine();

                message = "";
            };

            iteration();

            return iteration;
        }
    }
}
