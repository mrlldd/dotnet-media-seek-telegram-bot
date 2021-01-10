module TelegramAudioFinder.Program

open System
open Suave
open Operators
open TelegramAudioFinder.Startup
open TelegramAudioFinder.Utils


let isCtrlC (consoleKeyInfo: ConsoleKeyInfo) =
    consoleKeyInfo.Key = ConsoleKey.C
    && consoleKeyInfo.Modifiers = ConsoleModifiers.Control

let rec untilCloseCombination closeCombination =
    if not (Console.ReadKey true |> closeCombination)
    then untilCloseCombination closeCombination

[<EntryPoint>]
let main _ =
    let _, server = startupAsync cts.Token |> Async.RunSynchronously
    Async.Start(server, cts.Token)
    untilCloseCombination isCtrlC
    printfn "Shutting down..."
    cts.Cancel(false)
    0
