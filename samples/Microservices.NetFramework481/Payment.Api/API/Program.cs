using Microsoft.Owin.Hosting;
using Sample.Payment.NetFramework481.API;
using System;

namespace Sample.Payment.NetFramework481.API;

public class Program
{
    public static void Main(string[] args)
    {
        var port = Environment.GetEnvironmentVariable("PORT") ?? "3000";
        var url = $"http://localhost:{port}/";

        Console.WriteLine($"Starting Payment API on {url}...");

        using (WebApp.Start<Startup>(url))
        {
            Console.WriteLine($"✅ Payment API running at {url}");
            Console.WriteLine("Press Enter to stop...");
            Console.ReadLine();
        }
    }
}
