module Fable.Cli.Fork.WatcherChanges

open System
open System.Collections.Generic

/// Drain change messages that are already queued when a compilation finishes.
/// Messages arriving after the non-blocking receive remain in the mailbox and
/// are handled by the next compilation cycle.
let rec drainPending
    (tryReceive: unit -> Async<'message option>)
    (getChanges: 'message -> DateTime * ISet<string>)
    (timestamp: DateTime)
    (changes: ISet<string>)
    =
    async {
        let! next = tryReceive ()

        match next with
        | None -> return timestamp, changes
        | Some message ->
            let messageTimestamp, messageChanges = getChanges message
            let mergedChanges = HashSet<string>(StringComparer.OrdinalIgnoreCase)

            for change in changes do
                mergedChanges.Add(change) |> ignore

            for change in messageChanges do
                mergedChanges.Add(change) |> ignore

            return! drainPending tryReceive getChanges messageTimestamp (mergedChanges :> ISet<string>)
    }
