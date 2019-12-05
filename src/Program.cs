using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using MySql.Data.MySqlClient;

namespace testapp
{
    class Program
    {
        static void Main(string[] args)
        {
            var iterations = 100;
            var connectionString = Environment.GetEnvironmentVariable("MYSQL_CONNECTION");

            RunTest(iterations, connectionString, connectionPooling: false);
            RunTest(iterations, connectionString, connectionPooling: true);
        }

        private static void RunTest(int iterations, string connectionString, bool connectionPooling)
        {
            connectionString = connectionString + $";Pooling={connectionPooling}";
            Console.WriteLine($"Connection Pooling: {connectionPooling}");

            // Warmup
            var coldLatency = ExecuteRoundtrip(connectionString);
            Console.WriteLine($"Cold Connection Latency: {coldLatency.ConnectionElapsedMilliseconds}ms");
            Console.WriteLine($"Cold Command Latency: {coldLatency.CommandElapsedMilliseconds}ms");

            // Average over multiple executions
            var results = new List<Latency>();
            for (int i = 0; i < iterations; i++)
            {
                results.Add(ExecuteRoundtrip(connectionString));
            }
            Console.WriteLine($"Warm Iterations: {iterations}");
            Console.WriteLine($"Avg. Connection Latency: {results.Average(_ => _.ConnectionElapsedMilliseconds)}ms");
            Console.WriteLine($"Avg. Command Latency: {results.Average(_ => _.CommandElapsedMilliseconds)}ms");
            
            Console.WriteLine();
        }

        private static Latency ExecuteRoundtrip(string connectionString)
        {
            var result = new Latency();

            var stopwatch = new Stopwatch();
            stopwatch.Restart();
            using (var connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                result.ConnectionElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

                stopwatch.Restart();
                using (var command = new MySqlCommand("SELECT 1;", connection))
                {
                    command.ExecuteScalar();
                    result.CommandElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                }
            }
            return result;
        }

        class Latency
        {
            public long ConnectionElapsedMilliseconds { get; set; }

            public long CommandElapsedMilliseconds { get; set; }
        }
    }
}
