namespace Sortopia.Cli

open System
open System.Globalization
open System.IO

/// Stamp each report line at its source; redirected worker output is forwarded unchanged.
module internal CommandLineReport =
    let private gate = obj ()

    let private emit (writer: TextWriter) (message: string) =
        lock gate (fun () ->
            for line in message.Replace("\r\n", "\n").Split('\n') do
                let timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)
                writer.WriteLine($"[{timestamp}] {line}"))

    let write format = Printf.kprintf (fun message -> emit Console.Out message) format
    let error format = Printf.kprintf (fun message -> emit Console.Error message) format
