using IlCloningGenerator.Tests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IlCloningGenerator.Benchmarking
{
    class Program
    {
        static void Main(string[] args)
        {
            var benchmarks = new[]
            {
                new
                {
                    Name = "Standard Cloning",
                    Action = new Action(() =>
                    Benchmarker.Benchmark<ExampleClass>(
                        ExampleClass.GenerateExampleObject()
                    ))
                },
                new
                {
                    Name = "Non-Generic List",
                    Action = new Action(() =>
                    Benchmarker.Benchmark(
                        ExampleOldSchoolList.GenerateExampleObject()
                    ))
                }

            };

            Console.WriteLine("Which benchmark?");

            for (var i = 0; i < benchmarks.Length; i++)
                Console.WriteLine(" {0} - {1}", i, benchmarks[i].Name);

            var which = int.Parse(Console.ReadLine());


            while (true)
            {
                benchmarks[which].Action();
            }
        }
    }
}
