using Microsoft.Owin.Hosting;
using System;

namespace Sample.Delivery.NetFramework481.API;

public class Program
{
    public static void Main(string[] args)
    {
        var port = Environment.GetEnvironmentVariable("PORT") ?? "4000";
        var url = $"http://localhost:{port}/";

        Console.WriteLine($"Starting Delivery API on {url}...");

        using (WebApp.Start<Startup>(url))
        {
            Console.WriteLine($"✅ Delivery API running at {url}");
            Console.WriteLine("Press Enter to stop...");
            Console.ReadLine();
        }
    }
}
