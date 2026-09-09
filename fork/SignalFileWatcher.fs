module Fable.Cli.Fork.SignalFileWatcher

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography

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

/// Watches one file and emits the files whose content changed since the last
/// signal. The source tree is deliberately not watched in this mode: touching
/// the signal file is the only event that releases a compilation cycle.
type Watcher(delayMs: int, signalFilePath: string) =
    let signalFilePath = Path.GetFullPath(signalFilePath)
    let signalDirectory = Path.GetDirectoryName(signalFilePath)
    let signalFileName = Path.GetFileName(signalFilePath)
    let watcher = createWatcher signalFileName
    let snapshotLock = obj ()
    let mutable filesToCompile = []
    let mutable knownSnapshot: Dictionary<string, string option> option = None

    let fileHash path =
        if File.Exists(path) then
            use stream = File.OpenRead(path)
            use sha256 = SHA256.Create()
            sha256.ComputeHash(stream) |> Convert.ToHexString |> Some
        else
            None

    let snapshot (files: string list) : Dictionary<string, string option> =
        let result = Dictionary<string, string option>(StringComparer.OrdinalIgnoreCase)

        for path in files do
            result.[path] <- fileHash path

        result

    let changedFiles
        (previous: Dictionary<string, string option>)
        (current: Dictionary<string, string option>)
        : ISet<string>
        =
        let paths = HashSet<string>(StringComparer.OrdinalIgnoreCase)
        let changes = HashSet<string>(StringComparer.OrdinalIgnoreCase)

        for KeyValue(path, _) in previous do
            paths.Add(path) |> ignore

        for KeyValue(path, _) in current do
            paths.Add(path) |> ignore

        for path in paths do
            let previousValue =
                match previous.TryGetValue(path) with
                | true, value -> Some value
                | false, _ -> None

            let currentValue =
                match current.TryGetValue(path) with
                | true, value -> Some value
                | false, _ -> None

            if previousValue <> currentValue then
                changes.Add(path) |> ignore

        changes :> ISet<string>

    do
        if
            String.IsNullOrWhiteSpace(signalDirectory)
            || not (Directory.Exists(signalDirectory))
        then
            invalidArg (nameof signalFilePath) $"Signal file directory does not exist: {signalDirectory}"

        watcher.BasePath <- signalDirectory
        watcher.EnableRaisingEvents <- true

    member _.BasePath = watcher.BasePath

    member _.Observe(filesToObserve: string list) =
        let currentFiles = filesToObserve |> List.map Path.GetFullPath

        lock
            snapshotLock
            (fun () ->
                filesToCompile <- currentFiles

                match knownSnapshot with
                | None -> knownSnapshot <- Some(snapshot currentFiles)
                | Some previous ->
                    let current = snapshot currentFiles

                    for path in previous.Keys |> Seq.toArray do
                        if not (current.ContainsKey(path)) then
                            previous.Remove(path) |> ignore

                    for KeyValue(path, value) in current do
                        if not (previous.ContainsKey(path)) then
                            previous.[path] <- value
            )

        watcher.OnFileChange
        |> Observable.choose (fun path ->
            if String.Equals(Path.GetFullPath(path), signalFilePath, StringComparison.OrdinalIgnoreCase) then
                Some()
            else
                None
        )
        |> Observable.throttle delayMs
        |> Observable.map (fun _ ->
            lock
                snapshotLock
                (fun () ->
                    let previous = knownSnapshot |> Option.defaultWith (fun () -> Dictionary())
                    let current = snapshot filesToCompile
                    let changes = changedFiles previous current
                    knownSnapshot <- Some current
                    changes
                )
        )
