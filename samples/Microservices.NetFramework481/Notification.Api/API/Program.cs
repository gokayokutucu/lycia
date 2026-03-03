using Microsoft.Owin.Hosting;
using System;

namespace Sample.Notification.NetFramework481.API;

public class Program
{
    public static void Main(string[] args)
    {
        var port = Environment.GetEnvironmentVariable("PORT") ?? "6000";
        var url = $"http://localhost:{port}/";

        Console.WriteLine($"Starting Notification API on {url}...");

        using (WebApp.Start<Startup>(url))
        {
            Console.WriteLine($"✅ Notification API running at {url}");
            Console.WriteLine("Press Enter to stop...");
            Console.ReadLine();
        }
    }
}
