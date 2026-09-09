module Fable.Cli.Fork.WatchEvents

open System
open System.Globalization

let compilationFinished (detectedAt: DateTime) (success: bool) =
    let timestamp =
        detectedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)

    let result =
        if success then
            "true"
        else
            "false"

    Console.Out.WriteLine(
        $"TREESHEET_FABLE_EVENT {{\"event\":\"compilation-finished\",\"detectedAt\":\"{timestamp}\",\"success\":{result}}}"
    )
