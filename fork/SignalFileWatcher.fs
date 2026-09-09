module Fable.Cli.Fork.SignalFileWatcher

open System
open System.Collections.Generic
open System.IO

open Fable.Cli.FileWatcher
open Fable.Compiler.Util

let private usePollingWatcher () =
    match Environment.GetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER") with
    | value when
        not (String.IsNullOrWhiteSpace(value))
        && value.Equals("1", StringComparison.OrdinalIgnoreCase)
        ->
        true
    | value when
        not (String.IsNullOrWhiteSpace(value))
        && value.Equals("true", StringComparison.OrdinalIgnoreCase)
        ->
        true
    | _ -> false

let private createWatcher signalFileName : IFileSystemWatcher =
    if usePollingWatcher () then
        upcast new ResetablePollingFileWatcher([ signalFileName ], [ "(?i)node_modules"; "(?i)bin"; "(?i)obj"; "\..+" ])
    else
        upcast new DotnetFileWatcher([ signalFileName ])

/// Watches one file and emits the current project file set when it changes.
/// The source tree is deliberately not watched in this mode: touching the
/// signal file is the only event that releases a compilation cycle.
type Watcher(delayMs: int, signalFilePath: string) =
    let signalFilePath = Path.GetFullPath(signalFilePath)
    let signalDirectory = Path.GetDirectoryName(signalFilePath)
    let signalFileName = Path.GetFileName(signalFilePath)
    let watcher = createWatcher signalFileName

    do
        if
            String.IsNullOrWhiteSpace(signalDirectory)
            || not (Directory.Exists(signalDirectory))
        then
            invalidArg (nameof signalFilePath) $"Signal file directory does not exist: {signalDirectory}"

        watcher.BasePath <- signalDirectory
        watcher.EnableRaisingEvents <- true

    member _.BasePath = watcher.BasePath

    member _.Observe(filesToCompile: string list) =
        let currentFiles =
            HashSet<string>(filesToCompile, StringComparer.OrdinalIgnoreCase) :> ISet<string>

        watcher.OnFileChange
        |> Observable.choose (fun path ->
            if String.Equals(Path.GetFullPath(path), signalFilePath, StringComparison.OrdinalIgnoreCase) then
                Some()
            else
                None
        )
        |> Observable.throttle delayMs
        |> Observable.map (fun _ -> currentFiles)
