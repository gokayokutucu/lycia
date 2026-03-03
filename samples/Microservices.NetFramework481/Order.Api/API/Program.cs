using Microsoft.Owin.Hosting;
using System;

namespace Sample.Order.NetFramework481.API;

public class Program
{
    public static void Main(string[] args)
    {
        var port = Environment.GetEnvironmentVariable("PORT") ?? "1000";
        var url = $"http://localhost:{port}/";

        Console.WriteLine($"Starting Order API on {url}...");

        using (WebApp.Start<Startup>(url))
        {
            Console.WriteLine($"✅ Order API running at {url}");
            Console.WriteLine("Press Enter to stop...");
            Console.ReadLine();
        }
    }
}
